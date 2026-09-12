using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow
{
    int fileOperationDepth;

    void NavigationContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (sender is not ListBox list) return;
        var doc = FindDocument(e.OriginalSource as DependencyObject);
        if (doc == null && e.CursorLeft == -1) doc = list.SelectedItem as Document;
        if (doc == null) return;
        var menu = CreateDocumentMenu(doc);
        menu.PlacementTarget = list.ItemContainerGenerator.ContainerFromItem(doc) as UIElement ?? list;
        menu.Placement = e.CursorLeft == -1 ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal ContextMenu CreateDocumentMenu(Document doc)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool needsFile, bool needsPath = true)
        {
            bool enabled = !needsPath || doc.Path != null && (!needsFile || File.Exists(doc.Path));
            var item = new MenuItem
            {
                Header = label,
                IsEnabled = enabled,
                ToolTip = enabled ? doc.Path : doc.Path == null ? "Save this document first." : "The file is no longer at this path."
            };
            item.Click += (_, _) =>
            {
                if (!Documents.Contains(doc)) return;
                try { action(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                    NotSupportedException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
                { MessageBox.Show(this, ex.Message, "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            menu.Items.Add(item);
        }
        Add("_Rename…", () => RenameDocument(doc), true, needsPath: doc.Path != null);
        Add("D_uplicate", () => _ = RunDocumentAction("Duplicating Document…", async () => await DuplicateDocument(doc)), false, needsPath: false);
        Add("_Delete…", () => DeleteDocumentFile(doc, (path, dirty) => Dialogs.DeleteFile(this, path, dirty)), true);
        menu.Items.Add(new Separator());
        Add("Copy Full _Path", () => Clipboard.SetText(FullPathText(doc.Path!)), false);
        Add("Copy For _AI Use", () => Clipboard.SetText(AiInstructionText(doc.Path!)), false);
        Add("Open Containing _Folder", () => OpenContainingFolder(doc.Path!), false);
        menu.Items.Add(new Separator());
        Add("_Revert To Last Saved…", () => _ = RunDocumentAction("Reverting Document…", async () => await RevertDocument(doc)), true);
        Add("_Close", async () => await CloseDocument(doc), false, needsPath: false);
        ((MenuItem)menu.Items[^1]).InputGestureText = "Ctrl+W";
        return menu;
    }

    async Task RunDocumentAction(string title, Func<Task> action)
    {
        try { await Dialogs.WithProgress(this, title, action); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    internal async Task<Document> DuplicateDocument(Document doc)
    {
        fileOperationDepth++;
        try
        {
            ActiveDocument = doc;
            var copy = await DocumentCopies.CreateAsync(doc, GetEditor(doc),
                Application.Current.Windows.OfType<MainWindow>().SelectMany(w => w.Documents.Select(d => d.Name)));
            AddDocument(copy);
            if (copy.Path != null) AddRecent(copy.Path);
            return copy;
        }
        finally { fileOperationDepth--; }
    }

    internal async Task<bool> RevertDocument(Document doc, Func<bool>? confirm = null)
    {
        if (doc.Path == null) return false;
        fileOperationDepth++;
        try
        {
            ActiveDocument = doc;
            var view = GetEditor(doc);
            await view.FlushAsync();
            if (!(confirm?.Invoke() ?? MessageBox.Show(this, "Revert this document to its last saved state? Unsaved changes will be discarded.",
                "Revert Document", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)) return false;
            var disk = await Task.Run(() => TextFiles.Open(doc.Path));
            doc.SavedText = disk.Text; doc.EncodingName = doc.SavedEncoding = disk.EncodingName;
            doc.NewLine = doc.SavedNewLine = disk.NewLine; doc.Fingerprint = disk.Fingerprint;
            view.AcceptHtml(disk.Text); view.Editor.ClearUndo(); view.ReloadSavedHtml(); doc.Notify();
            pendingAutoSaves.Remove(doc.Id); autoSaveErrors.Remove(doc.Id); noticedVersions.Remove(doc.Id);
            ExternalNotice.Visibility = Visibility.Collapsed; App.Current.MarkChanged();
            return true;
        }
        finally { fileOperationDepth--; }
    }

    internal static ProcessStartInfo ContainingFolderCommand(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The containing folder no longer exists.");
        return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = true, Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{folder}\"" };
    }
    static void OpenContainingFolder(string path) => Process.Start(ContainingFolderCommand(path));
    internal static string FullPathText(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
    internal static string AiInstructionText(string path) => $"Read and execute the instructions in the \"{path}\" file.";

    static List<(MainWindow Window, Document Doc)> OpenReferences(string path) =>
        Application.Current.Windows.OfType<MainWindow>()
            .SelectMany(w => w.Documents.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).Select(d => (w, d))).ToList();

    internal bool DeleteDocumentFile(Document doc, Func<string, bool, bool> confirm, Action<string>? recycle = null)
    {
        if (!Documents.Contains(doc) || doc.Path == null) return false;
        string path = doc.Path;
        var references = OpenReferences(path);
        var windows = references.Select(r => r.Window).Distinct().ToArray();
        // Modal dialogs pump the dispatcher. Pause writes until cancellation or successful removal.
        foreach (var window in windows) window.fileOperationDepth++;
        try
        {
            if (!confirm(path, references.Any(r => r.Doc.Dirty))) return false;
            if (recycle != null) recycle(path);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            if (File.Exists(path)) throw new IOException("The file was not deleted. Its documents are still open.");
            // Another window can Save as or open this file while a modal dialog is displayed.
            // Close only references that still point at the deleted path, including newly opened ones.
            foreach (var (window, open) in OpenReferences(path)) window.RemoveDocument(open, fileDeleted: true);
            Preferences.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            App.Current.MarkChanged(); return true;
        }
        finally { foreach (var window in windows) window.fileOperationDepth--; }
    }
}
