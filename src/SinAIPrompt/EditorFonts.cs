using System.IO;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace SinAIPrompt;

// Reuse Microsoft fonts already cached by Office on this PC. No font binaries
// are copied into the app, saved HTML, or distribution; there are no downloads.
internal static class EditorFonts
{
    static readonly Lazy<Task<(string? Root, string Css)>> catalog = new(() => Task.Run(ReadCatalog));

    internal static async Task<string> StyleSheetAsync(CoreWebView2 browser)
    {
        var fonts = await catalog.Value;
        if (fonts.Root != null)
            browser.SetVirtualHostNameToFolderMapping("sin-office-fonts.local", fonts.Root, CoreWebView2HostResourceAccessKind.Allow);
        return fonts.Css;
    }

    static (string? Root, string Css) ReadCatalog()
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "FontCache");
        if (!Directory.Exists(cache)) return (null, "");
        try
        {
            foreach (string version in Directory.EnumerateDirectories(cache).OrderDescending())
            {
                string root = Path.Combine(version, "CloudFonts");
                if (!Directory.Exists(Path.Combine(root, "Aptos"))) continue;
                var rules = new List<string>();
                var faces = new HashSet<string>();
                foreach (string family in new[] { "Aptos", "Aptos Display" })
                {
                    string folder = Path.Combine(root, family);
                    if (!Directory.Exists(folder)) continue;
                    foreach (string path in Directory.EnumerateFiles(folder, "*.ttf"))
                    {
                        try
                        {
                            var face = new GlyphTypeface(new Uri(path));
                            int weight = face.Weight.ToOpenTypeWeight();
                            if (weight != 400 && weight != 700) continue;
                            string style = face.Style == System.Windows.FontStyles.Italic ? "italic" : "normal";
                            if (!faces.Add($"{family}/{weight}/{style}")) continue;
                            string url = "https://sin-office-fonts.local/" + Uri.EscapeDataString(family) + "/" + Uri.EscapeDataString(Path.GetFileName(path));
                            rules.Add($"@font-face{{font-family:'{family}';font-weight:{weight};font-style:{style};font-display:swap;src:local('{family}{(weight == 700 ? " Bold" : "")}{(style == "italic" ? " Italic" : "")}'),url('{url}');}}");
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                    }
                }
                return (root, string.Join('\n', rules));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (null, "");
    }
}
