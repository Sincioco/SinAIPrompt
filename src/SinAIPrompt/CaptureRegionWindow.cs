using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SinAIPrompt;

// A frozen capture is the selection surface: selection borders, instructions,
// and the selection cursor never become part of the resulting image.
internal sealed class CaptureRegionWindow : Window
{
    readonly BitmapSource image;
    readonly Canvas surface = new() { Background = Brushes.Transparent };
    readonly Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)), IsHitTestVisible = false };
    readonly Rectangle outline = new() { Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    Point? start;
    bool sticky;
    readonly CapturePointerSpeed pointerSpeed = new();
    BitmapSource? result;

    CaptureRegionWindow(BitmapSource image, Int32Rect bounds, bool magnify)
    {
        this.image = image;
        Title = "Select Capture Region";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false; Topmost = true; Opacity = 0;
        Background = Brushes.Black; Cursor = Cursors.Cross;
        var root = new Grid();
        root.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        surface.Children.Add(shade); surface.Children.Add(outline); root.Children.Add(surface);
        CaptureMagnifier? magnifier = magnify ? new(surface, image, bounds) : null;
        if (magnify) { shade.Visibility = Visibility.Collapsed; outline.Stroke = Brushes.DeepSkyBlue; }
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(14, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        var instruction = new StackPanel { Orientation = Orientation.Horizontal };
        instruction.Children.Add(new TextBlock { Text = "Drag Or Click Twice To Select · Wheel / + / − Adjusts Initial Mouse Speed · Esc Cancels", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        instruction.Children.Add(cancel);
        root.Children.Add(new Border { Child = instruction, Background = new SolidColorBrush(Color.FromRgb(30, 35, 42)),
            Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(12), CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
        Content = root;
        void UpdateMagnifier() => magnifier?.Update(Clamp(Mouse.GetPosition(surface)), pointerSpeed.Current, start != null);
        Loaded += (_, _) => { ScreenCapture.Place(this, bounds); Opacity = 1; Activate(); UpdateMagnifier(); };
        Closed += (_, _) => pointerSpeed.Dispose();
        Deactivated += (_, _) => { pointerSpeed.Restore(); UpdateMagnifier(); };
        surface.SizeChanged += (_, _) => Draw(Rect.Empty);
        surface.MouseLeftButtonDown += (_, e) => { BeginSelection(Clamp(e.GetPosition(surface))); UpdateMagnifier(); e.Handled = true; };
        surface.MouseMove += (_, e) =>
        {
            var point = Clamp(e.GetPosition(surface));
            magnifier?.Update(point, pointerSpeed.Current, start != null);
            if (start is Point first) Draw(new Rect(first, point));
        };
        surface.MouseLeftButtonUp += (_, e) =>
        {
            EndSelection(Clamp(e.GetPosition(surface))); e.Handled = true;
        };
        MouseWheel += (_, e) => { if (start == null) { pointerSpeed.Adjust(Math.Sign(e.Delta)); UpdateMagnifier(); } e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (start == null && e.Key is Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus)
            { pointerSpeed.Adjust(e.Key is Key.Add or Key.OemPlus ? 1 : -1); UpdateMagnifier(); e.Handled = true; }
        };
    }

    internal bool IsSticky => sticky;
    internal void BeginSelection(Point point)
    {
        pointerSpeed.Restore();
        if (sticky && start is Point first) { CompleteSelection(first, point); return; }
        start = point; surface.CaptureMouse(); Draw(new Rect(point, point));
    }
    internal void EndSelection(Point point)
    {
        surface.ReleaseMouseCapture();
        if (sticky || start is not Point first) return;
        if (Math.Abs(point.X - first.X) <= 6 && Math.Abs(point.Y - first.Y) <= 6) { sticky = true; return; }
        CompleteSelection(first, point);
        if (result == null) sticky = true;
    }

    Point Clamp(Point point) => new(Math.Clamp(point.X, 0, surface.ActualWidth), Math.Clamp(point.Y, 0, surface.ActualHeight));
    internal void CompleteSelection(Point first, Point last)
    {
        var pixels = PixelBounds(first, last, surface.RenderSize, image.PixelWidth, image.PixelHeight);
        if (pixels.Width < 2 || pixels.Height < 2) { Draw(Rect.Empty); return; }
        result = new CroppedBitmap(image, pixels); result.Freeze(); Close();
    }
    void Draw(Rect selection)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(surface.RenderSize)));
        if (!selection.IsEmpty) geometry.Children.Add(new RectangleGeometry(selection));
        shade.Data = geometry;
        outline.Visibility = selection.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (selection.IsEmpty) return;
        Canvas.SetLeft(outline, selection.Left); Canvas.SetTop(outline, selection.Top);
        outline.Width = selection.Width; outline.Height = selection.Height;
    }

    internal static Int32Rect PixelBounds(Point first, Point last, Size display, int width, int height)
    {
        var area = new Rect(first, last);
        int x = Math.Clamp((int)Math.Floor(area.Left * width / display.Width), 0, width);
        int y = Math.Clamp((int)Math.Floor(area.Top * height / display.Height), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(area.Right * width / display.Width), x, width);
        int bottom = Math.Clamp((int)Math.Ceiling(area.Bottom * height / display.Height), y, height);
        return new(x, y, right - x, bottom - y);
    }

    internal static BitmapSource? Select(BitmapSource image, Int32Rect bounds, bool magnify = false)
    {
        var window = new CaptureRegionWindow(image, bounds, magnify);
        window.ShowDialog();
        return window.result;
    }
}
