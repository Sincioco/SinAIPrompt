using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SinAIPrompt;

// Pixel scrolling and readable tab widths; documents and ordering remain in their owners.
internal sealed class TabStripLayout
{
    readonly ListBox tabs;
    readonly FrameworkElement row;
    readonly ButtonBase left, right;
    readonly Style baseStyle;
    ScrollViewer? scroll;
    double lastWidth;

    public TabStripLayout(ListBox tabs, FrameworkElement row, ButtonBase left, ButtonBase right)
    {
        this.tabs = tabs; this.row = row; this.left = left; this.right = right;
        baseStyle = tabs.ItemContainerStyle;
        tabs.Loaded += (_, _) => Update();
        row.SizeChanged += (_, _) => Update();
        row.IsVisibleChanged += (_, _) => Update();
        left.Click += (_, _) => Scroll(-240);
        right.Click += (_, _) => Scroll(240);
        row.PreviewMouseWheel += (_, e) =>
        {
            if (scroll?.ScrollableWidth > 0) { Scroll(-e.Delta); e.Handled = true; }
        };
    }
    public void Update()
    {
        if (tabs.Items.Count == 0 || row.ActualWidth <= 0) return;
        if (scroll == null && row.IsVisible)
        {
            tabs.ApplyTemplate(); scroll = FindScroll(tabs);
            if (scroll != null) scroll.ScrollChanged += (_, _) => UpdateButtons();
        }
        double available = Math.Max(140, row.ActualWidth - 36);
        bool overflow = tabs.Items.Count * 142 > available;
        left.Visibility = right.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        if (overflow) available -= 60;
        double width = Math.Clamp(available / tabs.Items.Count - 2, 140, 240);
        if (Math.Abs(width - lastWidth) > .5)
        {
            var style = new Style(typeof(ListBoxItem), baseStyle);
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, width));
            tabs.ItemContainerStyle = style; lastWidth = width;
        }
        UpdateButtons();
    }
    void Scroll(double amount) => scroll?.ScrollToHorizontalOffset(scroll.HorizontalOffset + amount);
    void UpdateButtons()
    {
        left.IsEnabled = scroll?.HorizontalOffset > .5;
        right.IsEnabled = scroll != null && scroll.HorizontalOffset < scroll.ScrollableWidth - .5;
    }
    static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScroll(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
}
