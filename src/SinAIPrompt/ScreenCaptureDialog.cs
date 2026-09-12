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
    readonly ComboBox kind = new() { ItemsSource = new[] { "Desktop Monitor", "Application Window" }, SelectedIndex = 0 };
    readonly ComboBox target = new();
    readonly ComboBox delay = new() { ItemsSource = new[] { 0, 3, 5, 10 }, SelectedItem = 3 };
    readonly CheckBox region = new() { Content = "Select A Region", Margin = new Thickness(0, 14, 0, 0) };
    readonly CheckBox cursor = new() { Content = "Include Cursor", Margin = new Thickness(0, 10, 0, 0) };
    readonly Button capture = new() { Content = "Capture", IsDefault = true, MinWidth = 95, Padding = new Thickness(14, 6, 14, 6) };
    readonly TextBlock error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource cancellation = new();
    bool capturing;

    ScreenCaptureDialog(Window owner)
    {
        Owner = owner; Title = "Screen Capture"; Width = 580;
        NameScope.SetNameScope(this, new NameScope());
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14; ThemeMode = owner.ThemeMode;
        SetResourceReference(BackgroundProperty, "ShellBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(24) };
        void Field(string name, string caption, FrameworkElement input)
        {
            panel.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 10, 0, 5) });
            RegisterName(name, input); System.Windows.Automation.AutomationProperties.SetName(input, caption);
            panel.Children.Add(input);
        }
        Field("CaptureKind", "Capture", kind); Field("CaptureTarget", "Choose A Monitor Or Window", target);
        Field("CaptureDelay", "Delay (Seconds)", delay);
        RegisterName("CaptureRegion", region); panel.Children.Add(region);
        RegisterName("CaptureCursor", cursor); panel.Children.Add(cursor);
        panel.Children.Add(new TextBlock { Text = "The editor moves out of the way during capture. Window capture brings the selected window forward; keep it visible until capture finishes. Captures open in the image editor before insertion.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontSize = 12 });
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var refresh = new Button { Content = "Refresh List", Margin = new Thickness(0, 0, 12, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) => RefreshTargets(); cancel.Click += (_, _) => Close();
        capture.Click += async (_, _) => await CaptureAsync();
        RegisterName("CaptureNow", capture);
        buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(capture); panel.Children.Add(buttons);
        Content = panel;
        kind.SelectionChanged += (_, _) => RefreshTargets();
        Loaded += (_, _) => RefreshTargets();
        Closed += (_, _) => { cancellation.Cancel(); if (!capturing) completion.TrySetResult(null); };
    }

    void RefreshTargets()
    {
        var previous = target.SelectedItem as ScreenCapture.Target;
        var targets = kind.SelectedIndex == 0 ? ScreenCapture.Monitors() : ScreenCapture.Windows(new WindowInteropHelper(this).Handle);
        target.ItemsSource = targets;
        target.SelectedItem = targets.FirstOrDefault(item => item.Name == previous?.Name) ?? targets.FirstOrDefault();
        capture.IsEnabled = targets.Count > 0;
        error.Text = targets.Count == 0 ? "No capture targets are available. Refresh the list to try again." : "";
    }

    async Task CaptureAsync()
    {
        if (capturing || target.SelectedItem is not ScreenCapture.Target selected) return;
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
        try { dialog.Show(); return await dialog.completion.Task; }
        finally
        {
            dialog.Close(); dialog.cancellation.Dispose();
            owner.IsEnabled = wasEnabled; owner.Activate();
        }
    }
}
