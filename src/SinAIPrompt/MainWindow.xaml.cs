using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<Document> Documents { get; } = [];
    readonly Dictionary<Guid, EditorView> editors = [];
    readonly DocumentOrder documentOrder;
    readonly HashSet<Guid> restoredDocuments = [];
    readonly Dictionary<Guid, string?> noticedVersions = [];
    readonly Dictionary<Guid, DateTime> pendingAutoSaves = [];
    readonly Dictionary<Guid, string> autoSaveErrors = [];
    readonly DispatcherTimer autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    Document? activeDocument;
    public Document? ActiveDocument
    {
        get => activeDocument;
        set
        {
            if (value == null || value == activeDocument || IsAnnotating || documentOrder?.IsSorting == true || !Documents.Contains(value)) return;
            activeDocument = value;
            if (EditorHost != null)
            {
                EditorHost.Content = GetEditor(value);
                ExternalNotice.Visibility = Visibility.Collapsed;
                UpdateStatus(); UpdateSearchStatus();
                Title = "Sin - AI Prompt - " + value.Name;
                Dispatcher.BeginInvoke(() => { if (!IsLoaded) return; Tabs.ScrollIntoView(value); DocumentList.ScrollIntoView(value); }, DispatcherPriority.Loaded);
            }
            PropertyChanged?.Invoke(this, new(nameof(ActiveDocument)));
            App.Current.MarkChanged();
        }
    }
    public EditorView? CurrentView => ActiveDocument == null ? null : editors.GetValueOrDefault(ActiveDocument.Id);
    internal int CreatedEditorCount => editors.Count;
    public TextBox? Editor => CurrentView?.Editor;
    public bool IsDocumentList { get; private set; }
    double savedListWidth = 250;
    Point dragOrigin;
    Document? dragDocument;
    bool dragging;
    bool externalChecking;
    bool initialized;
    public event PropertyChangedEventHandler? PropertyChanged;
    Settings Preferences => App.Current.Preferences;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow(WindowSession? session = null)
    {
        InitializeComponent(); DataContext = this; SearchPanel.GetEditor = () => CurrentView;
        documentOrder = new(Documents, Preferences, Dispatcher, App.Current.MarkChanged, () => PropertyChanged?.Invoke(this, new(nameof(ActiveDocument))));
        if (session != null)
        {
            Width = Math.Clamp(session.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
            Height = Math.Clamp(session.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
            savedListWidth = Math.Clamp(session.ListWidth, 150, 650);
            foreach (var doc in session.Documents)
            {
                if (Documents.Any(d => d.Id == doc.Id)) doc.Id = Guid.NewGuid();
                doc.Zoom = Math.Clamp(doc.Zoom, 10, 500);
                Preferences.NextDocumentNumber = Math.Max(Preferences.NextDocumentNumber, doc.UntitledNumber + 1);
                restoredDocuments.Add(doc.Id);
                AddDocument(doc, activate: false);
            }
            if (Documents.Count > 0) ActiveDocument = Documents[Math.Clamp(session.ActiveIndex, 0, Documents.Count - 1)];
            if (session.Maximized) WindowState = WindowState.Maximized;
        }
        else savedListWidth = Math.Clamp(Preferences.ListWidth, 150, 650);
        if (Documents.Count == 0) NewDocument();
        SetDocumentList(session?.DocumentList ?? Preferences.DocumentList, false);
        SourceInitialized += (_, _) => ApplyTheme();
        StateChanged += (_, _) => App.Current.MarkChanged();
        SizeChanged += (_, _) => { UpdateTabWidths(); ClampSidebar(); App.Current.MarkChanged(); };
        Loaded += (_, _) => { initialized = true; UpdateTabWidths(); ApplyPreferences(); CurrentView?.FocusEditing(); };
        autoSaveTimer.Tick += (_, _) => FlushAutoSaves(false);
        autoSaveTimer.Start();
        Closed += (_, _) => { documentOrder.Dispose(); autoSaveTimer.Stop(); if (Application.Current.Windows.OfType<MainWindow>().Any() && !App.Current.Exiting) { App.Current.MarkChanged(); App.Current.SaveState(); } };
    }
    public Document NewDocument(string? excludedPath = null)
    {
        Document doc;
        try { doc = DocumentFactory.Create(Preferences, excludedPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            doc = DocumentFactory.CreateDraft(App.Current.NextNumber());
            if (IsLoaded) MessageBox.Show(this, "The auto-save folder is unavailable. This document will stay open until you save it manually.\n\n" + ex.Message, "Could not create the file", MessageBoxButton.OK, MessageBoxImage.Warning);
            else Dispatcher.BeginInvoke(() => MessageBox.Show(this, "The auto-save folder is unavailable. Use Save As to save this document.\n\n" + ex.Message, "Sin - AI Prompt"));
        }
        AddDocument(doc); FocusEditor(); return doc;
    }
    void AddDocument(Document doc, bool activate = true)
    {
        Documents.Add(doc);
        doc.PropertyChanged += (_, _) => { if (doc == ActiveDocument) { Title = "Sin - AI Prompt - " + doc.Name; UpdateStatus(); } };
        if (activate) { ActiveDocument = doc; UpdateTabWidths(); }
        App.Current.MarkChanged();
        if (doc.AutoSave && doc.Dirty) pendingAutoSaves[doc.Id] = DateTime.UtcNow;
    }
    EditorView GetEditor(Document doc)
    {
        if (editors.TryGetValue(doc.Id, out var existing)) return existing;
        // Inactive restored documents keep their recovery text until first selected.
        if (restoredDocuments.Remove(doc.Id) && !doc.Dirty && doc.Path != null)
        {
            try { var disk = TextFiles.Open(doc.Path); doc.Text = disk.Text; doc.SavedText = disk.SavedText; doc.Fingerprint = disk.Fingerprint; doc.EncodingName = disk.EncodingName; doc.SavedEncoding = disk.EncodingName; doc.NewLine = disk.NewLine; doc.SavedNewLine = disk.NewLine; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var view = new EditorView(doc) { HostWindow = this };
        editors[doc.Id] = view;
        view.Editor.SelectionChanged += (_, _) => { if (doc == ActiveDocument) UpdateStatus(); };
        void ContentChanged(object? sender, EventArgs args) { if (doc.AutoSave) { pendingAutoSaves[doc.Id] = DateTime.UtcNow; autoSaveErrors.Remove(doc.Id); } if (doc == ActiveDocument) { UpdateStatus(); UpdateSearchStatus(); } }
        view.Editor.TextChanged += ContentChanged;
        view.HtmlChanged += ContentChanged;
        view.Editor.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ChangeZoom(e.Delta > 0 ? 10 : -10); e.Handled = true; } };
        view.ApplyPreferences();
        return view;
    }
    public bool FlushAutoSaves(bool force)
    {
        if (fileOperationDepth > 0) return true;
        bool success = true;
        foreach (var pending in pendingAutoSaves.ToArray())
        {
            if (!force && DateTime.UtcNow - pending.Value < TimeSpan.FromMilliseconds(800)) continue;
            var doc = Documents.FirstOrDefault(d => d.Id == pending.Key);
            pendingAutoSaves.Remove(pending.Key);
            if (doc == null || !doc.AutoSave || !doc.Dirty || doc.Path == null) continue;
            try { TextFiles.Save(doc, doc.Path); autoSaveErrors.Remove(doc.Id); App.Current.MarkChanged(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.EncoderFallbackException)
            { success = false; autoSaveErrors[doc.Id] = ex.Message; }
        }
        UpdateStatus(); return success;
    }
    public void OpenPaths(IEnumerable<string> paths)
    {
        foreach (string rawPath in paths)
        {
            try
            {
                if (rawPath.StartsWith("--", StringComparison.Ordinal)) continue;
                var path = Path.GetFullPath(rawPath);
                var existing = Documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { ActiveDocument = existing; continue; }
                var doc = TextFiles.Open(path);
                var empty = Documents.Count == 1 && Documents[0].Path == null && !Documents[0].Dirty && Documents[0].Text.Length == 0 ? Documents[0] : null;
                AddDocument(doc);
                if (empty != null) { Documents.Remove(empty); editors.GetValueOrDefault(empty.Id)?.Dispose(); editors.Remove(empty.Id); restoredDocuments.Remove(empty.Id); }
                AddRecent(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { MessageBox.Show(this, $"Could not open {Path.GetFileName(rawPath)}.\n\n{ex.Message}", "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        UpdateTabWidths(); FocusEditor();
    }
    void AddRecent(string path)
    {
        if (!Preferences.RecentFiles) return;
        Preferences.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Preferences.Recent.Insert(0, path);
        if (Preferences.Recent.Count > Settings.RecentFileLimit) Preferences.Recent.RemoveRange(Settings.RecentFileLimit, Preferences.Recent.Count - Settings.RecentFileLimit);
        App.Current.MarkChanged();
    }
    public async Task<bool> SaveDocument(Document doc, bool saveAs = false, string? destinationPath = null)
    {
        if (!await FlushDocument(doc)) return false;
        string? path = destinationPath ?? doc.Path;
        if (saveAs || path == null)
        {
            var dialog = new SaveFileDialog { Title = "Save As", FileName = doc.Path == null && !Path.HasExtension(doc.Name) ? doc.Name + ".html" : doc.Name, Filter = "HTML Documents (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*", DefaultExt = ".html", AddExtension = true, OverwritePrompt = true, CheckPathExists = true };
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
            try { TextFiles.Save(doc, path!, conflict); }
            catch { doc.Text = original; throw; }
            if (view != null) await view.RefreshBase(reloadDocument: relocated == null);
            if (relocated != null) { view!.AcceptHtml(doc.Text); view.ReloadSavedHtml(); }
            AddRecent(path!); noticedVersions.Remove(doc.Id); autoSaveErrors.Remove(doc.Id); pendingAutoSaves.Remove(doc.Id); ExternalNotice.Visibility = Visibility.Collapsed; UpdateStatus(); App.Current.MarkChanged(); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.EncoderFallbackException)
        { MessageBox.Show(this, "Could not save the file. Your text is still open.\n\n" + ex.Message, "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    async Task<bool> ConfirmSave(Document doc)
    {
        if (!await FlushDocument(doc)) return false;
        if (!doc.Dirty) return true;
        ActiveDocument = doc;
        var result = Dialogs.SaveChanges(this, doc.Name);
        return result == SaveChoice.Discard || (result == SaveChoice.Save && await SaveDocument(doc));
    }
    public async Task<bool> CloseDocument(Document doc)
    {
        if (doc.AutoSave) FlushAutoSaves(true);
        if (!Documents.Contains(doc) || !await ConfirmSave(doc)) return false;
        RemoveDocument(doc); return true;
    }
    internal void RemoveDocument(Document doc, bool fileDeleted = false)
    {
        int index = Documents.IndexOf(doc); bool active = doc == ActiveDocument;
        Documents.Remove(doc); editors.GetValueOrDefault(doc.Id)?.Dispose(); editors.Remove(doc.Id); restoredDocuments.Remove(doc.Id); noticedVersions.Remove(doc.Id); pendingAutoSaves.Remove(doc.Id); autoSaveErrors.Remove(doc.Id);
        if (Documents.Count == 0) NewDocument(fileDeleted ? doc.Path : null); else if (active) ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
        UpdateTabWidths(); FocusEditor(); App.Current.MarkChanged();
    }
    public async Task<bool> PrepareClose()
    {
        foreach (var doc in Documents.ToArray()) if (!await FlushDocument(doc)) return false;
        FlushAutoSaves(true);
        if (Preferences.AutoSaveAllOnClose)
            foreach (var doc in Documents.ToArray())
                if (doc.Dirty && doc.Path != null && !await SaveDocument(doc)) return false;
        if (!Preferences.RestoreSession) foreach (var doc in Documents.ToArray()) if (!await ConfirmSave(doc)) return false;
        return true;
    }
    bool closeApproved, closePending;
    async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (App.Current.TestMode || App.Current.Exiting || closeApproved) return;
        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        try
        {
            if (!await PrepareClose()) return;
            if (!App.Current.SaveState()) { MessageBox.Show(this, "Could not save your session: " + App.Current.PersistenceError); return; }
            closeApproved = true;
            if (Application.Current.Windows.OfType<MainWindow>().Count() == 1) App.Current.BeginExit();
            Close();
        }
        finally { closePending = false; }
    }
    public WindowSession Snapshot()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new() { Documents = Documents.ToList(), ActiveIndex = Math.Max(0, Documents.IndexOf(ActiveDocument!)), Width = bounds.Width, Height = bounds.Height, Maximized = WindowState == WindowState.Maximized, DocumentList = IsDocumentList, ListWidth = IsDocumentList ? ListColumn.ActualWidth : savedListWidth };
    }
    public void SetDocumentList(bool visible, bool persist = true)
    {
        if (!visible && IsDocumentList && ListColumn.ActualWidth >= 150) savedListWidth = ListColumn.ActualWidth;
        IsDocumentList = visible;
        DocumentPane.Visibility = DocumentSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        HorizontalNavigation.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        TabRow.Height = new GridLength(visible ? 0 : 39);
        ListColumn.MinWidth = visible ? 150 : 0;
        ListColumn.Width = new GridLength(visible ? savedListWidth : 0);
        SplitterColumn.Width = new GridLength(visible ? 5 : 0);
        if (persist) { Preferences.DocumentList = visible; Preferences.ListWidth = savedListWidth; App.Current.MarkChanged(); }
        ClampSidebar(); UpdateTabWidths(); FocusEditor();
    }
    void ClampSidebar()
    {
        if (!IsDocumentList || Workspace.ActualWidth <= 0) return;
        double maximum = Math.Max(150, Math.Min(900, Workspace.ActualWidth - 245));
        ListColumn.MaxWidth = maximum;
        if (ListColumn.Width.Value > maximum) ListColumn.Width = new GridLength(maximum);
    }
    public void ResizeDocumentList(double width)
    {
        savedListWidth = Math.Clamp(width, 150, Math.Max(150, Workspace.ActualWidth - 245));
        if (IsDocumentList) ListColumn.Width = new GridLength(savedListWidth);
        Preferences.ListWidth = savedListWidth; App.Current.MarkChanged();
    }
    void SplitterDragCompleted(object sender, DragCompletedEventArgs e) => ResizeDocumentList(ListColumn.ActualWidth);
    void UpdateTabWidths()
    {
        if (Tabs == null || Documents.Count == 0) return;
        double available = Math.Max(160, ActualWidth - 68);
        Tabs.MaxWidth = available;
        double width = Math.Clamp((available - 2 * Documents.Count) / Documents.Count, 88, 240);
        var style = new Style(typeof(ListBoxItem), (Style)FindResource("TabItemStyle")); style.Setters.Add(new Setter(WidthProperty, width)); Tabs.ItemContainerStyle = style;
    }
    void NavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is Document doc) { ActiveDocument = doc; if (list.IsMouseOver) FocusEditor(); }
    }
    void NavigationMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragOrigin = e.GetPosition(this); dragDocument = FindDocument(e.OriginalSource as DependencyObject);
    }
    void NavigationMouseMove(object sender, MouseEventArgs e)
    {
        if (dragging || dragDocument == null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragging = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("SinAIPrompt.Document", dragDocument.Id.ToString()), DragDropEffects.Move); }
        finally { dragging = false; dragDocument = null; }
    }
    Document? FindDocument(DependencyObject? node)
    {
        while (node != null) { if (node is ListBoxItem { DataContext: Document doc }) return doc; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
        return null;
    }
    async void NavigationMiddleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && FindDocument(e.OriginalSource as DependencyObject) is { } doc) { await CloseDocument(doc); e.Handled = true; }
    }
    public void MoveDocument(Document doc, int target)
    {
        int index = Documents.IndexOf(doc); if (index < 0) return;
        documentOrder.SetMode("manual");
        Documents.Move(index, Math.Clamp(target, 0, Documents.Count - 1)); ActiveDocument = doc; App.Current.MarkChanged();
    }
    void NavigationDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("SinAIPrompt.Document")) return;
        if (Guid.TryParse(e.Data.GetData("SinAIPrompt.Document") as string, out var id) && Documents.FirstOrDefault(d => d.Id == id) is { } doc)
        { var target = FindDocument(e.OriginalSource as DependencyObject); MoveDocument(doc, target == null ? Documents.Count - 1 : Documents.IndexOf(target)); }
        e.Handled = true;
    }
    void NavigationKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { FocusEditor(); e.Handled = true; } }
    void WindowDragOver(object sender, DragEventArgs e) { if (IsAnnotating) return; if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } }
    void WindowDrop(object sender, DragEventArgs e) { if (IsAnnotating) return; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { OpenPaths(paths); e.Handled = true; } }
    void FocusEditor() { if (IsLoaded) Dispatcher.BeginInvoke(() => CurrentView?.FocusEditing(), DispatcherPriority.Input); }
    public void ChangeZoom(int delta, bool absolute = false)
    {
        if (ActiveDocument == null) return;
        ActiveDocument.Zoom = Math.Clamp(absolute ? delta : ActiveDocument.Zoom + delta, 10, 500);
        CurrentView?.ApplyPreferences(); UpdateStatus(); App.Current.MarkChanged();
    }
    void UpdateStatus()
    {
        if (Editor == null || CurrentView == null || ActiveDocument == null || PositionStatus == null) return;
        if (CurrentView.IsVisual)
        {
            PositionStatus.Text = "Visual HTML";
            CountStatus.Text = $"{ActiveDocument.Text.Length:N0} characters";
        }
        else
        {
            var position = CurrentView.Gutter.Position(Editor.CaretIndex);
            PositionStatus.Text = $"Ln {position.Line:N0}, Col {position.Column:N0}";
            int totalLines = CurrentView.Gutter.LineCount;
            int count = TextFiles.Normalize(Editor.Text).Length;
            CountStatus.Text = $"{totalLines:N0} {(totalLines == 1 ? "line" : "lines")}  ·  {count:N0} characters" + (Editor.SelectionLength > 0 ? $"  ·  {Editor.SelectionLength:N0} selected" : "");
        }
        if (ActiveDocument.AutoSave) CountStatus.Text += autoSaveErrors.ContainsKey(ActiveDocument.Id) ? "  ·  Auto-save paused" : ActiveDocument.Dirty ? "  ·  Saving…" : "  ·  Saved";
        CountStatus.ToolTip = autoSaveErrors.GetValueOrDefault(ActiveDocument.Id) ?? (ActiveDocument.AutoSave ? ActiveDocument.Path : null);
        ZoomStatus.Content = $"{ActiveDocument.Zoom}%";
        NewlineStatus.Content = ActiveDocument.NewLine switch { "\n" => "Unix (LF)", "\r" => "Macintosh (CR)", _ => "Windows (CRLF)" };
        EncodingStatus.Content = ActiveDocument.EncodingName;
    }
    public void ApplyPreferences()
    {
        foreach (var view in editors.Values) view.ApplyPreferences();
        StatusBar.Visibility = Preferences.StatusBar ? Visibility.Visible : Visibility.Collapsed;
        ApplyTheme(); UpdateStatus(); App.Current.MarkChanged();
    }
    void ApplyTheme()
    {
        bool dark = Preferences.Theme == "Dark" || (Preferences.Theme == "System" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0);
        ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        var colors = new Dictionary<string, string>
        {
            ["ShellBrush"] = dark ? "#202020" : "#F3F3F3",
            ["EditorBrush"] = dark ? "#272727" : "#F9F9F9",
            ["TextBrush"] = dark ? "#F2F2F2" : "#202020",
            ["MutedBrush"] = dark ? "#A9A9A9" : "#606060",
            ["LineBrush"] = dark ? "#383838" : "#E5E5E5",
            ["HoverBrush"] = dark ? "#343434" : "#E9E9E9",
            ["SelectedBrush"] = dark ? "#333333" : "#FFFFFF",
            ["AccentBrush"] = dark ? "#60CDFF" : "#0067C0"
        };
        foreach (var pair in colors) Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) { int value = dark ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); int corner = 2; DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)); }
        foreach (var view in editors.Values) view.Gutter.RequestRefresh();
    }
    async void WindowActivated(object? sender, EventArgs e)
    {
        if (!initialized || externalChecking || App.Current.TestMode || ActiveDocument is not { Path: not null } doc) return;
        externalChecking = true;
        try
        {
            string? hash = await Task.Run(() => File.Exists(doc.Path) ? TextFiles.Hash(File.ReadAllBytes(doc.Path)) : null);
            if (doc != ActiveDocument) return;
            if (hash != doc.Fingerprint && (!noticedVersions.TryGetValue(doc.Id, out var seen) || seen != hash))
            { noticedVersions[doc.Id] = hash; ExternalMessage.Text = File.Exists(doc.Path) ? "This file changed outside Sin - AI Prompt." : "This file was moved or deleted. Your text is still open."; ExternalNotice.Visibility = Visibility.Visible; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { externalChecking = false; }
    }
    void ReloadClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is not { Path: not null } doc || Editor == null) return;
        if (doc.Dirty && MessageBox.Show(this, "Reloading will replace your unsaved changes with the file on disk. Continue?", "Reload file", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { var disk = TextFiles.Open(doc.Path); doc.EncodingName = disk.EncodingName; doc.SavedEncoding = disk.EncodingName; doc.NewLine = disk.NewLine; doc.SavedNewLine = disk.NewLine; doc.SavedText = disk.Text; doc.Fingerprint = disk.Fingerprint; Editor.Text = disk.Text; Editor.ClearUndo(); doc.Notify(); ExternalNotice.Visibility = Visibility.Collapsed; UpdateStatus(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, "Could not reload"); }
    }
    void KeepVersionClick(object sender, RoutedEventArgs e) { ExternalNotice.Visibility = Visibility.Collapsed; FocusEditor(); }
    public void ShowFind(bool replace = false) => SearchPanel.Show(replace);
    void UpdateSearchStatus() => SearchPanel.Refresh();
    public async Task<bool> FindNext(bool previous = false) => (await SearchPanel.ExecuteAsync(previous ? "previous" : "next")).Index > 0;
    void CloseSearchClick(object sender, RoutedEventArgs e) => SearchPanel.Close();
    public bool GoToLine(int line)
    {
        if (CurrentView == null || Editor == null || line < 1 || line > CurrentView.Gutter.LineCount) return false;
        int index = CurrentView.Gutter.LineStarts[line - 1]; Editor.Select(index, 0); Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(index)); FocusEditor(); return true;
    }
    void GoToClick(object sender, RoutedEventArgs e)
    {
        if (CurrentView == null || Editor == null) return;
        CurrentView.ShowSource();
        int current = CurrentView.Gutter.Position(Editor.CaretIndex).Line;
        int? line = Dialogs.LineNumber(this, current, CurrentView.Gutter.LineCount); if (line.HasValue) GoToLine(line.Value);
    }
    async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (IsAnnotating) return;
        ModifierKeys modifiers = e.KeyboardDevice.Modifiers;
        if (CurrentView?.IsVisual == false && TryInsertShortcut(e.Key, modifiers)) { e.Handled = true; return; }
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control), shift = modifiers.HasFlag(ModifierKeys.Shift), alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (ctrl)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.N: if (shift) NewWindowClick(this, e); else NewDocument(); break;
                case Key.T: NewDocument(); break;
                case Key.O: OpenClick(this, e); break;
                case Key.S: if (alt) SaveAllClick(this, e); else if (ActiveDocument != null) await SaveDocument(ActiveDocument, shift); break;
                case Key.W: if (shift) Close(); else if (ActiveDocument != null) await CloseDocument(ActiveDocument); break;
                case Key.U when shift: CurrentView?.ToggleSource(); break;
                case Key.L when shift: SetDocumentList(!IsDocumentList); break;
                case Key.F: ShowFind(); break;
                case Key.H: ShowFind(true); break;
                case Key.G: GoToClick(this, e); break;
                case Key.Tab: CycleDocument(shift ? -1 : 1); break;
                case Key.PageDown: CycleDocument(1); break;
                case Key.PageUp: CycleDocument(-1); break;
                case Key.OemPlus: case Key.Add: ChangeZoom(10); break;
                case Key.OemMinus: case Key.Subtract: ChangeZoom(-10); break;
                case Key.D0: case Key.NumPad0: ChangeZoom(100, true); break;
                default: e.Handled = false; return;
            }
        }
        else if (e.Key == Key.F3) { e.Handled = true; await FindNext(shift); }
        else if (e.Key == Key.F5) { InsertDate(); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible) { CloseSearchClick(this, e); e.Handled = true; }
    }
    void CycleDocument(int direction) { if (Documents.Count > 0) ActiveDocument = Documents[(Documents.IndexOf(ActiveDocument!) + direction + Documents.Count) % Documents.Count]; FocusEditor(); }
    internal bool TryInsertShortcut(Key key, ModifierKeys modifiers, DateTime? now = null)
    {
        if (modifiers != ModifierKeys.Control || Editor == null) return false;
        if (key == Key.D) { InsertDate(0, now, rememberChoice: false); return true; }
        if (key == Key.L) { InsertAtCaret(new string('_', 80)); return true; }
        if (key == Key.I) { InsertDateAndSeparator(now); return true; }
        return false;
    }
    void InsertAtCaret(string text)
    {
        if (Editor == null) return;
        if (CurrentView?.IsVisual == true) { CurrentView.Command("insertText", text); return; }
        int start = Editor.SelectionStart;
        Editor.SelectedText = text;
        Editor.Select(start + text.Length, 0);
        FocusEditor();
    }
    internal void InsertDate(int? choice = null, DateTime? now = null, bool rememberChoice = true)
    {
        int format = DateTimeFormats.NormalizeChoice(choice ?? Preferences.DateTimeFormat);
        if (Editor == null) return;
        InsertAtCaret(DateTimeFormats.Format(now ?? DateTime.Now, format));
        if (rememberChoice) { Preferences.DateTimeFormat = format; App.Current.MarkChanged(); }
    }
    void InsertSeparatorClick(object sender, RoutedEventArgs e) => InsertAtCaret(new string('_', 80));
    void InsertDateAndSeparator(DateTime? now = null) => InsertAtCaret(DateTimeFormats.Format(now ?? DateTime.Now, 0) + Environment.NewLine + new string('_', 80) + Environment.NewLine + Environment.NewLine);
    void InsertDateAndSeparatorClick(object sender, RoutedEventArgs e) => InsertDateAndSeparator();
    void DateTimeOpened(object sender, RoutedEventArgs e)
    {
        if (e.Source != DateTimeMenu) return;
        PopulateDateTimeMenu(DateTime.Now);
    }
    internal void PopulateDateTimeMenu(DateTime now)
    {
        DateTimeMenu.Items.Clear();
        for (int i = 0; i < DateTimeFormats.Count; i++)
        {
            int choice = i;
            var item = new MenuItem
            {
                Header = DateTimeFormats.Format(now, choice),
                IsCheckable = true,
                IsChecked = choice == DateTimeFormats.NormalizeChoice(Preferences.DateTimeFormat),
                InputGestureText = choice == 0 ? "Ctrl+D" : "",
                ToolTip = "Insert this format; F5 repeats your last choice."
            };
            item.Click += (_, _) => InsertDate(choice);
            DateTimeMenu.Items.Add(item);
        }
    }
    void MenuPreviewMouseDown(object sender, MouseButtonEventArgs e) => ToolbarVisibility.Toggle(e, Preferences, ApplyPreferences);
    void NewTabClick(object sender, RoutedEventArgs e) => NewDocument();
    void NewWindowClick(object sender, RoutedEventArgs e) { new MainWindow().Show(); }
    void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open", Multiselect = true, Filter = "HTML Documents (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        { if (Preferences.OpenInNewWindow) { var window = new MainWindow(); window.Show(); window.OpenPaths(dialog.FileNames); } else OpenPaths(dialog.FileNames); }
    }
    async void SaveClick(object sender, RoutedEventArgs e) { if (ActiveDocument != null) await SaveDocument(ActiveDocument); }
    async void SaveAsClick(object sender, RoutedEventArgs e) { if (ActiveDocument != null) await SaveDocument(ActiveDocument, true); }
    async void SaveAllClick(object sender, RoutedEventArgs e) => await SaveAllDocuments();
    async void CloseTabClick(object sender, RoutedEventArgs e) { if (ActiveDocument != null) await CloseDocument(ActiveDocument); }
    void WindowCloseClick(object sender, RoutedEventArgs e) => Close();
    void ExitClick(object sender, RoutedEventArgs e) => App.Current.ExitAll();
    void UndoClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("undo"); else Editor?.Undo(); FocusEditor(); }
    void RedoClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("redo"); else Editor?.Redo(); FocusEditor(); }
    void CutClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("cut"); else Editor?.Cut(); FocusEditor(); }
    void CopyClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("copy"); else Editor?.Copy(); FocusEditor(); }
    void PasteClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("paste"); else Editor?.Paste(); FocusEditor(); }
    void DeleteClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("delete"); else if (Editor != null) Editor.SelectedText = ""; FocusEditor(); }
    void SelectAllClick(object sender, RoutedEventArgs e) { if (CurrentView?.IsVisual == true) CurrentView.Command("selectAll"); else Editor?.SelectAll(); FocusEditor(); }
    void FindClick(object sender, RoutedEventArgs e) => ShowFind();
    void ReplaceClick(object sender, RoutedEventArgs e) => ShowFind(true);
    async void FindNextClick(object sender, RoutedEventArgs e) => await FindNext();
    async void FindPreviousClick(object sender, RoutedEventArgs e) => await FindNext(true);
    void ZoomInClick(object sender, RoutedEventArgs e) => ChangeZoom(10);
    void ZoomOutClick(object sender, RoutedEventArgs e) => ChangeZoom(-10);
    void ResetZoomClick(object sender, RoutedEventArgs e) => ChangeZoom(100, true);
    void StatusBarClick(object sender, RoutedEventArgs e) { Preferences.StatusBar = !Preferences.StatusBar; ApplyPreferences(); }
    internal void SetLineNumbers(bool visible)
    {
        Preferences.LineNumbers = visible;
        foreach (var window in Application.Current.Windows.OfType<MainWindow>()) window.ApplyPreferences();
        App.Current.MarkChanged();
    }
    void LineNumbersClick(object sender, RoutedEventArgs e) => SetLineNumbers(!Preferences.LineNumbers);
    void WordWrapClick(object sender, RoutedEventArgs e) { Preferences.WordWrap = !Preferences.WordWrap; ApplyPreferences(); }
    void DocumentListClick(object sender, RoutedEventArgs e) => SetDocumentList(!IsDocumentList);
    void SettingsClick(object sender, RoutedEventArgs e) { SettingsDialog.Show(this); foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.ApplyPreferences(); }
    void EditOpened(object sender, RoutedEventArgs e) { UndoMenu.IsEnabled = CurrentView?.IsVisual == true || Editor?.CanUndo == true; RedoMenu.IsEnabled = CurrentView?.IsVisual == true || Editor?.CanRedo == true; CutMenu.IsEnabled = CopyMenu.IsEnabled = DeleteMenu.IsEnabled = CurrentView?.IsVisual == true || Editor?.SelectionLength > 0; }
    void ViewOpened(object sender, RoutedEventArgs e) { StatusBarMenu.IsChecked = Preferences.StatusBar; WordWrapMenu.IsChecked = Preferences.WordWrap; DocumentListMenu.IsChecked = IsDocumentList; LineNumbersMenu.IsChecked = Preferences.LineNumbers; }
    void RecentOpened(object sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        foreach (var path in Preferences.Recent.Take(Settings.RecentFileLimit).ToArray()) { var item = new MenuItem { Header = path.Replace("_", "__"), ToolTip = path }; item.Click += (_, _) => OpenPaths([path]); RecentMenu.Items.Add(item); }
        if (RecentMenu.Items.Count == 0) RecentMenu.Items.Add(new MenuItem { Header = "No Recently Opened Files", IsEnabled = false });
    }
    void NewlineClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument == null) return;
        var menu = new ContextMenu();
        foreach (var (name, value) in new[] { ("Windows (CRLF)", "\r\n"), ("Unix (LF)", "\n"), ("Macintosh (CR)", "\r") })
        { var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = ActiveDocument.NewLine == value }; item.Click += (_, _) => { ActiveDocument.NewLine = value; ActiveDocument.Notify(); UpdateStatus(); App.Current.MarkChanged(); }; menu.Items.Add(item); }
        menu.PlacementTarget = NewlineStatus; menu.IsOpen = true;
    }
    void EncodingClick(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument == null) return;
        var menu = new ContextMenu();
        foreach (var name in new[] { "UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "ANSI" })
        { var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = ActiveDocument.EncodingName == name }; item.Click += (_, _) => { ActiveDocument.EncodingName = name; ActiveDocument.Notify(); UpdateStatus(); App.Current.MarkChanged(); }; menu.Items.Add(item); }
        menu.PlacementTarget = EncodingStatus; menu.IsOpen = true;
    }
}
