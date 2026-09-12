namespace SinAIPrompt.Core;

public sealed record PromptEntry(string Path, bool IsFolder, string? ParentHtml = null, string? ImageFolder = null, bool IsUnused = false)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsHtml => !IsFolder && System.IO.Path.GetExtension(Path).Equals(".html", StringComparison.OrdinalIgnoreCase);
    public bool IsImage => !IsFolder && PromptDirectory.IsImage(Path);
}

// Reads only one directory's metadata. Descendants and file contents are loaded on demand.
public static class PromptDirectory
{
    static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".txt", ".md", ".png", ".jpg", ".gif", ".pdf" };
    public static bool IsImage(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".gif";

    public static IReadOnlyList<PromptEntry> Read(string folder, bool showFolders, string sort, string? parentHtml = null, IReadOnlyList<string>? manualOrder = null, IReadOnlyDictionary<string, string>? openHtml = null)
    {
        string owner = System.IO.Path.TrimEndingDirectorySeparator(folder) + ".html";
        if (parentHtml == null && File.Exists(owner)) parentHtml = owner;
        var entries = new DirectoryInfo(folder).GetFileSystemInfos();
        var htmlNames = entries.OfType<FileInfo>().Where(f => f.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
            .Select(f => System.IO.Path.GetFileNameWithoutExtension(f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Normal(FileSystemInfo entry) => (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0;
        var folders = entries.OfType<DirectoryInfo>().Where(Normal).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var visible = entries.Where(Normal).Where(f => f is DirectoryInfo
            ? showFolders && !htmlNames.Contains(f.Name) : extensions.Contains(f.Extension));
        var positions = (manualOrder ?? []).Select((path, index) => (path, index)).DistinctBy(p => p.path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.path, p => p.index, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? used = null;
        if (parentHtml != null && entries.OfType<FileInfo>().Any(f => IsImage(f.FullName)))
        {
            try { used = ImageReferences.LocalPaths(openHtml?.GetValueOrDefault(parentHtml) ?? File.ReadAllText(parentHtml), parentHtml); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException or ArgumentException) { }
        }
        return visible.OrderBy(f => f is DirectoryInfo ? 0 : 1)
            .ThenBy(f => sort == "manual" ? positions.GetValueOrDefault(f.FullName, int.MaxValue) : 0)
            .ThenByDescending(f => sort == "created" ? f.CreationTimeUtc : f.LastWriteTimeUtc)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new PromptEntry(f.FullName, f is DirectoryInfo, parentHtml,
                f is FileInfo && f.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
                folders.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(f.Name), out var assets) ? assets.FullName : null,
                f is FileInfo && IsImage(f.FullName) && used != null && !used.Contains(f.FullName))).ToArray();
    }
}
