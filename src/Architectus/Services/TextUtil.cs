namespace Architectus.Services;

public static class TextUtil
{
    /// <summary>
    /// WinUI's TextBox stores line breaks as a bare "\r", which many apps (and AI chat boxes)
    /// don't treat as a new line, so copied prompts lost all their formatting.
    /// Everything leaving a TextBox goes through here and gets standard Windows "\r\n".
    /// </summary>
    public static string Newlines(string? s) =>
        (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
}
