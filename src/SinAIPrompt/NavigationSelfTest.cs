using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class NavigationSelfTest
{
    public static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var menu = (MenuItem)window.FindName("TabBarMenu");
        var tabs = (ListBox)window.FindName("Tabs");
        var row = (FrameworkElement)window.FindName("HorizontalNavigation");
        var right = (ButtonBase)window.FindName("ScrollTabsRight");
        bool originalTabs = menu.IsChecked, originalList = window.IsDocumentList;
        void ShowTabs(bool show) { menu.IsChecked = show; menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
        try
        {
            check(!originalTabs, "Legacy sidebar sessions preserve their initial hidden tab bar");
            foreach (bool showTabs in new[] { true, false })
                foreach (bool showList in new[] { true, false })
                {
                    ShowTabs(showTabs); window.SetDocumentList(showList); window.UpdateLayout();
                    check(row.IsVisible == showTabs && window.IsDocumentList == showList,
                        $"Tab visibility {showTabs} is independent of sidebar visibility {showList}");
                }
            ShowTabs(true); window.UpdateLayout(); await Task.Delay(60);
            var scroll = Descendants(tabs).OfType<ScrollViewer>().First();
            scroll.ScrollToLeftEnd(); await Task.Delay(30);
            check(right.IsVisible && right.IsEnabled && scroll.ScrollableWidth > 0, $"Crowded tabs expose scrolling buttons and preserve readable widths (visible={right.IsVisible}, enabled={right.IsEnabled}, extent={scroll.ExtentWidth}, viewport={scroll.ViewportWidth}, offset={scroll.HorizontalOffset})");
            right.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Task.Delay(30);
            check(scroll.HorizontalOffset > 0, "The right tab control scrolls horizontally");
            double before = scroll.HorizontalOffset;
            row.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            await Task.Delay(30); check(scroll.HorizontalOffset > before, "Mousewheel over the tab bar scrolls horizontally");
            var document = window.ActiveDocument!; bool pinned = document.Pinned; document.Pinned = true; document.Notify(); await Task.Delay(40);
            tabs.ScrollIntoView(document); window.UpdateLayout();
            var item = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(document);
            var marker = Descendants(item).OfType<TextBlock>().First(text => text.Text.Contains("📌"));
            check(marker.ActualWidth + marker.Margin.Left + marker.Margin.Right + .5 >= marker.DesiredSize.Width && ((Grid)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(marker))).ColumnDefinitions[1].Width.IsAuto,
                "Pinned tab markers use their full desired width instead of a clipped fixed column");
            document.Pinned = pinned; document.Notify();
            var session = window.Snapshot();
            check(session.ShowTabs == true && new Settings().WrapToolbar && new Settings().ShowTabs == null,
                "Independent tab visibility persists while old settings retain default wrapping");
            var wrapMenu = (MenuItem)window.FindName("WrapToolbarMenu");
            wrapMenu.IsChecked = false; wrapMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(60);
            check(await window.CurrentView!.Browser.ExecuteScriptAsync("document.querySelector('#toolbar').classList.contains('ribbon-nowrap')") == "true",
                "View menu disables ribbon wrapping in the active editor");
            wrapMenu.IsChecked = true; wrapMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(60);
            check(await window.CurrentView.Browser.ExecuteScriptAsync("document.querySelector('#toolbar').classList.contains('ribbon-nowrap')") == "false",
                "View menu restores normal ribbon wrapping");
            double originalWidth = window.Width;
            try
            {
                foreach (double width in new[] { 1000d, 1500d, 1100d })
                {
                    window.Width = width; window.SetDocumentList(true); window.UpdateLayout(); await Task.Delay(40);
                    check(await window.CurrentView.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left>100") == "true", "Navigation reserves document width after resizing to " + width);
                    window.SetDocumentList(false); window.UpdateLayout(); await Task.Delay(40);
                    check(window.ListColumn.ActualWidth == 0 && window.SplitterColumn.ActualWidth == 0 && await window.CurrentView.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left===0") == "true",
                        "Hiding navigation removes both native and browser space after resizing to " + width);
                }
                window.SetDocumentList(true);
                window.Explorer.ModeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(40);
                var popup = PresentationSource.CurrentSources.OfType<HwndSource>().Where(source => source.RootVisual != null).SelectMany(source => Descendants(source.RootVisual)).OfType<ContextMenu>().Single();
                check(popup.Items.OfType<MenuItem>().All(item => item.FontFamily.Source == "Segoe UI") && popup.Items.Count == 2,
                    "Navigation mode labels use the text font instead of inheriting the icon button font");
                popup.IsOpen = false;
                await PopupNavigation(window, check);
            }
            finally { window.Width = originalWidth; }
        }
        finally { ShowTabs(originalTabs); window.SetDocumentList(originalList); }
    }
    static async Task PopupNavigation(MainWindow window, Action<bool, string> check)
    {
        var browser = window.CurrentView!.Browser;
        bool explorer = window.Explorer.ExplorerMode, topmost = window.Topmost;
        var pane = (FrameworkElement)window.FindName("DocumentPane");
        window.Width = 1450; window.Topmost = true; window.Activate(); window.UpdateLayout();
        try
        {
            await browser.ExecuteScriptAsync("window.editor.setToolbarWrap(false)"); await Task.Delay(80);
            await window.Explorer.SetFolderAsync(System.IO.Path.Combine(App.Current.Store.DirectoryPath, "documents"));
            foreach (bool tree in new[] { false, true })
            {
                window.Explorer.SetMode(tree); await Task.Delay(100);
                foreach (string button in new[] { "moreRibbon", "listNumbering", "moreStyles" })
                {
                    await browser.ExecuteScriptAsync($"document.querySelector('#{button}').click()"); await Task.Delay(100);
                    check(await browser.ExecuteScriptAsync("!!document.querySelector('dialog[open],:popover-open')") == "true" && NavigationClipped(browser),
                        $"{(tree ? "Prompt Explorer" : "Document List")} stays visible while {button} is open");
                    check(pane.IsEnabled == (button != "listNumbering"), "Only a modal dialog disables native navigation: " + button);
                    var target = new ScreenCapture.Target("Navigation popup test", new WindowInteropHelper(window).Handle, default);
                    var bounds = ScreenCapture.Bounds(target);
                    var bitmap = await Task.Run(() => ScreenCapture.Capture(bounds));
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(App.Current.Store.DirectoryPath, $"navigation-{(tree ? "explorer" : "list")}-{button}.png"), Convert.FromBase64String(ScreenCapture.Png(bitmap).Split(',')[1]));
                    await browser.ExecuteScriptAsync("document.querySelectorAll('dialog[open]').forEach(p=>p.close('cancel'));document.querySelectorAll(':popover-open').forEach(p=>p.hidePopover())");
                    await Task.Delay(80);
                    check(pane.IsEnabled && NavigationClipped(browser), "Closing " + button + " preserves and re-enables navigation");
                }
            }
            await browser.ExecuteScriptAsync("window.chromeProbe=document.createElement('div');chromeProbe.popover='auto';chromeProbe.textContent='Overlay regression';chromeProbe.style.cssText='position:fixed;inset:auto;left:0;top:200px;width:180px;height:80px;margin:0';document.body.append(chromeProbe);chromeProbe.showPopover()");
            await Task.Delay(80);
            check(NavigationClipped(browser) && RegionContains(browser, 40, 230), "A popup overlaps only its own rectangle; the rest of navigation stays visible");
            await browser.ExecuteScriptAsync("chromeProbe.remove()"); await Task.Delay(80);
            check(NavigationClipped(browser) && !RegionContains(browser, 40, 230), "Removing an overlapping popup restores its part of navigation");
        }
        finally
        {
            await browser.ExecuteScriptAsync("window.chromeProbe?.remove();document.querySelectorAll('dialog[open]').forEach(p=>p.close('cancel'));document.querySelectorAll(':popover-open').forEach(p=>p.hidePopover());window.editor.setToolbarWrap(true)");
            window.Explorer.SetMode(explorer);
            window.Topmost = topmost;
        }
    }
    internal static bool RegionContains(RibbonWebView browser, double x, double y)
    {
        nint region = CreateRectRgn(0, 0, 0, 0); var dpi = VisualTreeHelper.GetDpi(browser);
        try { return GetWindowRgn(browser.Handle, region) > 0 && PtInRegion(region, (int)(x * browser.ZoomFactor * dpi.DpiScaleX), (int)(y * browser.ZoomFactor * dpi.DpiScaleY)); }
        finally { DeleteObject(region); }
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    internal static bool NavigationClipped(RibbonWebView browser)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        try { return GetWindowRgn(browser.Handle, region) > 0 && !PtInRegion(region, 10, (int)(browser.ActualHeight * VisualTreeHelper.GetDpi(browser).DpiScaleY) - 20); }
        finally { DeleteObject(region); }
    }
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint region);
    [DllImport("user32.dll")] static extern int GetWindowRgn(nint window, nint region);
}
