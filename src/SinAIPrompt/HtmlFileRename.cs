using System.IO;
using System.Windows;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow
{
    internal async Task RenameDocumentFile(Document doc, string name)
    {
        if (!Documents.Contains(doc) || doc.Path == null) throw new IOException("The document is no longer open as a saved file.");
        string oldPath = Path.GetFullPath(doc.Path), destination = TextFiles.RenamePath(oldPath, name);
        if (oldPath == destination) return;
        bool sameFile = string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && (OpenReferences(destination).Count > 0 || File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException("The new name already exists or is open in another tab or window. Choose a different name.");
        string parent = Path.GetDirectoryName(oldPath)!, oldName = Path.GetFileNameWithoutExtension(oldPath), newName = Path.GetFileNameWithoutExtension(destination);
        if (string.IsNullOrWhiteSpace(newName)) throw new IOException("Enter a file name before the extension.");
        string oldFolder = Path.GetFullPath(Path.Combine(parent, oldName)), newFolder = Path.GetFullPath(Path.Combine(parent, newName));
        bool moveFolder = oldName.Length > 0 && oldFolder != newFolder && Directory.Exists(oldFolder);
        // Both asset folders must be direct children of the HTML document's folder.
        if (moveFolder && (Path.GetDirectoryName(oldFolder) != parent || Path.GetDirectoryName(newFolder) != parent))
            throw new IOException("The image folder must remain beside the HTML document.");
        if (moveFolder && !string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase) && (Directory.Exists(newFolder) || File.Exists(newFolder)))
            throw new IOException("The new image folder name already exists. Choose a different name.");

        var references = OpenReferences(oldPath);
        var windows = references.Select(r => r.Window).Distinct().ToArray();
        foreach (var window in windows) window.fileOperationDepth++;
        try
        {
            foreach (var (window, open) in references) await window.editors[open.Id].FlushAsync();
            var disk = await Task.Run(() => TextFiles.Open(oldPath));
            if (references.Any(r => r.Doc.Fingerprint != null && r.Doc.Fingerprint != disk.Fingerprint))
                throw new IOException("The file changed outside Sin - AI Prompt. Reload it before renaming.");
            if (moveFolder) disk.Text = await editors[doc.Id].RenameImageFolderAsync(disk.Text, oldName, newName);
            await Task.Run(() => RenameFiles(disk, destination, moveFolder ? oldFolder : null, newFolder));
            foreach (var (window, open) in references)
            {
                var view = window.editors[open.Id];
                open.Path = destination; open.Fingerprint = disk.Fingerprint;
                if (moveFolder)
                {
                    open.SavedText = await view.RenameImageFolderAsync(open.SavedText, oldName, newName);
                    await view.RenameOpenImageFolderAsync(oldName, newName);
                }
                open.Notify(); window.noticedVersions.Remove(open.Id);
                if (open == window.ActiveDocument) window.ExternalNotice.Visibility = Visibility.Collapsed;
            }
            Preferences.Recent.RemoveAll(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
            AddRecent(destination); App.Current.MarkChanged();
        }
        finally { foreach (var window in windows) window.fileOperationDepth--; }
    }

    static void RenameFiles(Document disk, string destination, string? oldFolder, string newFolder)
    {
        if (TextFiles.ChangedOnDisk(disk)) throw new IOException("The file changed while preparing the rename. Try again.");
        string original = disk.Path!;
        bool folderMoved = false;
        TextFiles.Rename(original, destination);
        try
        {
            if (oldFolder != null) { MoveImageFolder(oldFolder, newFolder); folderMoved = true; }
            disk.Path = destination;
            if (disk.Dirty) TextFiles.Save(disk, destination);
        }
        catch
        {
            if (folderMoved) MoveImageFolder(newFolder, oldFolder!);
            TextFiles.Rename(destination, original);
            throw;
        }
    }

    static void MoveImageFolder(string source, string destination)
    {
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) { Directory.Move(source, destination); return; }
        // Windows needs an intermediate name for a change of letter case alone.
        string temporary = Path.Combine(Path.GetDirectoryName(source)!, ".sin-rename-" + Guid.NewGuid().ToString("N"));
        Directory.Move(source, temporary);
        try { Directory.Move(temporary, destination); }
        catch { Directory.Move(temporary, source); throw; }
    }
}
