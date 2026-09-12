using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class AnnotationClipboard
{
    const string ObjectsFormat = "SinAIPrompt.AnnotationObjects";
    internal static DataObject? TestData { get; private set; }

    internal static void Copy(string format, string content, string objects)
    {
        var data = new DataObject();
        data.SetData(ObjectsFormat, objects);
        if (format == "svg")
        {
            data.SetData("image/svg+xml", new MemoryStream(Encoding.UTF8.GetBytes(content)));
            data.SetText(content);
        }
        else if (format == "png")
        {
            var bytes = Convert.FromBase64String(content[(content.IndexOf(',') + 1)..]);
            data.SetData("PNG", new MemoryStream(bytes));
            using var stream = new MemoryStream(bytes);
            data.SetImage(BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));
        }
        else throw new ArgumentException("Choose SVG or PNG.");
        if (App.Current.TestMode) TestData = data;
        else Clipboard.SetDataObject(data, true);
    }

    internal static object? Read()
    {
        var data = App.Current.TestMode ? TestData : Clipboard.GetDataObject();
        if (data?.GetData(ObjectsFormat) is string objects) return new { objects };
        if (data?.GetData("PNG") is MemoryStream png) return new { image = "data:image/png;base64," + Convert.ToBase64String(png.ToArray()) };
        if (data?.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            return new { image = "data:image/png;base64," + Convert.ToBase64String(output.ToArray()) };
        }
        if (data?.GetData("image/svg+xml") is MemoryStream svg) return new { image = "data:image/svg+xml;base64," + Convert.ToBase64String(svg.ToArray()) };
        return null;
    }
}
