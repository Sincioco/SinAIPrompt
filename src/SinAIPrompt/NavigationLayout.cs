using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns sidebar visibility and width; neither documents nor directory state live here.
internal sealed class NavigationLayout(Grid workspace, FrameworkElement pane, FrameworkElement splitter,
    FrameworkElement tabs, RowDefinition tabRow, ColumnDefinition listColumn, ColumnDefinition splitterColumn,
    Settings preferences, Action changed)
{
    public bool Visible { get; private set; }
    public double SavedWidth { get; set; } = 250;
    public double Width => Visible ? listColumn.ActualWidth : SavedWidth;

    public void SetVisible(bool visible, bool persist)
    {
        if (!visible && Visible && listColumn.ActualWidth >= 150) SavedWidth = listColumn.ActualWidth;
        Visible = visible;
        pane.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        tabs.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        tabRow.Height = new GridLength(visible ? 0 : 39);
        listColumn.MinWidth = visible ? 150 : 0;
        listColumn.Width = new GridLength(visible ? SavedWidth : 0);
        splitterColumn.Width = new GridLength(visible ? 5 : 0);
        if (persist) { preferences.DocumentList = visible; preferences.ListWidth = SavedWidth; changed(); }
        Clamp();
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
}
