using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Windows text/HTML interchange; browser selection and editing stay in the editor.
internal static class EditorClipboard
{
    internal static DataObject? TestData { get; set; }

    const string imageFormat = "SinAIPrompt.DocumentImages.v1";
    sealed record DocumentFragment(string Document, string Html);
    internal static void Copy(string html, string text, string? internalHtml = null, string? documentKey = null)
    {
        const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        int startHtml = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(html);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        if (internalHtml != null && documentKey != null) data.SetData(imageFormat, JsonSerializer.Serialize(new DocumentFragment(documentKey, internalHtml)));
        data.SetData(DataFormats.Html, string.Format(header, startHtml, endHtml, startFragment, endFragment) + prefix + html + suffix);
        if (App.Current.TestMode) TestData = data;
        else Clipboard.SetDataObject(data, true);
    }

    internal static async Task<object?> ReadAsync(string? documentKey = null)
    {
        var data = App.Current.TestMode ? TestData : Clipboard.GetDataObject();
        if (data == null) return null;
        if (documentKey != null && data.GetData(imageFormat) is string internalJson)
        {
            try
            {
                var fragment = JsonSerializer.Deserialize<DocumentFragment>(internalJson);
                if (fragment?.Document == documentKey) return new { html = fragment.Html, text = data.GetData(DataFormats.UnicodeText) as string ?? "", preserveImageMarkup = true };
            }
            catch (JsonException) { }
        }
        if (data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(path => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown") is { } markdown)
            return new { markdown = await File.ReadAllTextAsync(markdown), @base = new Uri(Path.GetDirectoryName(markdown)! + Path.DirectorySeparatorChar).AbsoluteUri };
        string text = data.GetData(DataFormats.UnicodeText) as string ?? "";
        string? html = data.GetData(DataFormats.Html) as string;
        if (!string.IsNullOrEmpty(html))
        {
            int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start) html = html[(start + 20)..end];
            else
            {
                var bytes = Encoding.UTF8.GetBytes(html);
                int Offset(string key)
                {
                    int index = html.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) return -1;
                    string line = html[(index + key.Length)..].Split('\r', '\n')[0];
                    return int.TryParse(line, out int value) ? value : -1;
                }
                start = Offset("StartFragment:"); end = Offset("EndFragment:");
                html = start >= 0 && end >= start && end <= bytes.Length ? Encoding.UTF8.GetString(bytes, start, end - start) : html[html.IndexOf('<')..];
            }
            return new { html, text, preserveImageMarkup = data.GetDataPresent(imageFormat) };
        }
        if (data.GetData(DataFormats.FileDrop) is string[] imageFiles && imageFiles.FirstOrDefault(path => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg") is { } originalImage)
            return new { image = new Uri(originalImage).AbsoluteUri };
        if (data.GetData("PNG") is MemoryStream png) return new { image = "data:image/png;base64," + Convert.ToBase64String(png.ToArray()) };
        if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            return new { image = "data:image/png;base64," + Convert.ToBase64String(output.ToArray()) };
        }
        return new { text };
    }
}
