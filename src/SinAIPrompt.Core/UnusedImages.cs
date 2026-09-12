namespace SinAIPrompt.Core;

// Explicit on-demand scan. A failed document read aborts the scan rather than
// treating unknown references as unused. Never follows directory junctions.
public static class UnusedImages
{
    public static IReadOnlyList<string> Scan(string folder, IReadOnlyList<(string Path, string Html)> open, Action<int>? progress = null)
    {
        folder = Path.GetFullPath(folder);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, IgnoreInaccessible = false };
        var files = Directory.EnumerateFiles(folder, "*", options).ToArray();
        var documents = files.Where(path => Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".htm", StringComparison.OrdinalIgnoreCase)).ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in open) used.UnionWith(ImageReferences.LocalPaths(snapshot.Html, snapshot.Path));
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < documents.Length; i++)
        {
            string path = documents[i];
            var snapshots = open.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (snapshots.Length == 0) used.UnionWith(ImageReferences.LocalPaths(File.ReadAllText(path), path));
            assets.Add(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)) + Path.DirectorySeparatorChar);
            progress?.Invoke((i + 1) * 100 / documents.Length);
        }
        return files.Where(path => IsImage(path) && assets.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && !used.Contains(path)).ToArray();
    }
    static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".tif" or ".tiff" or ".ico";
}
