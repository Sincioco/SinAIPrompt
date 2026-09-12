using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
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
        check(CaptureMagnifier.OppositeCorner(new(20, 30), new(0, 0, 1000, 800), new(156, 156)) == new Point(828, 628) &&
            CaptureMagnifier.OppositeCorner(new(950, 750), new(0, 0, 1000, 800), new(156, 156)) == new Point(16, 16),
            "The capture magnifier moves to the opposite horizontal and vertical corner");
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
            var picker = await Picker(pending);
            check(!owner.IsEnabled && Controls(picker).OfType<Button>().Count(b => b.Tag is ScreenCapture.Target t && t.Window == 0) == monitors.Count,
                "Capture gallery offers a preview card for every monitor and protects the insertion document");
            var ownerSurface = (FrameworkElement)owner.Content;
            check(Math.Abs(picker.ActualWidth - ownerSurface.ActualWidth) < 2 && Math.Abs(picker.ActualHeight - ownerSurface.ActualHeight) < 2,
                "Capture picker fills the application content area above menus, documents and status");
            await Task.Delay(150);
            File.WriteAllBytes(System.IO.Path.Combine(App.Current.Store.DirectoryPath, "capture-monitors.png"),
                Convert.FromBase64String(ScreenCapture.Png(ScreenCapture.Capture(ScreenCapture.Bounds(new("", new WindowInteropHelper(picker).Handle, default)))).Split(',')[1]));
            var tile = TargetButton(picker, handle); tile.BringIntoView(); picker.UpdateLayout();
            await Task.Delay(200);
            var preview = Controls(tile).OfType<WindowThumbnail>().Single();
            var center = preview.PointToScreen(new Point(preview.ActualWidth / 2, preview.ActualHeight / 2));
            var thumbnailPixels = ScreenCapture.Capture(new Int32Rect((int)center.X, (int)center.Y, 1, 1));
            thumbnailPixels.CopyPixels(pixel, 4, 0);
            check(pixel[0] == 237 && pixel[1] == 149 && pixel[2] == 100,
                "Application thumbnail shows the actual window contents without activating it");
            File.WriteAllBytes(System.IO.Path.Combine(App.Current.Store.DirectoryPath, "capture-gallery.png"),
                Convert.FromBase64String(ScreenCapture.Png(ScreenCapture.Capture(ScreenCapture.Bounds(new("", new WindowInteropHelper(picker).Handle, default)))).Split(',')[1]));
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            picker = await Picker(pending);
            picker.Close();
            check(await pending == null && owner.IsEnabled, "Canceling the capture picker leaves the editor available");

            pending = ScreenCaptureDialog.CaptureAsync(owner);
            picker = await Picker(pending);
            ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var countdown = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Capture Countdown");
            ((StackPanel)countdown.Content).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(await pending.WaitAsync(TimeSpan.FromSeconds(5)) == null && owner.IsEnabled && owner.WindowState != WindowState.Minimized,
                "Canceling the asynchronous countdown restores the editor without capturing");

            pending = ScreenCaptureDialog.CaptureAsync(owner);
            picker = await Picker(pending);
            TargetButton(picker, handle).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
                using var speed = new CapturePointerSpeed(); int originalSpeed = speed.Current;
                overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, originalSpeed == 20 ? -120 : 120) { RoutedEvent = Mouse.MouseWheelEvent });
                var instruction = (StackPanel)((Grid)overlay.Content).Children.OfType<Border>().Single().Child;
                instruction.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                using var restored = new CapturePointerSpeed();
                check(restored.Current == originalSpeed, "Canceling before the first capture point restores temporary pointer speed");
            };
            try
            {
                closeRegion.Start();
                ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(await pending.WaitAsync(TimeSpan.FromSeconds(10)) == null && regionShown && owner.IsEnabled && owner.WindowState != WindowState.Minimized,
                    "Region capture displays the frozen image and Cancel restores the editor without inserting");
            }
            finally { closeRegion.Stop(); }
            if (owner is MainWindow main)
            {
                var view = main.CurrentView!;
                async Task WaitFor(string expression)
                {
                    for (int i = 0; i < 150; i++)
                    {
                        if (await view.Browser.ExecuteScriptAsync(expression) == "true") return;
                        await Task.Delay(20);
                    }
                    throw new TimeoutException("Image-editor capture did not finish: " + expression);
                }
                await view.Browser.ExecuteScriptAsync("window.editor.openAnnotation()");
                await WaitFor("!!document.querySelector('dialog.annotation [data-action=screenCapture]')");
                await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=screenCapture]').click()");
                picker = await Picker(Task.FromResult<string?>(null));
                TargetButton(picker, handle).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((ComboBox)picker.FindName("CaptureDelay")).SelectedItem = 0;
                ((Button)picker.FindName("CaptureNow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor("document.querySelectorAll('#canvas image').length===1 && document.querySelectorAll('#canvas [data-crop]').length===8");
                check(main.IsAnnotating && !main.Shell.IsEnabled, "Screen Capture inside Image Editor returns a selected image with crop handles and keeps the annotation session open");
                using (var capture = File.Create(Path.Combine(App.Current.Store.DirectoryPath, "capture-in-editor.png")))
                    await view.Browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture);
                await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=screenCapture]').click()");
                picker = await Picker(Task.FromResult<string?>(null)); picker.Close();
                await WaitFor("!document.querySelector('dialog.annotation [data-action=screenCapture]').disabled");
                check(await view.Browser.ExecuteScriptAsync("document.querySelectorAll('#canvas image').length===1") == "true", "Canceling a capture from Image Editor preserves its existing layers");
                await RegionInAnnotation(main, WaitFor, check);
                await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=cancel]').click()");
                for (int i = 0; i < 100 && main.IsAnnotating; i++) await Task.Delay(20);
            }
        }
        finally { fixture.Close(); }
    }

    static async Task RegionInAnnotation(MainWindow owner, Func<string, Task> wait, Action<bool, string> check)
    {
        var view = owner.CurrentView!;
        await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=regionCapture]').click()");
        for (int i = 0; i < 100 && !Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Region Capture"); i++) await Task.Delay(20);
        var options = Application.Current.Windows.Cast<Window>().Single(window => window.Title == "Region Capture");
        Controls(options).OfType<ComboBox>().Single().SelectedItem = 0;
        var select = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        select.Tick += (_, _) =>
        {
            var region = Application.Current.Windows.OfType<CaptureRegionWindow>().SingleOrDefault();
            if (region?.IsLoaded != true) return;
            select.Stop(); region.CompleteSelection(new(25, 25), new(125, 85));
        };
        try
        {
            select.Start(); options.DialogResult = true;
            await wait("document.querySelectorAll('#canvas image').length===2 && !document.querySelector('dialog.annotation [data-action=regionCapture]').disabled");
            check(owner.IsAnnotating && !owner.Shell.IsEnabled && owner.IsEnabled && await view.Browser.ExecuteScriptAsync("[...document.querySelectorAll('#canvas image')].some(image=>image.getAttribute('width')==='100'&&image.getAttribute('height')==='60')") == "true",
                "Region Capture inside Image Editor adds a lossless cropped layer while retaining existing artwork and annotation mode");
        }
        finally { select.Stop(); }
        await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=regionCapture]').click()");
        for (int i = 0; i < 100 && !Application.Current.Windows.Cast<Window>().Any(window => window.Title == "Region Capture"); i++) await Task.Delay(20);
        Application.Current.Windows.Cast<Window>().Single(window => window.Title == "Region Capture").Close();
        await wait("!document.querySelector('dialog.annotation [data-action=regionCapture]').disabled");
        check(await view.Browser.ExecuteScriptAsync("document.querySelectorAll('#canvas image').length===2") == "true", "Canceling annotation Region Capture preserves both layers");
    }

    internal static async Task RegionShortcut(MainWindow owner, Action<bool, string> check)
    {
        var view = owner.CurrentView!;
        string original = owner.ActiveDocument!.Text;
        bool inspected = false; Exception? failure = null;
        var select = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        select.Tick += (_, _) =>
        {
            var overlay = Application.Current.Windows.OfType<CaptureRegionWindow>().SingleOrDefault();
            if (overlay?.IsLoaded != true || overlay.Opacity != 1) return;
            select.Stop();
            try
            {
                var canvas = ((Grid)overlay.Content).Children.OfType<Canvas>().Single();
                check(canvas.Children.OfType<System.Windows.Shapes.Path>().Single().Visibility == Visibility.Collapsed &&
                    canvas.Children.OfType<Border>().Single().Name == "CaptureMagnifier", "Region shortcut shows its magnifier without darkening the frozen screen");
                var image = ((Grid)overlay.Content).Children.OfType<Image>().Single();
                check(((BitmapSource)image.Source).PixelWidth == ScreenCapture.DesktopBounds.Width, "Region shortcut covers the entire virtual desktop");
                var magnifier = canvas.Children.OfType<Border>().Single();
                check(magnifier.Width >= 300 && Controls(magnifier).OfType<Border>().Count(b => b.Opacity == .8) == 2,
                    "Region capture provides a large pixel-grid magnifier with 80-percent-opacity crosshairs");
                SavePreview(magnifier, "region-magnifier.png");
                using var speed = new CapturePointerSpeed(); int normal = speed.Current;
                int direction = normal == 20 ? -120 : 120;
                overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, direction) { RoutedEvent = Mouse.MouseWheelEvent });
                using var adjusted = new CapturePointerSpeed();
                check(adjusted.Current == normal + Math.Sign(direction), "Mousewheel temporarily adjusts Windows pointer speed before the first capture point");
                overlay.BeginSelection(new(25, 25));
                using var restored = new CapturePointerSpeed();
                check(restored.Current == normal, "The first capture click restores the original Windows pointer speed");
                overlay.EndSelection(new(27, 27));
                check(overlay.IsVisible && overlay.IsSticky, "Releasing near the first point leaves a sticky selection open");
                inspected = true; overlay.BeginSelection(new(125, 85));
                check(!overlay.IsVisible, "A second click completes the sticky region selection");
            }
            catch (Exception ex) { failure = ex; overlay.Close(); }
        };
        try
        {
            await view.Browser.ExecuteScriptAsync("window.editor.setImageStorage('inline');window.regionShortcut=document.querySelector('#regionCapture').onclick().then(()=>true)");
            for (int i = 0; i < 100 && !Application.Current.Windows.Cast<Window>().Any(w => w.Title == "Region Capture"); i++) await Task.Delay(20);
            var options = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Region Capture");
            var optionsPanel = (StackPanel)options.Content;
            check(optionsPanel.Children.OfType<RadioButton>().First().IsChecked == true && (int)optionsPanel.Children.OfType<ComboBox>().Single().SelectedItem == 3,
                "Region Capture native dialog defaults to the document folder and a three-second delay");
            options.DialogResult = true;
            for (int i = 0; i < 100 && !Application.Current.Windows.Cast<Window>().Any(w => w.Title == "Capture Countdown"); i++) await Task.Delay(20);
            check(Application.Current.Windows.Cast<Window>().Any(w => w.Title == "Capture Countdown"), "Region Capture options start the existing cancellable countdown");
            var countdown = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Capture Countdown");
            check(countdown.AllowsTransparency && ((SolidColorBrush)countdown.Background).Color.A == 204 && Controls(countdown).OfType<TextBlock>().Any(text => text.FontSize >= 150),
                "Region countdown shows a large number over an 80-percent-opacity background");
            SavePreview((FrameworkElement)countdown.Content, "region-countdown.png");
            select.Start();
            for (int i = 0; i < 500; i++)
            {
                if (await view.Browser.ExecuteScriptAsync("!document.querySelector('#regionCapture').disabled") == "true") break;
                await Task.Delay(20);
            }
            if (failure != null) throw failure;
            check(inspected && owner.IsEnabled && owner.WindowState != WindowState.Minimized && await view.Browser.ExecuteScriptAsync(
                "!document.querySelector('dialog[open]') && document.querySelector('#document').contentDocument.images[0]?.dataset.sinStorage==='separate' && document.querySelector('#document').contentDocument.images[0]?.naturalWidth>0") == "true",
                "Region capture inserts a visible separate PNG directly, overriding embed preference without annotation or storage prompts");
        }
        finally
        {
            select.Stop();
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", System.Text.Json.JsonSerializer.Serialize(new
            {
                expression = "window.editor.setImageStorage('');window.editor.load(" + System.Text.Json.JsonSerializer.Serialize(original) + ")", awaitPromise = true
            }));
            view.AcceptHtml(original);
        }
    }

    static void SavePreview(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        File.WriteAllBytes(Path.Combine(App.Current.Store.DirectoryPath, name), Convert.FromBase64String(ScreenCapture.Png(bitmap).Split(',')[1]));
    }

    static async Task<ScreenCaptureDialog> Picker(Task<string?> pending)
    {
        for (int i = 0; i < 250; i++)
        {
            if (pending.IsFaulted) await pending;
            var picker = Application.Current.Windows.OfType<ScreenCaptureDialog>().SingleOrDefault();
            if (picker?.IsLoaded == true && picker.IsVisible && picker.Opacity == 1) return picker;
            await Task.Delay(20);
        }
        throw new TimeoutException("Capture gallery did not open.");
    }
    internal static IEnumerable<DependencyObject> Controls(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Controls(child)) yield return descendant;
    }
    static Button TargetButton(Window picker, nint handle) => Controls(picker).OfType<Button>().Single(b => b.Tag is ScreenCapture.Target t && t.Window == handle);
}
