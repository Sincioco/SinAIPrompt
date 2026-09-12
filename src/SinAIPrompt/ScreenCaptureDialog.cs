using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// One invocation owns its picker, countdown, cancellation, and owner restoration.
// It returns a PNG in memory; the editor owns insertion and image storage choices.
internal sealed class ScreenCaptureDialog : Window
{
    readonly CaptureGallery gallery = new();
    readonly ComboBox delay = new() { ItemsSource = new[] { 0, 3, 5, 10 }, SelectedItem = 3, Width = 85 };
    readonly CheckBox region = new() { Content = "Select A Region", Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox cursor = new() { Content = "Include Cursor", Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Button capture = new() { Content = "Capture", IsDefault = true, MinWidth = 100, Padding = new Thickness(18, 8, 18, 8) };
    readonly TextBlock error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock selectedName = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource cancellation = new();
    bool capturing;

    ScreenCaptureDialog(Window owner)
    {
        Owner = owner; Title = "Screen Capture"; Width = owner.ActualWidth; Height = owner.ActualHeight;
        NameScope.SetNameScope(this, new NameScope());
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Opacity = 0;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14; ThemeMode = owner.ThemeMode;
        SetResourceReference(BackgroundProperty, "ShellBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(8, 0, 8, 16) };
        var cancel = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        cancel.Click += (_, _) => Close(); DockPanel.SetDock(cancel, Dock.Right); header.Children.Add(cancel);
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Screen Capture", FontSize = 26, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new TextBlock { Text = "Choose a monitor or an application below. Captures open in the image editor.", Margin = new Thickness(0, 5, 0, 0) });
        header.Children.Add(title); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 12) };
        options.Children.Add(new TextBlock { Text = "Delay (seconds)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        options.Children.Add(delay); options.Children.Add(region); options.Children.Add(cursor);
        var refresh = new Button { Content = "Refresh Window List", Margin = new Thickness(22, 0, 0, 0) };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = capture.IsEnabled = false;
            try { await gallery.RefreshAsync(new WindowInteropHelper(this).Handle); }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { refresh.IsEnabled = true; capture.IsEnabled = gallery.Selected != null; }
        };
        options.Children.Add(refresh); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options);
        var footer = new StackPanel { Margin = new Thickness(8, 12, 8, 0) };
        footer.Children.Add(error);
        var controls = new DockPanel(); DockPanel.SetDock(capture, Dock.Right); controls.Children.Add(capture); controls.Children.Add(selectedName);
        footer.Children.Add(controls);
        footer.Children.Add(new TextBlock { Text = "Window capture brings the chosen window forward. Keep it visible until capture finishes.", FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(gallery); Content = root;
        RegisterName("CaptureGallery", gallery); RegisterName("CaptureDelay", delay); RegisterName("CaptureRegion", region);
        RegisterName("CaptureCursor", cursor); RegisterName("CaptureNow", capture);
        System.Windows.Automation.AutomationProperties.SetName(delay, "Capture Delay In Seconds");
        gallery.SelectionChanged += target => { selectedName.Text = "Selected: " + target.Name; capture.IsEnabled = true; };
        capture.Click += async (_, _) => await CaptureAsync();
        Loaded += (_, _) =>
        {
            var surface = (FrameworkElement)owner.Content;
            var origin = surface.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(surface);
            ScreenCapture.Place(this, new Int32Rect((int)origin.X, (int)origin.Y, (int)Math.Round(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Round(surface.ActualHeight * dpi.DpiScaleY)));
            Opacity = 1;
        };
        Closed += (_, _) => { gallery.Dispose(); cancellation.Cancel(); if (!capturing) completion.TrySetResult(null); };
    }

    async Task CaptureAsync()
    {
        if (capturing || gallery.Selected is not ScreenCapture.Target selected) return;
        capturing = true;
        var originalState = Owner.WindowState;
        try
        {
            Hide();
            if (selected.Window != new WindowInteropHelper(Owner).Handle) Owner.WindowState = WindowState.Minimized;
            ScreenCapture.Activate(selected);
            await CountdownAsync((int)delay.SelectedItem, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var bounds = ScreenCapture.Bounds(selected);
            bool withCursor = cursor.IsChecked == true;
            var image = await Task.Run(() => ScreenCapture.Capture(bounds, withCursor), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (region.IsChecked == true) image = CaptureRegionWindow.Select(image, bounds);
            string? png = image == null ? null : await Task.Run(() => ScreenCapture.Png(image), cancellation.Token);
            completion.TrySetResult(png);
        }
        catch (OperationCanceledException) { completion.TrySetResult(null); }
        catch (Exception ex)
        {
            error.Text = "Capture could not finish. " + ex.Message;
            capturing = false; Show();
        }
        finally { Owner.WindowState = originalState; }
    }

    internal static async Task<string?> CaptureRegionAsync(Window owner)
    {
        var originalState = owner.WindowState;
        bool wasEnabled = owner.IsEnabled;
        try
        {
            // Keep the countdown nonmodal so the user can arrange any window,
            // including this editor, before freezing the desktop.
            await CountdownAsync(3, CancellationToken.None);
            owner.IsEnabled = false;
            var bounds = ScreenCapture.DesktopBounds;
            var image = await Task.Run(() => ScreenCapture.Capture(bounds, includeCursor: false));
            image = CaptureRegionWindow.Select(image, bounds, magnify: true);
            return image == null ? null : await Task.Run(() => ScreenCapture.Png(image));
        }
        catch (OperationCanceledException) { return null; }
        finally
        {
            owner.WindowState = originalState;
            owner.IsEnabled = wasEnabled; owner.Activate();
        }
    }

    static async Task CountdownAsync(int seconds, CancellationToken cancellation)
    {
        if (seconds == 0) { await Task.Delay(200, cancellation); return; }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var status = new TextBlock { FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
        var progress = new ProgressBar { Minimum = 0, Maximum = seconds, Height = 5, Margin = new Thickness(0, 0, 0, 12) };
        var cancel = new Button { Content = "Cancel Capture", Padding = new Thickness(10, 4, 10, 4) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(status); panel.Children.Add(progress); panel.Children.Add(cancel);
        var countdown = new Window { Title = "Capture Countdown", Content = panel, Width = 270,
            SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(30, 35, 42)),
            Left = SystemParameters.WorkArea.Right - 290, Top = SystemParameters.WorkArea.Top + 20 };
        cancel.Click += (_, _) => stop.Cancel();
        countdown.Closed += (_, _) => stop.Cancel();
        try
        {
            countdown.Show();
            for (int remaining = seconds; remaining > 0; remaining--)
            {
                status.Text = $"Capturing In {remaining}…"; progress.Value = seconds - remaining;
                await Task.Delay(1000, stop.Token);
            }
            countdown.Hide();
            await Task.Delay(150, stop.Token); // Let the compositor remove capture controls.
        }
        finally { countdown.Close(); }
    }

    internal static async Task<string?> CaptureAsync(Window owner)
    {
        var dialog = new ScreenCaptureDialog(owner);
        bool wasEnabled = owner.IsEnabled;
        owner.IsEnabled = false;
        try { await dialog.gallery.RefreshAsync(0); dialog.Show(); return await dialog.completion.Task; }
        finally
        {
            dialog.Close(); dialog.cancellation.Dispose();
            owner.IsEnabled = wasEnabled; owner.Activate();
        }
    }
}
