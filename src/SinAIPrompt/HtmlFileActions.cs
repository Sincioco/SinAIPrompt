using System.IO;
using System.Text;
using System.Windows;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow
{
    async Task<bool> FlushDocument(Document doc)
    {
        fileOperationDepth++;
        try { if (editors.TryGetValue(doc.Id, out var view)) await view.FlushAsync(); return true; }
        catch (Exception ex) { MessageBox.Show(this, "Could not synchronize the editor. The document remains open.\n\n" + ex.Message); return false; }
        finally { fileOperationDepth--; }
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
            if ((doc.Dirty || doc.Path == null) && !await SaveDocument(doc)) break;
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
            case "longDate": InsertDate(0, rememberChoice: false); break;
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
