using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns directory navigation, loaded branches, selection and refresh lifetime.
public partial class PromptExplorer : UserControl, IDisposable
{
    FrameworkElement documentList = null!;
    readonly ObservableCollection<TreeViewItem> roots = [];
    readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    Settings settings = null!;
    Func<string, CancellationToken, Task> open = null!;
    Func<PromptEntry, string, Task> rename = null!;
    Func<PromptEntry, Task> delete = null!;
    Action create = null!, changed = null!;
    Action<string> status = null!;
    FileSystemWatcher? watcher;
    IReadOnlyDictionary<int, ImageSource>? icons;
    CancellationTokenSource selection = new();
    TreeViewItem? restoredSelection;
    int revision, busy;
    bool refreshing, disposed, changingFolder;
    string root = "";
    public bool ExplorerMode => settings.ExplorerMode;
    internal Func<IReadOnlyList<(string Path, string Html)>> OpenDocuments { get; set; } = () => [];
    internal IReadOnlyList<PromptEntry> Entries => roots.Select(r => (PromptEntry)r.Tag).ToArray();

    public PromptExplorer()
    {
        InitializeComponent(); Tree.ItemsSource = roots;
        refreshTimer.Tick += async (_, _) => { refreshTimer.Stop(); await RefreshAsync(); };
    }
    internal void Initialize(Settings preferences, FrameworkElement openDocuments, string? initialFolder, Func<string, CancellationToken, Task> openFile,
        Func<PromptEntry, string, Task> renameFile, Func<PromptEntry, Task> deleteEntry, Action newDocument, Action<string> showPath, Action saveSettings)
    {
        settings = preferences; documentList = openDocuments; documentList.Margin = new Thickness(0, 40, 0, 0);
        open = openFile; rename = renameFile; delete = deleteEntry; create = newDocument; status = showPath; changed = saveSettings;
        Folders.IsChecked = settings.ExplorerShowFolders;
        root = settings.ExplorerDirectory.Length > 0 ? settings.ExplorerDirectory : initialFolder ?? "";
        SetMode(settings.ExplorerMode);
        // The last active document begins rendering before directory metadata is read.
        Loaded += Start;
    }
    async void Start(object sender, RoutedEventArgs e) { Loaded -= Start; if (root.Length > 0) await SetFolderAsync(root); }
    internal void SetMode(bool explorer)
    {
        if (!explorer) selection.Cancel();
        settings.ExplorerMode = explorer;
        Heading.Text = explorer ? "Prompt Explorer" : "Document List";
        Tree.Visibility = ExplorerTools.Visibility = ChooseFolder.Visibility = explorer ? Visibility.Visible : Visibility.Collapsed;
        documentList.Visibility = explorer ? Visibility.Collapsed : Visibility.Visible;
        ModeButton.ToolTip = "Choose Navigation View";
    }
    void ModeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ModeButton, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
        foreach (string mode in new[] { "Document List", "Prompt Explorer" })
        {
            var item = new MenuItem { Header = mode, IsCheckable = true, IsChecked = mode == Heading.Text };
            item.Click += (_, _) => { SetMode(mode == "Prompt Explorer"); changed(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    void NewClick(object sender, RoutedEventArgs e) => create();
    async void FolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose Working Folder", InitialDirectory = root };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await SetFolderAsync(dialog.FolderName); status(root); }
    }
    internal async Task SetFolderAsync(string path)
    {
        int current = ++revision; changingFolder = true; refreshTimer.Stop(); SetBusy(true); Notice.Text = "";
        try
        {
            var entries = await ReadAsync(path);
            icons = await ExplorerIcons.LoadAsync();
            if (disposed || current != revision) return;
            watcher?.Dispose(); watcher = null;
            root = Path.GetFullPath(path); settings.ExplorerDirectory = root; RootLabel.Text = root; changed();
            ChooseFolder.Content = new Image { Source = icons[3], Width = 16, Height = 16 };
            refreshing = true;
            try { roots.Clear(); foreach (var entry in entries) roots.Add(Row(entry)); }
            finally { refreshing = false; }
            watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            watcher.Changed += Watch; watcher.Created += Watch; watcher.Deleted += Watch; watcher.Renamed += Watch;
            watcher.Error += (_, _) => QueueRefresh(); watcher.EnableRaisingEvents = true;
            Notice.Text = entries.Count == 0 ? "No supported files in this folder." : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Notice.Text = ex.Message; }
        finally { if (current == revision) changingFolder = false; SetBusy(false); }
    }
    TreeViewItem Row(PromptEntry entry)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Image { Source = icons?.GetValueOrDefault(entry.IsFolder ? 3 : entry.IsImage ? 72 : 0),
            Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center };
        if (entry.IsUnused) text.Foreground = Brushes.Red;
        label.Children.Add(text);
        var row = new TreeViewItem { Tag = entry, Header = label, ToolTip = entry.Path + (entry.IsUnused ? "\nNot used in the parent document" : ""), Padding = new Thickness(2, 5, 2, 5) };
        System.Windows.Automation.AutomationProperties.SetName(row, entry.Name);
        if (entry.IsFolder || entry.ImageFolder != null) row.Items.Add(new TreeViewItem { Header = "Loading…", IsEnabled = false });
        row.Expanded += async (_, e) => { if (e.OriginalSource == row) { await ExpandAsync(row); e.Handled = true; } };
        row.PreviewMouseDoubleClick += (_, e) =>
        {
            if (FindRow(e.OriginalSource as DependencyObject) != row || e.ChangedButton != MouseButton.Left) return;
            if (row.Items.Count > 0) row.IsExpanded = !row.IsExpanded;
            e.Handled = true;
        };
        row.ContextMenuOpening += (_, e) =>
        {
            if (FindRow(e.OriginalSource as DependencyObject) != row) return;
            e.Handled = true;
            var menu = CreateFileMenu(entry); menu.PlacementTarget = row; menu.IsOpen = true;
        };
        row.ContextMenu = new ContextMenu();
        return row;
    }
    static TreeViewItem? FindRow(DependencyObject? target)
    {
        while (target != null && target is not TreeViewItem) target = target is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(target);
        return target as TreeViewItem;
    }
    internal async Task ExpandAsync(TreeViewItem row)
    {
        if (row.Tag is not PromptEntry entry || row.Items.Count != 1 || ((TreeViewItem)row.Items[0]).Tag != null) return;
        if (entry.ImageFolder != null)
        {
            row.Items.Clear(); row.Items.Add(Row(new(entry.ImageFolder, true, entry.Path))); return;
        }
        SetBusy(true);
        try
        {
            var children = await ReadAsync(entry.Path, entry.ParentHtml);
            if (disposed) return;
            row.Items.Clear(); foreach (var child in children) row.Items.Add(Row(child));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    async void Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (refreshing || e.NewValue == restoredSelection || !ExplorerMode || !Tree.IsVisible || e.NewValue is not TreeViewItem { Tag: PromptEntry entry }) return;
        restoredSelection = null;
        selection.Cancel(); selection.Dispose(); selection = new();
        status(entry.Path);
        if (entry.IsFolder) return;
        SetBusy(true); Notice.Text = "";
        try { await open(entry.Path, selection.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    void Rename(PromptEntry entry) => Dialogs.RenameFile(Window.GetWindow(this), entry.Name, async name => { await rename(entry, name); await RefreshAsync(); }, keepExtension: !entry.IsFolder);
    internal ContextMenu CreateFileMenu(PromptEntry entry)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, string shortcut = "")
        {
            var item = new MenuItem { Header = label, InputGestureText = shortcut };
            item.Click += (_, _) => { try { action(); } catch (Exception ex) { Notice.Text = ex.Message; } };
            menu.Items.Add(item);
        }
        if (entry.IsImage || entry.IsHtml) Add("Rename…", () => Rename(entry), "F2");
        if (!entry.IsFolder || entry.ParentHtml != null) Add("Delete to Recycle Bin…", () => Delete(entry), "Delete");
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        Add("Show in File Explorer", () => ExplorerFileOperations.ShowLocation(entry.Path, true));
        Add("Open Containing Folder", () => ExplorerFileOperations.ShowLocation(entry.Path, false));
        return menu;
    }
    async void Delete(PromptEntry entry)
    {
        SetBusy(true); Notice.Text = "";
        try { await delete(entry); await RefreshAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Delete to Recycle Bin", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { SetBusy(false); }
    }
    void TreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; QueueRefresh(); }
        if (e.Key == Key.F2 && Tree.SelectedItem is TreeViewItem { Tag: PromptEntry entry } && (entry.IsImage || entry.IsHtml)) { e.Handled = true; Rename(entry); }
        if (e.Key == Key.Delete && Tree.SelectedItem is TreeViewItem { Tag: PromptEntry selected } && (!selected.IsFolder || selected.ParentHtml != null)) { e.Handled = true; Delete(selected); }
    }
    async void FoldersClick(object sender, RoutedEventArgs e) { settings.ExplorerShowFolders = Folders.IsChecked == true; changed(); await RefreshAsync(); }
    async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    void Watch(object sender, FileSystemEventArgs e) => QueueRefresh();
    Task<IReadOnlyList<PromptEntry>> ReadAsync(string folder, string? parent = null)
    {
        var documents = OpenDocuments(); bool folders = settings.ExplorerShowFolders; string sort = settings.DocumentSort;
        var html = documents.DistinctBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(d => d.Path, d => d.Html, StringComparer.OrdinalIgnoreCase);
        return Task.Run(() => PromptDirectory.Read(folder, folders, sort, parent, documents.Select(d => d.Path).ToArray(), html));
    }
    internal void QueueUsageRefresh() { if (ExplorerMode) QueueRefresh(); }
    internal void QueueRefresh()
    {
        if (disposed) return;
        Dispatcher.BeginInvoke(() => { if (disposed) return; refreshTimer.Stop(); refreshTimer.Start(); });
    }
    internal async Task RefreshAsync()
    {
        if (root.Length == 0 || disposed || changingFolder) return;
        int current = ++revision; SetBusy(true);
        try
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Remember(IEnumerable<TreeViewItem> rows) { foreach (var row in rows) if (row.IsExpanded && row.Tag is PromptEntry item) { expanded.Add(item.Path); Remember(row.Items.Cast<TreeViewItem>()); } }
            Remember(roots);
            string? selected = (Tree.SelectedItem as TreeViewItem)?.Tag is PromptEntry selectionEntry ? selectionEntry.Path : null;
            var documents = OpenDocuments(); bool folders = settings.ExplorerShowFolders; string sort = settings.DocumentSort;
            var order = documents.Select(d => d.Path).ToArray();
            var html = documents.DistinctBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(d => d.Path, d => d.Html, StringComparer.OrdinalIgnoreCase);
            // Read all expanded branches off-thread, then replace the tree in one UI update.
            var snapshot = await Task.Run(() =>
            {
                var loaded = new Dictionary<string, IReadOnlyList<PromptEntry>>(StringComparer.OrdinalIgnoreCase);
                void Read(string folder, string? parent)
                {
                    var entries = PromptDirectory.Read(folder, folders, sort, parent, order, html); loaded[folder] = entries;
                    foreach (var item in entries)
                    {
                        if (item.IsFolder && expanded.Contains(item.Path)) Read(item.Path, parent);
                        if (item.ImageFolder != null && expanded.Contains(item.Path) && expanded.Contains(item.ImageFolder)) Read(item.ImageFolder, item.Path);
                    }
                }
                Read(root, null); return loaded;
            });
            if (disposed || current != revision) return;
            TreeViewItem Restore(PromptEntry item)
            {
                var row = Row(item);
                if (expanded.Contains(item.Path))
                {
                    row.Items.Clear();
                    if (item.ImageFolder != null) row.Items.Add(Restore(new(item.ImageFolder, true, item.Path)));
                    else if (snapshot.TryGetValue(item.Path, out var children)) foreach (var child in children) row.Items.Add(Restore(child));
                    row.IsExpanded = true;
                }
                // WPF can raise SelectedItemChanged after this row is attached and
                // refreshing is false. Restoring a highlight is not an open request.
                if (string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase)) { restoredSelection = row; row.IsSelected = true; }
                return row;
            }
            refreshing = true;
            try { roots.Clear(); foreach (var item in snapshot[root]) roots.Add(Restore(item)); }
            finally { refreshing = false; }
            Notice.Text = roots.Count == 0 ? "No supported files in this folder." : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    void SetBusy(bool value) { busy += value ? 1 : -1; Progress.Visibility = busy > 0 ? Visibility.Visible : Visibility.Hidden; }
    public void Dispose() { disposed = true; revision++; watcher?.Dispose(); refreshTimer.Stop(); selection.Cancel(); selection.Dispose(); }
}
