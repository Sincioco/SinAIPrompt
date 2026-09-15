using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// One editor owns its exact-file display mappings. Saved HTML keeps ordinary references.
internal sealed class OriginalImages : IDisposable
{
    internal sealed record Mapping(string source, string display);
    readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    readonly string draftPath;
    bool disposed;
    internal OriginalImages(string storageFolder) => draftPath = Path.Combine(storageFolder, "Untitled.html");
    internal void Attach(CoreWebView2 browser)
    {
        var environment = browser.Environment;
        browser.AddWebResourceRequestedFilter("https://sin-original.local/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        browser.WebResourceRequested += async (_, args) =>
        {
            if (new Uri(args.Request.Uri).Host != "sin-original.local") return;
            using var deferral = args.GetDeferral();
            try
            {
                if (!files.TryGetValue(args.Request.Uri, out var path)) throw new IOException("Unknown image reference.");
                var bytes = await File.ReadAllBytesAsync(path);
                string mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml", ".gif" => "image/gif",
                    ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "image/png"
                };
                if (!disposed) args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (!disposed) args.Response = environment.CreateWebResourceResponse(Stream.Null, 404, "Image unavailable", ""); }
        };
    }
    Uri Base(string? document, string? baseHref)
    {
        var folder = new Uri(new Uri(Path.GetFullPath(document ?? draftPath)).AbsoluteUri);
        if (string.IsNullOrEmpty(baseHref)) return new Uri(folder, ".");
        var result = new Uri(folder, baseHref);
        return result.Host == "sin-document.local" ? new Uri(new Uri(folder, "."), result.AbsolutePath.TrimStart('/')) : result;
    }
    internal string Source(string source)
    {
        if (files.TryGetValue(source, out var path)) return new Uri(path).AbsoluteUri;
        return source;
    }
    internal static string? ChooseSource(Window owner, string source)
    {
        if (!source.StartsWith("data:") && !source.StartsWith("blob:")) return source;
        var dialog = new OpenFileDialog { Title = "Choose The Original Image", Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? new Uri(dialog.FileName).AbsoluteUri : null;
    }
    internal Mapping Map(string? document, string source, string? baseHref)
    {
        var absolute = new Uri(Base(document, baseHref), Source(source));
        if (!absolute.IsFile) throw new IOException("Reference Original Image requires a local image file.");
        string path = Path.GetFullPath(absolute.LocalPath);
        if (!File.Exists(path)) throw new FileNotFoundException("The original image is no longer at this location.", path);
        string display = "https://sin-original.local/" + TextFiles.Hash(Encoding.UTF8.GetBytes(path.ToUpperInvariant())).ToLowerInvariant();
        files[display] = path;
        return new(new Uri(path).AbsoluteUri, display);
    }
    internal string Reference(string? document, string source, bool absolute, string? baseHref, string? destination = null)
    {
        var resolved = new Uri(Base(document, baseHref), Source(source));
        if (!resolved.IsFile) throw new IOException("Choose the original image file to create a reference.");
        if (absolute) return resolved.AbsoluteUri;
        var target = Base(destination ?? document, destination == null ? baseHref : null);
        if (!target.IsFile || !string.Equals(Path.GetPathRoot(target.LocalPath), Path.GetPathRoot(resolved.LocalPath), StringComparison.OrdinalIgnoreCase)) return resolved.AbsoluteUri;
        var relative = target.MakeRelativeUri(resolved);
        return relative.ToString();
    }
    public void Dispose() { disposed = true; files.Clear(); }
}
