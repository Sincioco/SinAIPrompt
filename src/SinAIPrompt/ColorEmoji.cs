using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// A resource instance owns a bounded cache of frozen images. No per-row WebView,
// downloaded artwork, font parsing, or document/editor dependency is needed.
public sealed class ColorEmoji : IValueConverter
{
    readonly Dictionary<string, BitmapSource> images = [];

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return null;
        if (!images.TryGetValue(text, out var image))
        {
            image = Render(text);
            if (images.Count >= 256) images.Clear();
            images.Add(text, image);
        }
        return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static BitmapSource Render(string text)
    {
        const int size = 96;
        nint wic = 0, bitmap = 0, drawing = 0, target = 0, write = 0, format = 0, brush = 0;
        try
        {
            var wicClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
            var wicInterface = new Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
            Check(CoCreateInstance(ref wicClass, 0, 1, ref wicInterface, out wic));
            var pixelFormat = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910"); // 32bppPBGRA
            Check(Method<CreateBitmap>(wic, 17)(wic, size, size, ref pixelFormat, 2, out bitmap));
            var drawingInterface = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
            Check(D2D1CreateFactory(0, ref drawingInterface, 0, out drawing));
            var properties = new TargetProperties { Type = 1, Format = 87, AlphaMode = 1, DpiX = 96, DpiY = 96 };
            Check(Method<CreateTarget>(drawing, 13)(drawing, bitmap, ref properties, out target));
            var writeInterface = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
            Check(DWriteCreateFactory(0, ref writeInterface, out write));
            Check(Method<CreateFormat>(write, 15)(write, "Segoe UI Emoji", 0, 400, 0, 5, 72, "en-us", out format));
            Check(Method<SetAlignment>(format, 3)(format, 2));
            Check(Method<SetAlignment>(format, 4)(format, 1));
            var ink = new FourFloats { D = 1 };
            Check(Method<CreateBrush>(target, 8)(target, ref ink, 0, out brush));
            Method<SetMode>(target, 34)(target, 2); // grayscale antialiasing for transparent output
            Method<Begin>(target, 48)(target);
            var transparent = new FourFloats();
            Method<Clear>(target, 47)(target, ref transparent);
            var rectangle = new FourFloats { C = size, D = size };
            // DirectWrite shapes ZWJ/skin tones; ENABLE_COLOR_FONT (4) draws Windows colors.
            Method<DrawText>(target, 27)(target, text, text.Length, format, ref rectangle, brush, 4, 0);
            Check(Method<End>(target, 49)(target, out _, out _));
            var pixels = new byte[size * size * 4];
            Check(Method<CopyPixels>(bitmap, 7)(bitmap, 0, size * 4, pixels.Length, pixels));
            var result = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            result.Freeze(); return result;
        }
        finally
        {
            foreach (nint instance in new[] { brush, format, write, target, drawing, bitmap, wic })
                if (instance != 0) Marshal.Release(instance);
        }
    }

    // ABI slots/signatures from the installed Windows SDK: d2d1.h, dwrite.h, wincodec.h.
    static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    [StructLayout(LayoutKind.Sequential)] struct TargetProperties { public uint Type, Format, AlphaMode; public float DpiX, DpiY; public uint Usage, MinLevel; }
    [StructLayout(LayoutKind.Sequential)] struct FourFloats { public float A, B, C, D; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBitmap(nint self, int width, int height, ref Guid format, int cache, out nint bitmap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTarget(nint self, nint bitmap, ref TargetProperties properties, out nint target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int CreateFormat(nint self, string family, nint collection, int weight, int style, int stretch, float size, string locale, out nint format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetAlignment(nint self, int alignment);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBrush(nint self, ref FourFloats color, nint properties, out nint brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SetMode(nint self, int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Begin(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Clear(nint self, ref FourFloats color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate void DrawText(nint self, string text, int length, nint format, ref FourFloats rectangle, nint brush, int options, int measuringMode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int End(nint self, out ulong tag1, out ulong tag2);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CopyPixels(nint self, nint rectangle, int stride, int size, [Out] byte[] pixels);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(uint type, ref Guid iid, nint options, out nint factory);
    [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(uint type, ref Guid iid, out nint factory);
}
