using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class ScreenCaptureSelfTest
{
    internal static async Task Run(Window owner, Action<bool, string> check)
    {
        var monitors = ScreenCapture.Monitors();
        check(monitors.Count > 0 && monitors.All(m => m.Bounds.Width > 0 && m.Bounds.Height > 0),
            "Screen Capture enumerates desktop monitors with physical pixel bounds");
        check(CaptureRegionWindow.PixelBounds(new Point(75, 60), new Point(25, 10), new Size(100, 80), 200, 160) == new Int32Rect(50, 20, 100, 100),
            "Region selection handles reverse drags and scaled display coordinates");
        check(ScreenCapture.Intersect(new(-1500, 20, 200, 100), new(-1920, 0, 3840, 1080)) == new Int32Rect(-1500, 20, 200, 100),
            "Capture geometry preserves negative monitor coordinates");
        var fixture = new Window { Title = "Screen Capture Test", Width = 320, Height = 220, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new Border { Background = Brushes.CornflowerBlue } };
        fixture.Show();
        try
        {
            await Task.Delay(150);
            nint handle = new WindowInteropHelper(fixture).Handle;
            var target = ScreenCapture.Windows(0).Single(t => t.Window == handle);
            var bounds = ScreenCapture.Bounds(target);
            var image = await Task.Run(() => ScreenCapture.Capture(bounds));
            var pixel = new byte[4];
            image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixel, 4, 0);
            check(pixel[0] == 237 && pixel[1] == 149 && pixel[2] == 100,
                "Windows capture returns the actual pixels of the selected application window");
            check(image.IsFrozen && image.PixelWidth == bounds.Width && image.PixelHeight == bounds.Height,
                "Capture preserves physical resolution and can cross the worker/UI boundary");

            var pending = ScreenCaptureDialog.CaptureAsync(owner);
            await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (pending.IsFaulted) await pending;
            var picker = Application.Current.Windows.OfType<ScreenCaptureDialog>().Single();
            check(!owner.IsEnabled && ((ComboBox)picker.FindName("CaptureTarget")).Items.Count == monitors.Count,
                "Capture picker offers every monitor and protects the insertion document");
            ((ComboBox)picker.FindName("CaptureKind")).SelectedIndex = 1;
            var targets = (ComboBox)picker.FindName("CaptureTarget");
            targets.SelectedItem = targets.Items.Cast<ScreenCapture.Target>().Single(t => t.Window == handle);
            var delays = (ComboBox)picker.FindName("CaptureDelay");
            var cursor = (CheckBox)picker.FindName("CaptureCursor");
            check(cursor.IsChecked != true, "Screen Capture excludes the pointer by default and offers Include Cursor");
            cursor.IsChecked = true;
            check(delays.Items.Cast<int>().SequenceEqual(new[] { 0, 3, 5, 10 }),
                "Capture supports immediate capture and 3, 5 and 10 second delays");
            delays.SelectedItem = 3;
            ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string? png = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            using var stream = new MemoryStream(Convert.FromBase64String(png!["data:image/png;base64,".Length..]));
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            check(owner.IsEnabled && owner.WindowState != WindowState.Minimized && decoded.PixelWidth == bounds.Width,
                "Capture workflow returns a PNG and restores the editor after hiding capture controls");

            pending = ScreenCaptureDialog.CaptureAsync(owner);
            await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            picker = Application.Current.Windows.OfType<ScreenCaptureDialog>().Single();
            picker.Close();
            check(await pending == null && owner.IsEnabled, "Canceling the capture picker leaves the editor available");

            pending = ScreenCaptureDialog.CaptureAsync(owner);
            await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            picker = Application.Current.Windows.OfType<ScreenCaptureDialog>().Single();
            ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var countdown = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Capture Countdown");
            ((StackPanel)countdown.Content).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(await pending.WaitAsync(TimeSpan.FromSeconds(5)) == null && owner.IsEnabled && owner.WindowState != WindowState.Minimized,
                "Canceling the asynchronous countdown restores the editor without capturing");

            pending = ScreenCaptureDialog.CaptureAsync(owner);
            await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            picker = Application.Current.Windows.OfType<ScreenCaptureDialog>().Single();
            ((ComboBox)picker.FindName("CaptureKind")).SelectedIndex = 1;
            targets = (ComboBox)picker.FindName("CaptureTarget");
            targets.SelectedItem = targets.Items.Cast<ScreenCapture.Target>().Single(t => t.Window == handle);
            ((ComboBox)picker.FindName("CaptureDelay")).SelectedItem = 0;
            ((CheckBox)picker.FindName("CaptureRegion")).IsChecked = true;
            bool regionShown = false;
            var closeRegion = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            closeRegion.Tick += (_, _) =>
            {
                var overlay = Application.Current.Windows.OfType<CaptureRegionWindow>().SingleOrDefault();
                if (overlay?.IsLoaded != true) return;
                regionShown = overlay.IsVisible && overlay.Opacity == 1;
                closeRegion.Stop();
                var instruction = (StackPanel)((Grid)overlay.Content).Children.OfType<Border>().Single().Child;
                instruction.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            try
            {
                closeRegion.Start();
                ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(await pending.WaitAsync(TimeSpan.FromSeconds(10)) == null && regionShown && owner.IsEnabled && owner.WindowState != WindowState.Minimized,
                    "Region capture displays the frozen image and Cancel restores the editor without inserting");
            }
            finally { closeRegion.Stop(); }
        }
        finally { fixture.Close(); }
    }
}
