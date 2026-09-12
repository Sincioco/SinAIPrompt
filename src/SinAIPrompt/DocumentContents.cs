using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns only the active document's outline and its delayed refresh. Never opens files.
public sealed class DocumentContents : DockPanel
{
    public static readonly DependencyProperty SourceViewProperty = DependencyProperty.Register(nameof(SourceView), typeof(object), typeof(DocumentContents), new PropertyMetadata(null, SourceChanged));
    public object? SourceView { get => GetValue(SourceViewProperty); set => SetValue(SourceViewProperty, value); }
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly ListBox list = new() { BorderThickness = new Thickness(0) };
    internal ItemCollection Items => list.Items;
    Action<bool>? visibilityChanged;
    int revision;
    bool updating;
    public DocumentContents()
    {
        Width = 220;
        SetResourceReference(BackgroundProperty, "ShellBrush");
        list.SetResourceReference(BackgroundProperty, "ShellBrush"); list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var header = new DockPanel { Height = 40, Margin = new Thickness(10, 0, 4, 0) };
        var close = new Button { Content = "×", Width = 28, ToolTip = "Close Content View" };
        close.SetResourceReference(StyleProperty, "FlatButton"); close.Click += (_, _) => SetVisible(false);
        SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "Content View", VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); header.Children.Add(title);
        SetDock(header, Dock.Top); Children.Add(header); Children.Add(list);
        System.Windows.Automation.AutomationProperties.SetName(this, "Document Contents");
        refresh.Tick += async (_, _) => { refresh.Stop(); await UpdateAsync(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueRefresh(); else refresh.Stop(); };
        list.SelectionChanged += async (_, _) =>
        {
            if (!updating && list.SelectedItem is ListBoxItem { Tag: int index } && SourceView is EditorView view)
                await view.Browser.ExecuteScriptAsync($"window.editor.outline.jump({index})");
        };
    }
    internal void Initialize(MenuItem menu, Settings preferences, Action changed)
    {
        visibilityChanged = visible => { menu.IsChecked = preferences.ContentView = visible; changed(); };
        menu.Click += (_, _) => SetVisible(menu.IsChecked);
        SetVisible(preferences.ContentView);
    }
    internal void SetVisible(bool visible)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        visibilityChanged?.Invoke(visible);
    }
    static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var contents = (DocumentContents)sender;
        if (args.OldValue is EditorView old) { old.HtmlChanged -= contents.Changed; old.ChromeChanged -= contents.Changed; }
        if (args.NewValue is EditorView next) { next.HtmlChanged += contents.Changed; next.ChromeChanged += contents.Changed; }
        contents.revision++; contents.Items.Clear(); contents.QueueRefresh();
    }
    void Changed(object? sender, EventArgs e) => QueueRefresh();
    void QueueRefresh() { if (!IsVisible) return; refresh.Stop(); refresh.Start(); }
    internal async Task UpdateAsync()
    {
        if (SourceView is not EditorView view || !view.IsVisual || view.Browser.CoreWebView2 == null) return;
        int current = ++revision;
        string raw = await view.Browser.ExecuteScriptAsync("window.editor?.outline.items()||[]");
        if (current != revision) return;
        using var data = JsonDocument.Parse(raw);
        updating = true;
        try
        {
            Items.Clear();
            foreach (var item in data.RootElement.EnumerateArray())
                Items.Add(new ListBoxItem { Content = new TextBlock { Text = item.GetProperty("text").GetString(), TextWrapping = TextWrapping.Wrap },
                    Tag = item.GetProperty("index").GetInt32(), Padding = new Thickness(12 + item.GetProperty("level").GetInt32() * 14, 8, 8, 8) });
            if (Items.Count == 0) Items.Add(new ListBoxItem { Content = "Apply Title, Heading or Heading2 to create an outline.", IsEnabled = false });
        }
        finally { updating = false; }
    }
}
