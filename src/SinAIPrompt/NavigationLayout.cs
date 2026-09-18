using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns independent sidebar/tab visibility and sidebar width, without document state.
internal sealed class NavigationLayout(Grid workspace, FrameworkElement pane, FrameworkElement splitter,
    FrameworkElement tabs, RowDefinition tabRow, ColumnDefinition listColumn, ColumnDefinition splitterColumn,
    MenuItem tabsMenu, Settings preferences, Action changed)
{
    public bool Visible { get; private set; }
    public bool TabsVisible { get; private set; }
    public double SavedWidth { get; set; } = 250;
    public double Width => Visible ? listColumn.ActualWidth : SavedWidth;

    public void SetVisible(bool visible, bool persist)
    {
        if (!visible && Visible && listColumn.ActualWidth >= 150) SavedWidth = listColumn.ActualWidth;
        Visible = visible;
        pane.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        listColumn.MinWidth = visible ? 150 : 0;
        listColumn.Width = new GridLength(visible ? SavedWidth : 0);
        splitterColumn.Width = new GridLength(visible ? 5 : 0);
        if (persist) { preferences.DocumentList = visible; preferences.ListWidth = SavedWidth; changed(); }
        Clamp();
    }
    public void InitializeTabs(bool? saved)
    {
        SetTabsVisible(saved ?? preferences.ShowTabs ?? !Visible, false);
        tabsMenu.Click += (_, _) => SetTabsVisible(tabsMenu.IsChecked, true);
    }
    public void SetTabsVisible(bool visible, bool persist)
    {
        TabsVisible = tabsMenu.IsChecked = visible;
        tabs.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        tabRow.Height = new GridLength(visible ? 39 : 0);
        if (persist) { preferences.ShowTabs = visible; changed(); }
    }
    public void Clamp()
    {
        if (!Visible || workspace.ActualWidth <= 0) return;
        double maximum = Math.Max(150, Math.Min(900, workspace.ActualWidth - 245));
        listColumn.MaxWidth = maximum;
        if (listColumn.Width.Value > maximum) listColumn.Width = new GridLength(maximum);
    }
    public void Resize(double width)
    {
        SavedWidth = Math.Clamp(width, 150, Math.Max(150, workspace.ActualWidth - 245));
        if (Visible) listColumn.Width = new GridLength(SavedWidth);
        preferences.ListWidth = SavedWidth; changed();
    }
    public void ScrollToTop(ListBox list, object selected)
    {
        // Selection and virtualized layout can queue their own BringIntoView.
        // Scroll afterward, unless the user has already selected another file.
        list.Dispatcher.BeginInvoke(() =>
        {
            if (!list.IsLoaded || list.SelectedItem != selected) return;
            list.ApplyTemplate();
            if (list.Template.FindName("PART_ScrollViewer", list) is ScrollViewer scroll) scroll.ScrollToTop();
        }, DispatcherPriority.ContextIdle);
    }
}
