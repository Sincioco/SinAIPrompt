using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace SinAIPrompt;

// Keep one hardware-accelerated browser. Its native window leaves a cutout for
// WPF navigation beneath the ribbon; no extra browser or composition SDK is needed.
public sealed class RibbonWebView : WebView2
{
    double inset, ribbon;
    Rect[] popups = [];
    internal bool IsModal { get; private set; }
    (int Width, int Height, int Left, int Top, double Zoom)? previous;
    public RibbonWebView()
    {
        SizeChanged += (_, _) => UpdateRegion();
        Loaded += (_, _) => { previous = null; UpdateRegion(); };
    }
    internal void SetChrome(double left, double top)
    {
        inset = left; ribbon = top; UpdateRegion();
    }
    internal void SetPopups(Rect[] bounds, bool modal)
    {
        popups = bounds; IsModal = modal; previous = null; UpdateRegion();
    }
    void UpdateRegion()
    {
        if (Handle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = ((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            (int)Math.Round(inset * dpi.DpiScaleX), (int)Math.Round(ribbon * dpi.DpiScaleY), ZoomFactor);
        if (previous == bounds) return;
        previous = bounds;
        if (bounds.Item3 == 0) { SetWindowRgn(Handle, IntPtr.Zero, true); return; }
        nint region = CreateRectRgn(0, 0, bounds.Item1, bounds.Item4);
        try
        {
            Union(region, bounds.Item3, bounds.Item4, bounds.Item1, bounds.Item2);
            // Only the popup's actual rectangle may cover native navigation.
            // Restoring the entire browser window would paint the sidebar blank.
            foreach (var popup in popups)
                Union(region, (int)Math.Floor(popup.Left * dpi.DpiScaleX * ZoomFactor), (int)Math.Floor(popup.Top * dpi.DpiScaleY * ZoomFactor),
                    (int)Math.Ceiling(popup.Right * dpi.DpiScaleX * ZoomFactor), (int)Math.Ceiling(popup.Bottom * dpi.DpiScaleY * ZoomFactor));
            if (SetWindowRgn(Handle, region, true) != 0) region = 0; // Windows takes ownership.
        }
        finally { if (region != 0) DeleteObject(region); }
    }
    static void Union(nint region, int left, int top, int right, int bottom)
    {
        nint part = CreateRectRgn(left, top, right, bottom);
        try { CombineRgn(region, region, part, 2); }
        finally { DeleteObject(part); }
    }
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint window, nint region, bool redraw);
}
