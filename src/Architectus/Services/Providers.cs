using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Architectus.Services;

public sealed record ProviderInfo(
    string Id,
    string DisplayName,
    string DefaultModel,
    string[] SuggestedModels,
    string KeyHint,
    string KeyUrl,
    string? FreeNote = null);

public static class ProviderCatalog
{
    public static readonly ProviderInfo OpenAI = new(
        "openai", "OpenAI", "gpt-5-mini",
        new[] { "gpt-5-nano", "gpt-5-mini", "gpt-4o-mini" },
        "Starts with sk-", "https://platform.openai.com/api-keys");

    public static readonly ProviderInfo Anthropic = new(
        "anthropic", "Anthropic (Claude)", "claude-haiku-5-5",
        new[] { "claude-haiku-5-5", "claude-sonnet-5-5" },
        "Starts with sk-ant-", "https://console.anthropic.com/settings/keys");

    public static readonly ProviderInfo Gemini = new(
        "gemini", "Google Gemini", "gemini-flash-lite-latest",
        new[] { "gemini-flash-lite-latest", "gemini-flash-latest" },
        "Starts with AIza", "https://aistudio.google.com/apikey",
        "Free option: get a key from Google AI Studio with just a Google account, no card needed. " +
        "The free tier has request limits per minute and per day, and Google may use free-tier requests to improve its products.");

    // Gemini first: it's the free way to use Architectus.
    public static IReadOnlyList<ProviderInfo> All { get; } = new[] { Gemini, OpenAI, Anthropic };

    public static ProviderInfo Get(string id) =>
        All.FirstOrDefault(p => p.Id == id) ?? Gemini;

    public static IPromptProvider CreateClient(string id) => id switch
    {
        "anthropic" => new AnthropicProvider(),
        "gemini" => new GeminiProvider(),
        _ => new OpenAiProvider(),
    };
}

public interface IPromptProvider
{
    IAsyncEnumerable<string> StreamAsync(string apiKey, string model, string system, string user, CancellationToken ct);
    Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct);
}

public sealed class ProviderException(string message) : Exception(message);

internal static class Http
{
    // One shared client. Streaming responses can run long, so no global timeout;
    // callers cancel with their CancellationToken instead.
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    static Http()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Architectus/0.1 (+https://github.com/arkbullet2626-droid/architectus)");
    }

    public static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>Turns an HTTP error into a short, human sentence. Never includes the key.</summary>
    public static async Task ThrowIfFailed(HttpResponseMessage res, string provider, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        string body = "";
        try { body = await res.Content.ReadAsStringAsync(ct); } catch { }
        string? detail = null;
        try
        {
            var node = JsonNode.Parse(body);
            detail = node?["error"]?["message"]?.GetValue<string>()
                  ?? node?[0]?["error"]?["message"]?.GetValue<string>();
        }
        catch { }

        string reason = (int)res.StatusCode switch
        {
            400 => "The request was rejected",
            401 or 403 => "The API key was rejected — check it in Settings",
            404 => "That model wasn't found — try another model in Settings",
            429 when provider == "Gemini" => "Gemini's usage limit was reached. On the free tier, wait a minute and try again (there's also a daily cap)",
            429 => "Rate limit or out of credit on your account",
            402 => "Your account is out of credit. Add credit on the provider's billing page",
            >= 500 => $"{provider} is having trouble right now",
            _ => $"{provider} returned an error",
        };
        throw new ProviderException(detail is null
            ? $"{reason} (HTTP {(int)res.StatusCode})."
            : $"{reason} (HTTP {(int)res.StatusCode}): {detail}");
    }

    /// <summary>Reads a Server-Sent Events stream and yields each "data:" payload.</summary>
    public static async IAsyncEnumerable<string> ReadSse(HttpResponseMessage res, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (data.Length > 0) { yield return data.ToString(); data.Clear(); }
                continue;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
            }
        }
        if (data.Length > 0) yield return data.ToString();
    }
}

public sealed class OpenAiProvider : IPromptProvider
{
    private const string Base = "https://api.openai.com/v1";

    public async IAsyncEnumerable<string> StreamAsync(string apiKey, string model, string system, string user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = Http.Json(new
        {
            model,
            stream = true,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        });

        using var res = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await Http.ThrowIfFailed(res, "OpenAI", ct);

        await foreach (var data in Http.ReadSse(res, ct))
        {
            if (data == "[DONE]") yield break;
            string? text = null;
            try
            {
                var node = JsonNode.Parse(data);
                if (node?["error"]?["message"] is JsonNode err) throw new ProviderException(err.GetValue<string>());
                text = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            }
            catch (JsonException) { }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await Http.Client.SendAsync(req, ct);
        await Http.ThrowIfFailed(res, "OpenAI", ct);
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        var skip = new[] { "embedding", "whisper", "tts", "dall-e", "image", "audio", "realtime", "transcribe", "moderation", "search", "davinci", "babbage", "sora" };
        return (node?["data"]?.AsArray() ?? new JsonArray())
            .Select(m => m?["id"]?.GetValue<string>() ?? "")
            .Where(id => (id.StartsWith("gpt-") || id.StartsWith("o")) && !skip.Any(s => id.Contains(s)))
            .OrderBy(id => id)
            .ToList();
    }
}

public sealed class AnthropicProvider : IPromptProvider
{
    private const string Base = "https://api.anthropic.com/v1";
    private const string Version = "2023-06-01";

    public async IAsyncEnumerable<string> StreamAsync(string apiKey, string model, string system, string user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/messages");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", Version);
        req.Content = Http.Json(new
        {
            model,
            max_tokens = 4096,
            stream = true,
            system,
            messages = new object[] { new { role = "user", content = user } },
        });

        using var res = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await Http.ThrowIfFailed(res, "Anthropic", ct);

        await foreach (var data in Http.ReadSse(res, ct))
        {
            string? text = null;
            bool stop = false;
            try
            {
                var node = JsonNode.Parse(data);
                switch (node?["type"]?.GetValue<string>())
                {
                    case "content_block_delta":
                        if (node["delta"]?["type"]?.GetValue<string>() == "text_delta")
                            text = node["delta"]?["text"]?.GetValue<string>();
                        break;
                    case "message_stop":
                        stop = true;
                        break;
                    case "error":
                        throw new ProviderException(node["error"]?["message"]?.GetValue<string>() ?? "Anthropic returned an error.");
                }
            }
            catch (JsonException) { }
            if (!string.IsNullOrEmpty(text)) yield return text;
            if (stop) yield break;
        }
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/models?limit=100");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", Version);
        using var res = await Http.Client.SendAsync(req, ct);
        await Http.ThrowIfFailed(res, "Anthropic", ct);
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        return (node?["data"]?.AsArray() ?? new JsonArray())
            .Select(m => m?["id"]?.GetValue<string>() ?? "")
            .Where(id => id.Length > 0)
            .ToList();
    }
}

public sealed class GeminiProvider : IPromptProvider
{
    private const string Base = "https://generativelanguage.googleapis.com/v1beta";

    public async IAsyncEnumerable<string> StreamAsync(string apiKey, string model, string system, string user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string name = model.StartsWith("models/") ? model : $"models/{model}";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{name}:streamGenerateContent?alt=sse");
        req.Headers.Add("x-goog-api-key", apiKey); // header, never the URL, so it can't leak into logs
        req.Content = Http.Json(new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
        });

        using var res = await Http.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await Http.ThrowIfFailed(res, "Gemini", ct);

        await foreach (var data in Http.ReadSse(res, ct))
        {
            var sb = new StringBuilder();
            try
            {
                var node = JsonNode.Parse(data);
                if (node?["error"]?["message"] is JsonNode err) throw new ProviderException(err.GetValue<string>());
                var parts = node?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                if (parts is not null)
                {
                    foreach (var p in parts)
                    {
                        // Skip "thought" parts some models emit.
                        if (p?["thought"]?.GetValue<bool>() == true) continue;
                        sb.Append(p?["text"]?.GetValue<string>());
                    }
                }
            }
            catch (JsonException) { }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }

    public async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/models?pageSize=200");
        req.Headers.Add("x-goog-api-key", apiKey);
        using var res = await Http.Client.SendAsync(req, ct);
        await Http.ThrowIfFailed(res, "Gemini", ct);
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        return (node?["models"]?.AsArray() ?? new JsonArray())
            .Where(m => m?["supportedGenerationMethods"]?.AsArray()
                .Any(x => x?.GetValue<string>() == "generateContent") == true)
            .Select(m => (m?["name"]?.GetValue<string>() ?? "").Replace("models/", ""))
            .Where(id => id.StartsWith("gemini"))
            .OrderBy(id => id)
            .ToList();
    }
}
