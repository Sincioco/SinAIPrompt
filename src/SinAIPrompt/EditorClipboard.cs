using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Windows text/HTML interchange; browser selection and editing stay in the editor.
internal static class EditorClipboard
{
    static DataObject? testData;

    internal static void Copy(string html, string text)
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
        data.SetData(DataFormats.Html, string.Format(header, startHtml, endHtml, startFragment, endFragment) + prefix + html + suffix);
        if (App.Current.TestMode) testData = data;
        else Clipboard.SetDataObject(data, true);
    }

    internal static object? Read()
    {
        var data = App.Current.TestMode ? testData : Clipboard.GetDataObject();
        if (data == null) return null;
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
            return new { html };
        }
        if (data.GetData("PNG") is MemoryStream png) return new { image = "data:image/png;base64," + Convert.ToBase64String(png.ToArray()) };
        if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            return new { image = "data:image/png;base64," + Convert.ToBase64String(output.ToArray()) };
        }
        return new { text = data.GetData(DataFormats.UnicodeText) as string ?? "" };
    }
}
