using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Cache three stock Windows Shell icons, loaded off the UI thread on first use.
internal static class ExplorerIcons
{
    static readonly Lazy<Task<IReadOnlyDictionary<int, ImageSource>>> icons = new(() => Task.Run<IReadOnlyDictionary<int, ImageSource>>(() =>
    {
        var values = new Dictionary<int, ImageSource>();
        foreach (int kind in new[] { 0, 3, 72 })
        {
            var info = new StockIconInfo { Size = (uint)Marshal.SizeOf<StockIconInfo>() };
            Marshal.ThrowExceptionForHR(SHGetStockIconInfo(kind, 0x101, ref info)); // SHGSI_ICON | SHGSI_SMALLICON
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze(); values[kind] = source;
            }
            finally { DestroyIcon(info.Icon); }
        }
        return values;
    }));
    internal static Task<IReadOnlyDictionary<int, ImageSource>> LoadAsync() => icons.Value;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StockIconInfo
    {
        public uint Size;
        public IntPtr Icon;
        public int SystemIndex, IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }
    [DllImport("shell32.dll")] static extern int SHGetStockIconInfo(int id, uint flags, ref StockIconInfo info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
