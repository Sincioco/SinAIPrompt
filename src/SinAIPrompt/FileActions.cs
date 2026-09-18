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
    int documentLoads;
    async void AddBundledDocumentClick(object sender, RoutedEventArgs e)
    {
        if (IsAnnotating) return;
        var menu = (MenuItem)sender;
        menu.IsEnabled = false;
        documentLoads++; OpenProgress.Visibility = Visibility.Visible;
        try
        {
            var copy = await BundledDocuments.CreateCopyAsync((string)menu.Tag,
                BundledDocuments.WorkingDirectory(Preferences, App.Current.Store.DirectoryPath),
                Application.Current.Windows.OfType<MainWindow>().SelectMany(w => w.Documents.Select(d => d.Name)));
            if (!IsLoaded || App.Current.Exiting) return;
            AddDocument(copy); AddRecent(copy.Path!);
            Explorer.SetMode(false); SetDocumentList(true); Explorer.QueueRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { MessageBox.Show(this, ex.Message, "Could not add reference document", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { menu.IsEnabled = true; if (--documentLoads == 0) OpenProgress.Visibility = Visibility.Collapsed; }
    }
    public void OpenPaths(IEnumerable<string> paths) => _ = OpenPathsAsync(paths);
    internal async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (string rawPath in paths)
        {
            try
            {
                if (rawPath.StartsWith("--", StringComparison.Ordinal)) continue;
                var path = Path.GetFullPath(rawPath);
                if (MarkdownImport.IsMarkdown(path))
                {
                    documentLoads++; OpenProgress.Visibility = Visibility.Visible;
                    try { path = await MarkdownImport.ConvertAsync(path, GetEditor(activeDocument!).ConvertMarkdownAsync); }
                    finally { if (--documentLoads == 0) OpenProgress.Visibility = Visibility.Collapsed; }
                }
                var existing = Documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { ActiveDocument = existing; continue; }
                var doc = TextFiles.Open(path);
                var empty = Documents.Count == 1 && Documents[0].Path == null && !Documents[0].Dirty && Documents[0].Text.Length == 0 ? Documents[0] : null;
                AddDocument(doc);
                if (empty != null) RemoveDocument(empty);
                AddRecent(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            { MessageBox.Show(this, $"Could not open {Path.GetFileName(rawPath)}.\n\n{ex.Message}", "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        UpdateTabWidths(); FocusEditor();
    }
    internal async Task OpenDroppedPathsAsync(IEnumerable<string> paths)
    {
        if (IsAnnotating) return;
        foreach (string path in paths)
        {
            try
            {
                if (MarkdownImport.IsMarkdown(path)) await OpenExplorerFile(Path.GetFullPath(path), CancellationToken.None);
                else await OpenPathsAsync([path]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { MessageBox.Show(this, ex.Message, "Open dropped file", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
    internal async Task<IReadOnlyList<(string Path, string Html)>> CurrentHtmlSnapshots()
    {
        foreach (var view in editors.Values) await view.FlushAsync();
        return Documents.Select(d => (d.Path ?? Path.Combine(App.Current.Store.DirectoryPath, d.Id + ".html"), d.Text)).ToArray();
    }
    void SortDocumentsClick(object sender, RoutedEventArgs e) { documentOrder.SetMode((string)((MenuItem)sender).Tag); Explorer.QueueRefresh(); }

    internal async Task OpenExplorerFile(string path, CancellationToken token)
    {
        if (IsAnnotating) return;
        if (Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            var existing = Documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) ActiveDocument = existing;
            else
            {
                var document = await Task.Run(() => TextFiles.Open(path), token);
                if (token.IsCancellationRequested || IsAnnotating) return;
                AddDocument(document); AddRecent(path);
            }
            CurrentView?.PathStatus.SetImage("");
        }
        else
        {
            (EditorHost.Content as ExplorerPreview)?.Dispose();
            var preview = new ExplorerPreview(path, App.Current.Store.DirectoryPath, () => ActiveDocument = activeDocument) { OpenDroppedFiles = OpenDroppedPathsAsync };
            EditorHost.Content = preview; ExternalNotice.Visibility = Visibility.Collapsed;
            Title = "Sin - AI Prompt - " + Path.GetFileName(path);
            PositionStatus.Text = "Preview"; CountStatus.Text = "Read-only";
            try { await preview.LoadAsync(token, MarkdownImport.IsMarkdown(path) ? GetEditor(activeDocument!).ConvertMarkdownAsync : null); }
            catch (OperationCanceledException) { preview.ShowError("Select a file to preview."); }
            catch (Exception ex) { preview.ShowError(ex.Message); throw; }
        }
    }
    void ShowExplorerPath(string path)
    {
        if (CurrentView != null) CurrentView.PathStatus.SetImage(new Uri(path).AbsoluteUri);
        else if (EditorHost.Content is ExplorerPreview preview) preview.PathStatus.Text = path;
    }

    bool CloseExplorerPreview()
    {
        if (EditorHost.Content is not ExplorerPreview) return false;
        ActiveDocument = activeDocument; return true;
    }

    internal async Task RenameExplorerFile(PromptEntry entry, string name)
    {
        if (entry.IsHtml)
        {
            await OpenExplorerFile(entry.Path, CancellationToken.None);
            await RenameDocumentFile(ActiveDocument!, name);
        }
        else if (entry.IsImage) await RenameExplorerImage(entry, name);
    }

    internal async Task<bool> DeleteExplorerEntry(PromptEntry entry, Func<string, bool, bool>? confirm = null)
    {
        if (IsAnnotating) return false;
        var windows = Application.Current.Windows.OfType<MainWindow>().ToArray();
        foreach (var window in windows) window.fileOperationDepth++;
        try
        {
            var references = OpenReferences(entry.Path);
            foreach (var (window, doc) in references) if (window.editors.TryGetValue(doc.Id, out var view)) await view.FlushAsync();
            confirm ??= (path, dirty) => Dialogs.DeleteFile(this, path, dirty, entry.IsFolder);
            if (!confirm(entry.Path, references.Any(r => r.Doc.Dirty))) return false;
            if (entry.IsFolder)
            {
                // Capture live HTML after the confirmation; queued browser edits must
                // reach their document before deciding whether a folder is unused.
                foreach (var window in windows)
                    foreach (var view in window.editors.Values) await view.FlushAsync();
                var documents = windows.SelectMany(w => w.Documents).Where(d => d.Path != null).Select(d => (d.Path!, d.Text)).ToArray();
                await Task.Run(() => { ExplorerFileOperations.RequireUnusedFolder(entry, documents); ExplorerFileOperations.Recycle(entry.Path, true); });
            }
            else await Task.Run(() => ExplorerFileOperations.Recycle(entry.Path, false));
            foreach (var (window, doc) in OpenReferences(entry.Path)) window.RemoveDocument(doc, fileDeleted: true);
            foreach (var window in windows)
            {
                if (window.EditorHost.Content is ExplorerPreview preview && (string.Equals(preview.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase) ||
                    entry.IsFolder && preview.FilePath.StartsWith(Path.TrimEndingDirectorySeparator(entry.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    window.CloseExplorerPreview();
                window.Explorer.ExplorerSelection.Set(entry, false);
                window.Explorer.QueueRefresh();
            }
            Preferences.Recent.RemoveAll(path => string.Equals(path, entry.Path, StringComparison.OrdinalIgnoreCase));
            DocumentEmojis.Forget(Preferences, entry.Path);
            App.Current.MarkChanged(); return true;
        }
        finally { foreach (var window in windows) window.fileOperationDepth--; }
    }

    void NavigationContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (sender is not ListBox list) return;
        var doc = FindDocument(e.OriginalSource as DependencyObject);
        if (doc == null && e.CursorLeft == -1) doc = list.SelectedItem as Document;
        if (doc == null) return;
        var menu = CreateDocumentMenu(doc, list == DocumentList);
        menu.PlacementTarget = list.ItemContainerGenerator.ContainerFromItem(doc) as UIElement ?? list;
        menu.Placement = e.CursorLeft == -1 ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal ContextMenu CreateDocumentMenu(Document doc, bool documentList = false)
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
        Add(doc.Pinned ? "Un_pin" : "_Pin To Top", () => documentOrder.TogglePin(doc), false, needsPath: false);
        Add("_Private", () => { doc.IsPrivate = !doc.IsPrivate; doc.Notify(); RefreshPrivateDocuments(); }, false, needsPath: false);
        ((MenuItem)menu.Items[^1]).IsCheckable = true; ((MenuItem)menu.Items[^1]).IsChecked = doc.IsPrivate;
        Add("Choose _Emoji…", () => EmojiPicker.Create(this, doc, Preferences, () => { Explorer.QueueRefresh(); App.Current.MarkChanged(); }).ShowDialog(), false, needsPath: false);
        Add(doc.IsReadOnly ? "_Unlock Document" : "_Lock Document", () => _ = RunDocumentAction("Changing Document Lock…",
            () => DocumentLock.ChangeAsync(this, doc, !doc.IsReadOnly, () => SaveDocument(doc))), false, needsPath: false);
        if (Explorer.DocumentSelection.Enabled && Explorer.DocumentSelection.Order(doc) > 0 && Explorer.DocumentSelection.Items.Count > 1)
            Add("_Combine Selected Documents…", () => _ = CombineFilesFromUi(Explorer.DocumentSelection), false, needsPath: false);
        bool multiple = documentList && Explorer.DocumentSelection.Enabled && Explorer.DocumentSelection.Order(doc) > 0;
        Add(multiple ? "_Delete Selected Files…" : "_Delete…", () => _ = DeleteFilesFromUi(multiple ? Explorer.DocumentSelection.Items : [doc]), false, needsPath: false);
        menu.Items.Add(new Separator());
        Add("Copy Full _Path", () => Clipboard.SetText(FullPathText(doc.Path!)), false);
        Add("Copy for _AI Use", () => _ = RunDocumentAction("Saving And Locking Document…", () => CopyForAiUse(doc)), false, needsPath: false);
        Add("Open Containing _Folder", () => OpenContainingFolder(doc.Path!), false);
        menu.Items.Add(new Separator());
        Add("_Revert To Last Saved…", () => _ = RunDocumentAction("Reverting Document…", async () => await RevertDocument(doc)), true);
        Add("_Close", async () => await CloseDocument(doc), false, needsPath: false);
        ((MenuItem)menu.Items[^1]).InputGestureText = "Ctrl+W";
        return menu;
    }

    internal async Task CopyForAiUse(Document doc, Action<string>? copy = null)
    {
        await DocumentLock.ChangeAsync(this, doc, true, () => SaveDocument(doc));
        if (doc.IsReadOnly && doc.Dirty) throw new IOException("Unlock and save the changed document before copying it for AI use.");
        if (doc.IsReadOnly && doc.Path != null) (copy ?? Clipboard.SetText)(AiInstructionText(doc.Path));
    }

    void ShowPrivateDocumentsClick(object sender, RoutedEventArgs e)
    {
        Preferences.ShowPrivateDocuments = ShowPrivateDocumentsMenu.IsChecked;
        foreach (var window in Application.Current.Windows.OfType<MainWindow>()) window.RefreshPrivateDocuments();
    }
    void RefreshPrivateDocuments()
    {
        ShowPrivateDocumentsMenu.IsChecked = Preferences.ShowPrivateDocuments;
        privacy.Refresh(); Explorer.RefreshPrivacy();
        if (activeDocument == null || !privacy.IsVisible(activeDocument) || EditorHost.Content is ExplorerPreview preview && !privacy.IsPathVisible(preview.FilePath))
            ActiveDocument = privacy.VisibleDocument() ?? NewDocument();
        UpdateTabWidths(); App.Current.MarkChanged();
    }

    async void CombineDocumentsClick(object sender, RoutedEventArgs e) => await CombineFilesFromUi();
    Task CombineFilesFromUi(CombineSelection? selection = null) => RunDocumentAction("Combining Documents…", async () => await CombineSelectedDocuments(selection));
    internal async Task<Document> CombineSelectedDocuments(CombineSelection? selectedSelection = null)
    {
        var selection = selectedSelection ?? Explorer.CombineSelection;
        var selected = selection.Items;
        if (selected.Count < 2) throw new ArgumentException("Check at least two HTML documents in the navigation list. The numbers beside them show their combination order.");
        if (selected.OfType<PromptEntry>().Any(entry => !entry.IsHtml)) throw new ArgumentException("Combine supports HTML documents. Uncheck other file types before combining.");
        var sources = new List<DocumentCombine.Source>();
        var inputs = new List<Document>();
        foreach (var item in selected)
        {
            var doc = item as Document;
            if (item is PromptEntry entry)
                doc = Documents.FirstOrDefault(document => string.Equals(document.Path, entry.Path, StringComparison.OrdinalIgnoreCase)) ?? await Task.Run(() => TextFiles.Open(entry.Path));
            if (doc == null || item is Document && !Documents.Contains(doc)) throw new IOException("A selected document has closed. Select the files again.");
            DocumentEmojis.Restore(doc, Preferences); inputs.Add(doc);
            if (!await FlushDocument(doc)) throw new OperationCanceledException();
            var path = doc.Path ?? Path.Combine(App.Current.Store.DirectoryPath, "Untitled.html");
            sources.Add(new(doc.Text, new Uri(Path.GetFullPath(path)).AbsoluteUri));
        }
        var processor = CurrentView ?? GetEditor(ActiveDocument!);
        var combined = await DocumentCombine.CreateAsync(sources, processor,
            Explorer.ExplorerMode ? Preferences.ExplorerDirectory : Preferences.AutoSaveDirectory, Documents.Select(doc => doc.Name).ToArray());
        foreach (var input in inputs) if (input.Emoji.Length == 0) DocumentEmojis.Set(input, "❌", Preferences);
        AddDocument(combined, scrollToTop: true); selection.Clear(); Explorer.QueueRefresh();
        return combined;
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

    internal static ProcessStartInfo ContainingFolderCommand(string path) => ExplorerFileOperations.LocationCommand(path, true);
    static void OpenContainingFolder(string path) => Process.Start(ContainingFolderCommand(path));
    internal static string FullPathText(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
    internal static string AiInstructionText(string path) => $"Read and execute the instructions in the \"{path}\" file.";

    static List<(MainWindow Window, Document Doc)> OpenReferences(string path) =>
        Application.Current.Windows.OfType<MainWindow>()
            .SelectMany(w => w.Documents.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).Select(d => (w, d))).ToList();

    async void DeleteSelectedFilesClick(object sender, RoutedEventArgs e) => await DeleteSelectedFiles();
    Task DeleteSelectedFiles() => DeleteFilesFromUi(Explorer.CombineSelection.Items);
    async Task DeleteFilesFromUi(IReadOnlyList<object> items)
    {
        try { await DeleteFiles(items); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Delete Selected Files", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    internal async Task<int> DeleteFiles(IReadOnlyList<object> items, Func<IReadOnlyList<string>, IReadOnlyList<string>, bool, bool>? confirm = null)
    {
        if (IsAnnotating) return 0;
        if (items.Count == 0) throw new ArgumentException("Check the files or unsaved documents you want to delete first.");
        if (items.OfType<Document>().Any(doc => !Documents.Contains(doc))) throw new IOException("A selected document has closed. Select the files again.");
        var drafts = items.OfType<Document>().Where(doc => doc.Path == null).Distinct().ToArray();
        var paths = items.Select(item => item is Document doc ? doc.Path : item is PromptEntry { IsFolder: false } entry ? entry.Path : null)
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var windows = Application.Current.Windows.OfType<MainWindow>().ToArray();
        // Pause autosave across confirmation and the whole batch, including drafts.
        foreach (var window in windows) window.fileOperationDepth++;
        int deleted = 0;
        try
        {
            var references = paths.SelectMany(OpenReferences).ToArray();
            foreach (var (window, doc) in references) if (window.editors.TryGetValue(doc.Id, out var view)) await view.FlushAsync();
            confirm ??= (files, unsaved, dirty) => Dialogs.DeleteFiles(this, files, unsaved, dirty);
            if (!confirm(paths, drafts.Select(doc => doc.Name).ToArray(), references.Any(r => r.Doc.Dirty))) return 0;
            await Dialogs.WithProgress(this, "Deleting Selected Files…", async () =>
            {
                foreach (string path in paths)
                    if (await DeleteExplorerEntry(new(path, false), (_, _) => true)) deleted++;
                foreach (var draft in drafts)
                    if (Documents.Contains(draft) && draft.Path == null) { RemoveDocument(draft); deleted++; }
            });
            return deleted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException($"{deleted} item(s) processed. Remaining items were kept open and selected.\n\n{ex.Message}", ex); }
        finally { foreach (var window in windows) window.fileOperationDepth--; }
    }
}
