using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SinAIPrompt;

// Coordinates the native navigation overlay with the active browser's ribbon height.
// The browser's native window region reserves an empty left area for WPF controls.
internal sealed class EditorChromeLayout
{
    readonly Grid workspace;
    readonly FrameworkElement pane, splitter, search, contents;
    readonly EditorSurface host;
    EditorView? active;
    double lastRibbonHeight = 96;
    (FrameworkElement? Content, bool Visual, double Left, double Top, bool Modal)? arranged;

    internal EditorChromeLayout(Grid workspace, EditorSurface host, FrameworkElement pane, FrameworkElement splitter, FrameworkElement search, FrameworkElement contents)
    {
        this.workspace = workspace; this.host = host; this.pane = pane; this.splitter = splitter;
        this.search = search; this.contents = contents;
        DependencyPropertyDescriptor.FromProperty(EditorSurface.ContentProperty, typeof(EditorSurface)).AddValueChanged(host, ContentChanged);
        workspace.SizeChanged += (_, _) => Arrange();
        workspace.LayoutUpdated += (_, _) => Arrange();
        pane.SizeChanged += (_, _) => Arrange();
        pane.IsVisibleChanged += (_, _) => Arrange();
        search.SizeChanged += (_, _) => Arrange();
        search.IsVisibleChanged += (_, _) => Arrange();
        contents.SizeChanged += (_, _) => Arrange();
        contents.IsVisibleChanged += (_, _) => Arrange();
        ContentChanged(null, EventArgs.Empty);
    }
    void ContentChanged(object? sender, EventArgs e)
    {
        if (active != null)
        {
            active.ChromeChanged -= Changed;
            if (host.Content == null) { active.Margin = new Thickness(0); active.SetNavigationInset(0); }
        }
        active = host.Content as EditorView;
        if (active != null) { active.ReserveRibbonHeight(lastRibbonHeight); active.ChromeChanged += Changed; }
        Arrange();
    }
    void Changed(object? sender, EventArgs e) => Arrange();
    void Arrange()
    {
        double left = (search.Visibility == Visibility.Visible ? workspace.ColumnDefinitions[0].ActualWidth : 0) +
            (pane.Visibility == Visibility.Visible ? workspace.ColumnDefinitions[1].ActualWidth + workspace.ColumnDefinitions[2].ActualWidth : 0) +
            (contents.Visibility == Visibility.Visible ? workspace.ColumnDefinitions[3].ActualWidth : 0);
        double top = active?.IsVisual == true ? active.RibbonHeight : 0;
        if (top > 0) lastRibbonHeight = top;
        bool modal = active?.IsVisual == true && active.Browser.IsModal;
        var next = (host.Content, active?.IsVisual == true, left, top, modal);
        if (arranged == next) return;
        arranged = next;
        pane.IsEnabled = splitter.IsEnabled = search.IsEnabled = contents.IsEnabled = !modal;
        pane.Margin = splitter.Margin = search.Margin = contents.Margin = new Thickness(0, top, 0, 0);
        if (active != null)
        {
            active.Margin = new Thickness(active.IsVisual ? 0 : left, 0, 0, 0);
            active.SetNavigationInset(active.IsVisual ? left : 0);
        }
        else if (host.Content is FrameworkElement preview) preview.Margin = new Thickness(left, 0, 0, 0);
    }
}
