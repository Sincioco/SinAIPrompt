namespace SinAIPrompt.Core;

// File labels live in application settings, never in user HTML. Draft labels
// remain on the document until it receives a path through Save.
public static class DocumentEmojis
{
    static string Key(string path) => Path.GetFullPath(path).ToUpperInvariant();
    public static string Read(Settings settings, string path) => settings.FileEmojis.GetValueOrDefault(Key(path), "");
    public static void Restore(Document document, Settings settings)
    {
        if (document.Path == null) return;
        if (document.Emoji.Length == 0) document.Emoji = Read(settings, document.Path);
        else Set(document, document.Emoji, settings);
    }
    public static void Set(Document document, string emoji, Settings settings)
    {
        document.Emoji = emoji;
        if (document.Path != null)
        {
            string key = Key(document.Path);
            if (emoji.Length == 0) settings.FileEmojis.Remove(key);
            else settings.FileEmojis[key] = emoji;
        }
        document.Notify();
    }
    public static void Rename(Settings settings, string previous, string destination)
    {
        if (settings.FileEmojis.Remove(Key(previous), out var emoji)) settings.FileEmojis[Key(destination)] = emoji;
    }
    public static void Forget(Settings settings, string path) => settings.FileEmojis.Remove(Key(path));
}
