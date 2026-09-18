using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Filters navigation only: no file reads, document mutation or editor creation.
internal sealed class NavigationFilter : IDisposable
{
    readonly ListBox documents;
    readonly TreeView tree;
    readonly Button toggle;
    readonly FrameworkElement panel;
    readonly TextBox input;
    readonly TextBlock status;
    readonly ListCollectionView view;
    readonly Func<PromptEntry, bool> entryVisible;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    string query = "";
    bool explorerMode;

    internal NavigationFilter(ListBox documents, IList source, TreeView tree, Button toggle, FrameworkElement panel, TextBox input, TextBlock status,
        Func<Document, bool> documentVisible, Func<PromptEntry, bool> entryVisible)
    {
        this.documents = documents; this.tree = tree; this.toggle = toggle;
        this.panel = panel; this.input = input; this.status = status;
        this.entryVisible = entryVisible;
        // A private view leaves Tabs, which share the source collection, unfiltered.
        view = new ListCollectionView(source);
        view.Filter = item => item is Document doc && documentVisible(doc) && Matches(doc.Name);
        view.LiveFilteringProperties.Add(nameof(Document.Name)); view.IsLiveFiltering = true;
        documents.IsSynchronizedWithCurrentItem = false;
        documents.ItemsSource = view;
        input.TextChanged += TextChanged; input.PreviewKeyDown += KeyDown;
        toggle.Click += Toggle; panel.SizeChanged += Resize; debounce.Tick += Tick;
    }
    bool Matches(string name) => name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    void TextChanged(object sender, TextChangedEventArgs e) { debounce.Stop(); debounce.Start(); }
    void Tick(object? sender, EventArgs e) { debounce.Stop(); query = input.Text.Trim(); Refresh(); }
    void Toggle(object sender, RoutedEventArgs e)
    {
        if (panel.Visibility == Visibility.Visible) Hide();
        else { panel.Visibility = Visibility.Visible; input.Focus(); input.SelectAll(); }
    }
    void Hide()
    {
        input.Clear(); debounce.Stop(); query = ""; panel.Visibility = Visibility.Collapsed;
        Refresh(); Resize();
        if (explorerMode) tree.Focus(); else documents.Focus();
    }
    void KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Hide(); } }
    void Resize(object? sender = null, SizeChangedEventArgs? e = null) =>
        documents.Margin = new Thickness(0, 40 + (panel.Visibility == Visibility.Visible ? panel.ActualHeight + panel.Margin.Top + panel.Margin.Bottom : 0), 0, 0);

    internal void SetMode(bool explorer) { explorerMode = explorer; Refresh(); }
    internal void Refresh()
    {
        view.Refresh();
        documents.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
        bool FilterRow(TreeViewItem row, bool ancestorMatches)
        {
            if (row.Tag is not PromptEntry entry) return false;
            if (!entryVisible(entry)) { row.Visibility = Visibility.Collapsed; return false; }
            bool match = ancestorMatches || Matches(entry.Name), childMatch = false;
            foreach (var child in row.Items.OfType<TreeViewItem>()) childMatch |= FilterRow(child, match);
            row.Visibility = match || childMatch ? Visibility.Visible : Visibility.Collapsed;
            return match || childMatch;
        }
        foreach (var row in tree.Items.OfType<TreeViewItem>()) FilterRow(row, false);
        int count = explorerMode ? tree.Items.OfType<TreeViewItem>().Count(row => row.Visibility == Visibility.Visible) : view.Count;
        status.Text = query.Length == 0 ? "" : count == 0 ? "No matching files" : $"{count} matching items";
        status.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void Dispose()
    {
        debounce.Stop(); debounce.Tick -= Tick;
        input.TextChanged -= TextChanged; input.PreviewKeyDown -= KeyDown;
        toggle.Click -= Toggle; panel.SizeChanged -= Resize;
        view.IsLiveFiltering = false; view.Filter = null;
    }
}
