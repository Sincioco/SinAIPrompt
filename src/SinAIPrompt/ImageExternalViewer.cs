using System.Diagnostics;
using System.IO;

namespace SinAIPrompt;

internal static class ImageExternalViewer
{
    internal static async Task OpenAsync(string storage, string data)
    {
        string path = await Task.Run(() => Prepare(storage, data));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    internal static string Prepare(string storage, string data)
    {
        // Launch only an image we decoded ourselves, including for embedded/SVG sources.
        byte[] png = HtmlAssets.Png(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
        string folder = Path.Combine(storage, "Image previews"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Core.TextFiles.Hash(png).ToLowerInvariant() + ".png");
        if (!File.Exists(path)) Core.TextFiles.AtomicWrite(path, png);
        return path;
    }
}
