using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Windows file actions; callers supply current document snapshots, never UI state.
internal static class ExplorerFileOperations
{
    internal static ProcessStartInfo LocationCommand(string path, bool select)
    {
        path = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The containing folder no longer exists.");
        return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = true, Arguments = select && Path.Exists(path) ? $"/select,\"{path}\"" : $"\"{folder}\"" };
    }

    internal static void ShowLocation(string path, bool select) => Process.Start(LocationCommand(path, select));

    internal static void RequireUnusedFolder(PromptEntry entry, IReadOnlyList<(string Path, string Html)> documents)
    {
        if (!entry.IsFolder || entry.ParentHtml == null) throw new IOException("Only a document's unused image folders can be deleted here.");
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path));
        string assets = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(entry.ParentHtml))!, Path.GetFileNameWithoutExtension(entry.ParentHtml));
        if (!string.Equals(folder, assets, StringComparison.OrdinalIgnoreCase) && !folder.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("This folder does not belong to the document.");
        for (string? path = folder; path != null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders cannot be deleted here.");
        var snapshots = documents.ToList();
        if (!snapshots.Any(d => string.Equals(d.Path, entry.ParentHtml, StringComparison.OrdinalIgnoreCase)))
            snapshots.Add((entry.ParentHtml, File.ReadAllText(entry.ParentHtml)));
        string prefix = folder + Path.DirectorySeparatorChar;
        foreach (var document in snapshots)
        {
            string? used = ImageReferences.LocalPaths(document.Html, document.Path).FirstOrDefault(path =>
                (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)) && Path.Exists(path));
            if (used != null) throw new IOException($"This folder still contains a file referenced by {Path.GetFileName(document.Path)}:\n{used}\n\nRemove its reference from the document first.");
        }
    }

    internal static void Recycle(string path, bool folder)
    {
        if (folder) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        if (Path.Exists(path)) throw new IOException("Windows did not move the item to the Recycle Bin.");
    }
}
