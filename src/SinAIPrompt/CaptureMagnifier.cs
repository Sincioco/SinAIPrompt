using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// A pixel-grid view of the frozen capture; it never samples its own overlay or cursor.
internal sealed class CaptureMagnifier
{
    readonly Canvas surface;
    readonly BitmapSource source;
    readonly Int32Rect desktop;
    readonly List<ScreenCapture.Target> monitors = ScreenCapture.Monitors();
    readonly Image preview = new() { Stretch = Stretch.Fill };
    readonly Border border;
    readonly Border vertical = new() { Width = 1, Background = Brushes.DeepSkyBlue, Opacity = .8, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border horizontal = new() { Height = 1, Background = Brushes.DeepSkyBlue, Opacity = .8, VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock speed = new() { Foreground = Brushes.White, FontSize = 13, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };

    internal CaptureMagnifier(Canvas surface, BitmapSource source, Int32Rect desktop)
    {
        this.surface = surface; this.source = source; this.desktop = desktop;
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        var grid = new Grid { Width = 310, Height = 310 }; grid.Children.Add(preview);
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(10, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 10)));
        var paper = new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(85, 25, 50, 70)), 1), lines))
        { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 10, 10), Stretch = Stretch.None };
        paper.Freeze(); grid.Children.Add(new Border { Background = paper });
        grid.Children.Add(vertical); grid.Children.Add(horizontal);
        var panel = new StackPanel(); panel.Children.Add(grid); panel.Children.Add(speed);
        border = new Border { Name = "CaptureMagnifier", Width = 326, Height = 360, Padding = new Thickness(5), BorderThickness = new Thickness(3),
            BorderBrush = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(30, 35, 42)), Child = panel, IsHitTestVisible = false };
        surface.Children.Add(border);
    }

    internal void Update(Point pointer, int mouseSpeed = 0, bool selecting = false)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        double scaleX = source.PixelWidth / surface.ActualWidth, scaleY = source.PixelHeight / surface.ActualHeight;
        int x = Math.Clamp((int)(pointer.X * scaleX), 0, source.PixelWidth - 1);
        int y = Math.Clamp((int)(pointer.Y * scaleY), 0, source.PixelHeight - 1);
        int width = Math.Min(31, source.PixelWidth), height = Math.Min(31, source.PixelHeight);
        // At desktop edges keep the crosshair on the actual pointer pixel within the sample.
        var crop = new Int32Rect(Math.Clamp(x - width / 2, 0, source.PixelWidth - width), Math.Clamp(y - height / 2, 0, source.PixelHeight - height), width, height);
        preview.Source = new CroppedBitmap(source, crop);
        vertical.Margin = new Thickness((x - crop.X + .5) * 310 / width, 0, 0, 0);
        horizontal.Margin = new Thickness(0, (y - crop.Y + .5) * 310 / height, 0, 0);
        speed.Text = $"Mouse speed: {(mouseSpeed > 0 ? mouseSpeed + "/20" : "unavailable")} · {(selecting ? "Restored" : "Wheel or +/−")}";
        var monitor = monitors.FirstOrDefault(m => new Rect(m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height)
            .Contains(x + desktop.X, y + desktop.Y))?.Bounds ?? desktop;
        var area = new Rect((monitor.X - desktop.X) / scaleX, (monitor.Y - desktop.Y) / scaleY, monitor.Width / scaleX, monitor.Height / scaleY);
        var corner = OppositeCorner(pointer, area, new Size(border.Width, border.Height));
        Canvas.SetLeft(border, corner.X); Canvas.SetTop(border, corner.Y);
    }

    internal static Point OppositeCorner(Point pointer, Rect monitor, Size size) => new(
        pointer.X < monitor.Left + monitor.Width / 2 ? monitor.Right - size.Width - 16 : monitor.Left + 16,
        pointer.Y < monitor.Top + monitor.Height / 2 ? monitor.Bottom - size.Height - 16 : monitor.Top + 16);
}
