using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class HtmlAssets
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static byte[] Png(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var image = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(image.Frames[0]);
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    public static string SavePng(string documentPath, string data)
    {
        var bytes = Png(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
        string folderName = Path.GetFileNameWithoutExtension(documentPath);
        string folder = Path.Combine(Path.GetDirectoryName(documentPath)!, folderName);
        Directory.CreateDirectory(folder);
        string name = "image-" + Guid.NewGuid().ToString("N") + ".png";
        Core.TextFiles.AtomicWrite(Path.Combine(folder, name), bytes);
        return Uri.EscapeDataString(folderName) + "/" + name;
    }
    public static async Task<string> ReadImageAsync(string? documentPath, string source)
    {
        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return source;
        byte[] bytes;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Host != "sin-document.local")
        {
            if (uri.IsFile) bytes = await File.ReadAllBytesAsync(uri.LocalPath);
            else if (uri.Scheme is "https" or "http") bytes = await http.GetByteArrayAsync(uri);
            else throw new IOException("This image URL is unsupported.");
        }
        else
        {
            string relative = uri?.Host == "sin-document.local" ? Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) : Uri.UnescapeDataString(source.Split('?','#')[0]);
            string folder = documentPath == null ? App.Current.Store.DirectoryPath : Path.GetDirectoryName(documentPath)!;
            bytes = await File.ReadAllBytesAsync(Path.GetFullPath(Path.Combine(folder, relative)));
        }
        // SVG is rasterized in the browser; WPF decodes other common image formats losslessly to PNG.
        if (System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 300))).Contains("<svg", StringComparison.OrdinalIgnoreCase))
            return "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
        return "data:image/png;base64," + Convert.ToBase64String(Png(bytes));
    }
    public static string ClipboardPng()
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Clipboard.GetImage()));
        using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
}
