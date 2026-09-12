using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class HtmlAssetsSelfTest
{
    public static async Task Run(string folder, Action<bool, string> check)
    {
        string document = Path.Combine(folder, "Image hash.html"), assets = Path.Combine(folder, "Image hash");
        string Png(byte red)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, red, 255 }, 4)));
            using var output = new MemoryStream(); encoder.Save(output);
            return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
        }
        string image = Png(255), first = await HtmlAssets.SavePngAsync(document, image);
        string repeated = await HtmlAssets.SavePngAsync(document, image);
        check(first == repeated && Directory.GetFiles(assets).Length == 1, "Repeated PNG saves reuse the same SHA-256 image file");
        check(await HtmlAssets.ReusePngAsync(document, image) == first, "Repeated paste finds its existing file before asking for image storage");
        string renamed = Path.Combine(assets, "Custom # name.png"); File.Move(Path.Combine(folder, Uri.UnescapeDataString(first)), renamed);
        check(await HtmlAssets.SavePngAsync(document, image) == "Image%20hash/Custom%20%23%20name.png" && Directory.GetFiles(assets).Length == 1,
            "Hash reuse preserves a user's renamed PNG filename");
        string other = await HtmlAssets.SavePngAsync(document, Png(128));
        check(other != first && Directory.GetFiles(assets).Length == 2, "Different image bytes receive distinct files");
        File.WriteAllText(Path.Combine(folder, Uri.UnescapeDataString(other)), "External edit");
        string preserved = await HtmlAssets.SavePngAsync(document, Png(128));
        check(preserved != other && File.ReadAllText(Path.Combine(folder, Uri.UnescapeDataString(other))) == "External edit",
            "Saving an image never overwrites a changed file with its former hash filename");
        EditorClipboard.Copy("<img src='data:image/png;base64,external'>", "", "<img src='Images/photo.png' data-sin-storage='separate'>", "document-one");
        string same = System.Text.Json.JsonSerializer.Serialize(await EditorClipboard.ReadAsync("document-one"));
        string different = System.Text.Json.JsonSerializer.Serialize(await EditorClipboard.ReadAsync("document-two"));
        check(same.Contains("Images/photo.png") && different.Contains("data:image/png;base64,external"),
            "Image clipboard preserves same-document references and gives other documents portable image data");
    }
}
