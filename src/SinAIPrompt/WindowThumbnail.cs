using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace SinAIPrompt;

// A DWM relationship renders a window preview without activating that window,
// copying its pixels, or sending blocking messages to another application.
internal sealed class WindowThumbnail : Border, IDisposable
{
    readonly nint source;
    nint thumbnail;
    Window? destination;
    NativeRect lastDestination, lastSource;
    bool lastVisible;

    internal WindowThumbnail(nint source)
    {
        this.source = source;
        Height = 105; Background = new SolidColorBrush(Color.FromRgb(235, 239, 244));
        Child = new TextBlock { Text = "Preview unavailable", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray, FontSize = 11 };
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            destination = Window.GetWindow(this);
            if (thumbnail == 0 && destination != null && DwmRegisterThumbnail(new WindowInteropHelper(destination).Handle, source, out thumbnail) == 0)
                Child.Visibility = Visibility.Hidden;
            Update();
        };
        LayoutUpdated += (_, _) => Update();
        Unloaded += (_, _) => Dispose();
    }

    void Update()
    {
        if (thumbnail == 0 || destination == null || !IsLoaded) return;
        if (DwmQueryThumbnailSourceSize(thumbnail, out var size) != 0 || size.Width <= 0 || size.Height <= 0) return;
        double scale = Math.Min(ActualWidth / size.Width, ActualHeight / size.Height);
        var point = TransformToAncestor(destination).Transform(new Point((ActualWidth - size.Width * scale) / 2, (ActualHeight - size.Height * scale) / 2));
        var full = new Rect(point, new Size(size.Width * scale, size.Height * scale));
        var visible = full;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent != null && parent != destination; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollContentPresenter clip)
                visible.Intersect(new Rect(clip.TransformToAncestor(destination).Transform(new Point()), clip.RenderSize));
        bool show = IsVisible && !visible.IsEmpty && visible.Width >= 1 && visible.Height >= 1;
        var dpi = VisualTreeHelper.GetDpi(destination);
        var target = show ? NativeRect.From(visible, dpi.DpiScaleX, dpi.DpiScaleY) : default;
        var crop = show ? NativeRect.From(new Rect((visible.X - full.X) / scale, (visible.Y - full.Y) / scale, visible.Width / scale, visible.Height / scale), 1, 1) : default;
        if (target.Equals(lastDestination) && crop.Equals(lastSource) && show == lastVisible) return;
        var properties = new ThumbnailProperties { Flags = 31, Destination = target, Source = crop, Opacity = 255, Visible = show ? 1 : 0 };
        DwmUpdateThumbnailProperties(thumbnail, ref properties);
        lastDestination = target; lastSource = crop; lastVisible = show;
    }

    public void Dispose()
    {
        if (thumbnail != 0) DwmUnregisterThumbnail(thumbnail);
        thumbnail = 0; destination = null; lastVisible = false;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        internal static NativeRect From(Rect value, double sx, double sy) => new()
        { Left = (int)Math.Round(value.Left * sx), Top = (int)Math.Round(value.Top * sy), Right = (int)Math.Round(value.Right * sx), Bottom = (int)Math.Round(value.Bottom * sy) };
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination, Source;
        public byte Opacity;
        public int Visible, ClientOnly;
    }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(nint thumbnail);
}
