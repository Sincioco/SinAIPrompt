using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Windows capture primitives use physical pixels, including negative monitor
// coordinates. Capture UI and its lifetime belong to ScreenCaptureDialog.
internal static class ScreenCapture
{
    internal sealed record Target(string Name, nint Window, Int32Rect Bounds)
    {
        public override string ToString() => Name;
    }

    internal static List<Target> Monitors()
    {
        var targets = new List<Target>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rectangle, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
                targets.Add(new($"{info.Device.Replace(@"\\.\", "")} — {rectangle.Width} × {rectangle.Height}" +
                    ((info.Flags & 1) != 0 ? " (Primary)" : ""), 0, rectangle.Pixels));
            return true;
        }, 0);
        return targets;
    }

    internal static List<Target> Windows(nint exclude)
    {
        var targets = new List<Target>();
        EnumWindows((window, data) =>
        {
            if (window == exclude || !IsWindowVisible(window) || GetWindowTextLength(window) == 0) return true;
            DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int));
            if (cloaked != 0 || window == GetShellWindow()) return true;
            var title = new StringBuilder(GetWindowTextLength(window) + 1);
            GetWindowText(window, title, title.Capacity);
            targets.Add(new(title.ToString(), window, default));
            return true;
        }, 0);
        return targets.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static void Activate(Target target)
    {
        if (target.Window == 0) return;
        if (!IsWindow(target.Window)) throw new InvalidOperationException("That window has closed. Choose another window.");
        if (IsIconic(target.Window)) ShowWindowAsync(target.Window, 9); // SW_RESTORE
        SetForegroundWindow(target.Window);
    }

    internal static Int32Rect Bounds(Target target)
    {
        if (target.Window == 0) return target.Bounds;
        if (!IsWindow(target.Window) || IsIconic(target.Window))
            throw new InvalidOperationException("Keep the selected window open and visible until capture finishes.");
        if (DwmGetWindowAttribute(target.Window, 9, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(target.Window, out rect)) throw new Win32Exception();
        // A desktop capture contains the visible portion when a window extends
        // beyond the virtual desktop; it never invents off-screen pixels.
        var desktop = new Int32Rect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        return Intersect(rect.Pixels, desktop);
    }

    internal static Int32Rect Intersect(Int32Rect a, Int32Rect b)
    {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x),
            Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    internal static BitmapSource Capture(Int32Rect bounds, bool includeCursor = false)
    {
        if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("The capture area is not visible.");
        nint screen = GetDC(0), memory = 0, bitmap = 0, previous = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
            if (screen == 0 || memory == 0 || bitmap == 0) throw new Win32Exception();
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020))
                throw new Win32Exception(); // SRCCOPY | CAPTUREBLT
            if (includeCursor) DrawCursor(memory, bounds);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }

    internal static string Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var bytes = new MemoryStream();
        encoder.Save(bytes);
        return "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
    }

    static void DrawCursor(nint dc, Int32Rect bounds)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0 || !GetIconInfo(cursor.Cursor, out var icon)) return;
        try
        {
            // Subtract the cursor hotspot, not just its screen position. GDI
            // clips the pointer naturally when it overlaps a capture edge.
            if (!DrawIconEx(dc, cursor.X - bounds.X - (int)icon.HotspotX, cursor.Y - bounds.Y - (int)icon.HotspotY,
                cursor.Cursor, 0, 0, 0, 0, 3)) throw new Win32Exception(); // DI_NORMAL
        }
        finally
        {
            if (icon.Mask != 0) DeleteObject(icon.Mask);
            if (icon.Color != 0) DeleteObject(icon.Color);
        }
    }

    internal static void Place(Window window, Int32Rect pixels) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, new nint(-1), pixels.X, pixels.Y, pixels.Width, pixels.Height, 0x0040);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly Int32Rect Pixels => new(Left, Top, Math.Max(0, Width), Math.Max(0, Height));
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    delegate bool WindowCallback(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo
    {
        public int Size;
        public uint Flags;
        public nint Cursor;
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX, HotspotY;
        public nint Mask, Color;
    }
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo cursor);
    [DllImport("user32.dll")] static extern bool GetIconInfo(nint cursor, out IconInfo icon);
    [DllImport("user32.dll", SetLastError = true)] static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
