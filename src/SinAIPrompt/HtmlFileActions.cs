using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow
{
    async Task<bool> FlushDocument(Document doc)
    {
        if (projectMoveInProgress) { PositionStatus.Text = "Moving prompts to project…"; return false; }
        fileOperationDepth++;
        try { if (editors.TryGetValue(doc.Id, out var view)) await view.FlushAsync(); return true; }
        catch (Exception ex) { MessageBox.Show(this, "Could not synchronize the editor. The document remains open.\n\n" + ex.Message); return false; }
        finally { fileOperationDepth--; }
    }
    public async Task<bool> SaveDocument(Document doc, bool saveAs = false, string? destinationPath = null)
    {
        if (!await FlushDocument(doc)) return false;
        var firstSave = doc.Path == null && destinationPath == null
            ? await DocumentTitleSave.ResolveAsync(doc, editors.GetValueOrDefault(doc.Id), Explorer.WorkingFolder, Preferences.AutoSaveDirectory) : null;
        string? path = destinationPath ?? doc.Path ?? (saveAs ? null : firstSave?.Path);
        bool automatic = !saveAs && firstSave?.Path != null;
        if (saveAs || path == null)
        {
            var dialog = new SaveFileDialog { Title = "Save As", FileName = firstSave?.FileName ?? (doc.Path == null && !Path.HasExtension(doc.Name) ? doc.Name + ".html" : doc.Name), Filter = "HTML Documents (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*", DefaultExt = ".html", AddExtension = true, OverwritePrompt = true, CheckPathExists = true };
            if (doc.Path != null) dialog.InitialDirectory = Path.GetDirectoryName(doc.Path);
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        var duplicate = Documents.FirstOrDefault(d => d != doc && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null) { MessageBox.Show(this, "This file is already open in another tab. Choose a different name to keep both documents.", "Sin - AI Prompt"); return false; }
        try
        {
            bool conflict = string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase) && TextFiles.ChangedOnDisk(doc);
            if (conflict && MessageBox.Show(this, "This file changed outside Sin - AI Prompt or was moved. Replace it with the text in this tab?", "File changed", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            var view = editors.GetValueOrDefault(doc.Id);
            if (view == null && doc.Path != null && !string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase))
            { ActiveDocument = doc; view = CurrentView; }
            string original = doc.Text;
            string? relocated = view == null ? null : await view.PrepareSaveAsAsync(path!);
            if (relocated != null) doc.Text = relocated;
            try { TextFiles.Save(doc, path!, conflict, createNew: automatic); }
            catch { doc.Text = original; throw; }
            DocumentEmojis.Set(doc, doc.Emoji, Preferences);
            if (view != null) await view.RefreshBase(reloadDocument: relocated == null);
            if (relocated != null) { view!.AcceptHtml(doc.Text); view.ReloadSavedHtml(); }
            AddRecent(path!); noticedVersions.Remove(doc.Id); autoSaveErrors.Remove(doc.Id); pendingAutoSaves.Remove(doc.Id); ExternalNotice.Visibility = Visibility.Collapsed; UpdateStatus(); App.Current.MarkChanged(); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.EncoderFallbackException)
        { MessageBox.Show(this, "Could not save the file. Your text is still open.\n\n" + ex.Message, "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    void SourceClick(object sender, RoutedEventArgs e) => CurrentView?.ToggleSource();
    async void ExportMarkdownClick(object sender, RoutedEventArgs e)
    {
        if (CurrentView is not { } view) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export as Markdown", FileName = Path.GetFileNameWithoutExtension(view.Document.Name) + ".md", Filter = "Markdown Document|*.md", DefaultExt = ".md" };
        if (dialog.ShowDialog(this) != true) return;
        if (Dialogs.MarkdownImagePaths(this) is not bool absolute) return;
        await RunDocumentAction("Exporting Markdown…", async () => await view.SaveMarkdownAsync(dialog.FileName, absolute));
    }
    async Task SaveAllDocuments()
    {
        foreach (var doc in Documents.ToArray())
            if (!doc.IsReadOnly && (doc.Dirty || doc.Path == null) && !await SaveDocument(doc)) break;
    }
    async void ExportHtmlClick(object sender, RoutedEventArgs e)
    {
        if (CurrentView == null || ActiveDocument == null) return;
        var view = CurrentView;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export As Standalone HTML", FileName = Path.GetFileNameWithoutExtension(ActiveDocument.Name) + " - Standalone.html", Filter = "HTML Document|*.html", DefaultExt = ".html" };
        if (dialog.ShowDialog(this) != true) return;
        try { TextFiles.AtomicWrite(dialog.FileName, new UTF8Encoding(false).GetBytes(await view.ExportAsync())); MessageBox.Show(this, "Standalone HTML exported successfully.", "Sin - AI Prompt"); }
        catch (Exception ex) { MessageBox.Show(this, "Export could not finish.\n\n" + ex.Message, "Sin - AI Prompt"); }
    }
    internal async Task HandleHtmlCommand(string command)
    {
        switch (command)
        {
            case "lock": case "unlock":
                if (ActiveDocument != null) await DocumentLock.ChangeAsync(this, ActiveDocument, command == "lock", () => SaveDocument(ActiveDocument)); break;
            case "save": if (ActiveDocument != null) await SaveDocument(ActiveDocument); break;
            case "saveAs": if (ActiveDocument != null) await SaveDocument(ActiveDocument, true); break;
            case "saveAll": await SaveAllDocuments(); break;
            case "rename": if (ActiveDocument != null) RenameDocument(ActiveDocument); break;
            case "new": NewDocument(); break;
            case "open": OpenClick(this, new RoutedEventArgs()); break;
            case "close": if (ActiveDocument != null) await CloseDocument(ActiveDocument); break;
            case "next": CycleDocument(1); break;
            case "previous": CycleDocument(-1); break;
            case "documentList": SetDocumentList(!IsDocumentList); break;
            case "date": InsertDate(); break;
            case "longDate": InsertDate(0, rememberChoice: false, heading: true); break;
            case "separator": InsertAtCaret(new string('_', 80)); break;
            case "find": ShowFind(); break;
            case "replace": ShowFind(true); break;
            case "findNext": await FindNext(); break;
            case "findPrevious": await FindNext(true); break;
            case "zoomIn": ChangeZoom(10); break;
            case "zoomOut": ChangeZoom(-10); break;
            case "zoomReset": ChangeZoom(100, true); break;
        }
    }
}
