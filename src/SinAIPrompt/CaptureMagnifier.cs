using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// A small view of the frozen capture; it never samples its own overlay or cursor.
internal sealed class CaptureMagnifier
{
    readonly Canvas surface;
    readonly BitmapSource source;
    readonly Int32Rect desktop;
    readonly List<ScreenCapture.Target> monitors = ScreenCapture.Monitors();
    readonly Image preview = new() { Stretch = Stretch.Fill };
    readonly Border border;

    internal CaptureMagnifier(Canvas surface, BitmapSource source, Int32Rect desktop)
    {
        this.surface = surface; this.source = source; this.desktop = desktop;
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        var grid = new Grid(); grid.Children.Add(preview);
        grid.Children.Add(new Border { Width = 1, Background = Brushes.DeepSkyBlue });
        grid.Children.Add(new Border { Height = 1, Background = Brushes.DeepSkyBlue });
        border = new Border { Name = "CaptureMagnifier", Width = 156, Height = 156, BorderThickness = new Thickness(3),
            BorderBrush = Brushes.White, Background = Brushes.Black, Child = grid, IsHitTestVisible = false };
        surface.Children.Add(border);
    }

    internal void Update(Point pointer)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        double scaleX = source.PixelWidth / surface.ActualWidth, scaleY = source.PixelHeight / surface.ActualHeight;
        int x = Math.Clamp((int)(pointer.X * scaleX), 0, source.PixelWidth - 1);
        int y = Math.Clamp((int)(pointer.Y * scaleY), 0, source.PixelHeight - 1);
        int width = Math.Min(31, source.PixelWidth), height = Math.Min(31, source.PixelHeight);
        preview.Source = new CroppedBitmap(source, new Int32Rect(Math.Clamp(x - width / 2, 0, source.PixelWidth - width),
            Math.Clamp(y - height / 2, 0, source.PixelHeight - height), width, height));
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
