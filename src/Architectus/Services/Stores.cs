using System.Text.Json;
using System.Text.Json.Serialization;

namespace Architectus.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Architectus");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LibraryFile => Path.Combine(DataDir, "library.json");

    public static void Ensure() => Directory.CreateDirectory(DataDir);
}

internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Read<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch
        {
            // Corrupt file: keep a copy so nothing is silently lost, then start fresh.
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
        }
        return new T();
    }

    public static void Write<T>(string path, T value)
    {
        AppPaths.Ensure();
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Non-secret preferences only. API keys are NOT stored here — see CredentialStore.</summary>
public sealed class AppSettings
{
    public string Provider { get; set; } = "openai";
    public Dictionary<string, string> Models { get; set; } = new();
    public string Theme { get; set; } = "System"; // System | Light | Dark
    public bool SeenWelcome { get; set; }

    public string ModelFor(string providerId) =>
        Models.TryGetValue(providerId, out var m) && !string.IsNullOrWhiteSpace(m)
            ? m
            : ProviderCatalog.Get(providerId).DefaultModel;
}

public static class SettingsStore
{
    private static AppSettings? _current;
    public static AppSettings Current => _current ??= JsonFile.Read<AppSettings>(AppPaths.SettingsFile);
    public static void Save() => JsonFile.Write(AppPaths.SettingsFile, Current);
}

public sealed class LibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string RoughInput { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public string Subtitle => $"{UpdatedUtc.ToLocalTime():MMM d, yyyy h:mm tt} · {Model}";
    [JsonIgnore] public string Preview => Prompt.Length > 160 ? Prompt[..160].Replace('\n', ' ') + "…" : Prompt.Replace('\n', ' ');
}

public static class LibraryStore
{
    private static List<LibraryItem>? _items;
    public static List<LibraryItem> Items => _items ??= JsonFile.Read<List<LibraryItem>>(AppPaths.LibraryFile);

    public static void Add(LibraryItem item) { Items.Insert(0, item); Save(); }

    public static void Remove(string id) { Items.RemoveAll(i => i.Id == id); Save(); }

    public static void Save() => JsonFile.Write(AppPaths.LibraryFile, Items);
}
