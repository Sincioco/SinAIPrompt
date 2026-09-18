using System.IO;
using System.Windows;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow
{
    void RenameDocument(Document doc) => Dialogs.RenameFile(this, doc.Name, name => RenameDocumentFile(doc, name), keepExtension: doc.Path != null);

    internal async Task RenameExplorerImage(PromptEntry entry, string name)
    {
        string destination = ImageFileRename.Destination(entry.Path, name);
        if (entry.Path == destination) return;
        var references = entry.ParentHtml == null ? [] : OpenReferences(entry.ParentHtml);
        var windows = references.Select(r => r.Window).Append(this).Distinct().ToArray();
        foreach (var window in windows) window.fileOperationDepth++;
        try
        {
            foreach (var (window, doc) in references) if (window.editors.TryGetValue(doc.Id, out var view)) await view.FlushAsync();
            Document? disk = entry.ParentHtml == null ? null : await Task.Run(() => TextFiles.Open(entry.ParentHtml));
            if (disk != null && references.Any(r => r.Doc.Fingerprint != null && r.Doc.Fingerprint != disk.Fingerprint))
                throw new IOException("The parent HTML changed outside Sin - AI Prompt. Reload it before renaming its image.");
            // Reuse the already loaded editor as a pure HTML parser; no parent editor is created.
            var converter = editors.GetValueOrDefault(ActiveDocument!.Id)!;
            if (disk != null) disk.Text = await converter.RenameImageFileAsync(disk.Text, disk.Path!, entry.Path, destination);
            await Task.Run(() => ImageFileRename.Commit(entry.Path, destination, disk));
            foreach (var (window, doc) in references)
            {
                var view = window.editors.GetValueOrDefault(doc.Id);
                doc.SavedText = await converter.RenameImageFileAsync(doc.SavedText, doc.Path!, entry.Path, destination);
                if (view?.IsVisual == true)
                    view.AcceptHtml(await view.RenameImageFileAsync("", doc.Path!, entry.Path, destination, live: true));
                else
                {
                    string original, updated;
                    do { original = doc.Text; updated = await converter.RenameImageFileAsync(original, doc.Path!, entry.Path, destination); } while (doc.Text != original);
                    if (view == null) doc.Text = updated; else view.AcceptHtml(updated);
                }
                doc.Fingerprint = disk!.Fingerprint; doc.ModifiedUtc = disk.ModifiedUtc; doc.Notify(); window.noticedVersions.Remove(doc.Id);
            }
            if (EditorHost.Content is ExplorerPreview preview && string.Equals(preview.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase))
                await OpenExplorerFile(destination, CancellationToken.None);
            App.Current.MarkChanged();
        }
        finally { foreach (var window in windows) window.fileOperationDepth--; }
    }

    internal async Task RenameDocumentFile(Document doc, string name)
    {
        if (doc.IsReadOnly) throw new IOException("Unlock the document before renaming it.");
        if (!Documents.Contains(doc)) throw new IOException("The document is no longer open.");
        if (doc.Path == null)
        {
            TextFiles.ValidateFileName(name);
            doc.DraftName = name;
            doc.Notify();
            App.Current.MarkChanged();
            return;
        }
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
            foreach (var (window, open) in references) if (window.editors.TryGetValue(open.Id, out var loaded)) await loaded.FlushAsync();
            var disk = await Task.Run(() => TextFiles.Open(oldPath));
            if (references.Any(r => r.Doc.Fingerprint != null && r.Doc.Fingerprint != disk.Fingerprint))
                throw new IOException("The file changed outside Sin - AI Prompt. Reload it before renaming.");
            var converter = CurrentView!;
            if (moveFolder) disk.Text = await converter.RenameImageFolderAsync(disk.Text, oldName, newName);
            await Task.Run(() => RenameFiles(disk, destination, moveFolder ? oldFolder : null, newFolder));
            foreach (var (window, open) in references)
            {
                var view = window.editors.GetValueOrDefault(open.Id);
                open.Path = destination; open.Fingerprint = disk.Fingerprint;
                if (moveFolder)
                {
                    open.SavedText = await (view ?? converter).RenameImageFolderAsync(open.SavedText, oldName, newName);
                    if (view != null) await view.RenameOpenImageFolderAsync(oldName, newName);
                    else open.Text = await converter.RenameImageFolderAsync(open.Text, oldName, newName);
                }
                open.Notify(); window.noticedVersions.Remove(open.Id);
                if (open == window.ActiveDocument) window.ExternalNotice.Visibility = Visibility.Collapsed;
            }
            Preferences.Recent.RemoveAll(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
            DocumentEmojis.Rename(Preferences, oldPath, destination);
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
