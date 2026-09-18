using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class NavigationFilterSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var explorer = window.Explorer;
        bool mode = explorer.ExplorerMode, visible = window.IsDocumentList;
        var active = window.ActiveDocument!;
        string root = App.Current.Preferences.ExplorerDirectory;
        var list = window.DocumentList;
        bool pin = active.Pinned, locked = active.IsReadOnly;
        try
        {
            window.SetDocumentList(true); explorer.SetMode(false);
            active.Pinned = true; active.IsReadOnly = true; active.Notify(); await Task.Delay(80);
            list.ScrollIntoView(active); window.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(active);
            var scrollbar = NavigationSelfTest.Descendants(list).OfType<ScrollBar>().Single(bar => bar.Orientation == Orientation.Vertical);
            double left = scrollbar.TranslatePoint(new Point(), list).X;
            var markers = NavigationSelfTest.Descendants(row).OfType<TextBlock>().Where(text => text.Text == active.Marker || text.Text == active.LockMarker).ToArray();
            check(scrollbar.IsVisible && markers.Length == 2 && markers.All(text => text.TranslatePoint(new Point(text.ActualWidth, 0), list).X <= left),
                "Pinned and locked Document List markers remain entirely left of the vertical scrollbar");
            check(explorer.SearchFiles.TranslatePoint(new Point(explorer.SearchFiles.ActualWidth, 0), explorer).X <= explorer.ChooseFolder.TranslatePoint(new Point(), explorer).X,
                "Filename search icon sits immediately left of the shared folder icon");
            int editors = window.CreatedEditorCount, count = list.Items.Count;
            explorer.SearchFiles.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(explorer.FilterPanel.IsVisible, "Search icon exposes the filename filter");
            explorer.FilterInput.Text = "does-not-match-any-file";
            check(list.Items.Count == count, "Filename filtering waits for its debounce instead of refreshing each keystroke");
            await Task.Delay(350);
            check(list.Items.Count == 0 && explorer.FilterStatus.Text == "No matching files" && window.ActiveDocument == active,
                "A filter with no matches preserves the current document and reports the empty result");
            check(((ListBox)window.FindName("Tabs")).Items.Count == count && window.CreatedEditorCount == editors,
                "Filtering leaves tabs intact and creates no editors");
            explorer.FilterInput.Text = "INACTIVE"; await Task.Delay(350);
            check(list.Items.Count == 1 && ((Document)list.Items[0]).Name == "Inactive.html", "Document List filters names without case sensitivity");
            explorer.SearchFiles.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
            check(list.Items.Count == count && list.Margin.Top == 40 && !explorer.FilterPanel.IsVisible,
                "Closing search clears its query and restores Document List space");
            string folder = Path.Combine(App.Current.Store.DirectoryPath, "filter-fixture"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Alpha.html"), "<p>Alpha</p>");
            File.WriteAllText(Path.Combine(folder, "Beta.md"), "# Beta");
            await explorer.SetFolderAsync(folder); explorer.SetMode(true);
            explorer.SearchFiles.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            explorer.FilterInput.Text = "beta";
            for (int i = 0; i < 50 && explorer.FilterStatus.Text != "1 matching items"; i++) await Task.Delay(20);
            window.UpdateLayout();
            var rows = explorer.Tree.Items.Cast<TreeViewItem>().ToArray();
            check(rows.Count(row => row.IsVisible) == 1 && ((PromptEntry)rows.Single(row => row.IsVisible).Tag).Name == "Beta.md",
                "Prompt Explorer uses the same debounced filename filter for all supported file types: " + explorer.FilterStatus.Text);
            check(window.ActiveDocument == active && window.CreatedEditorCount == editors, "Explorer filtering never opens matching files");
            var alpha = rows.Single(row => ((PromptEntry)row.Tag).Name == "Alpha.html");
            explorer.ExplorerSelection.Set(alpha.Tag, true);
            await explorer.RefreshAsync(); explorer.FilterInput.Clear(); await Task.Delay(350); window.UpdateLayout();
            var selected = NavigationSelfTest.Descendants(explorer.Tree).OfType<CombineSelectionBox>().Single(box => box.Item is PromptEntry { Name: "Alpha.html" });
            check(selected.IsChecked == true && selected.Content.ToString() == "1", "Combine selection order survives filtering and Explorer refresh");
            explorer.ExplorerSelection.Clear();
        }
        finally
        {
            if (explorer.FilterPanel.IsVisible) explorer.SearchFiles.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            active.Pinned = pin; active.IsReadOnly = locked; active.Notify();
            if (root.Length > 0) await explorer.SetFolderAsync(root);
            explorer.SetMode(mode); window.SetDocumentList(visible); window.ActiveDocument = active;
        }
    }
}
