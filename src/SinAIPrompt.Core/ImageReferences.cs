using System.Net;
using System.Text.RegularExpressions;

namespace SinAIPrompt.Core;

// Reads references, never image pixels. Used only for expanded asset folders.
public static class ImageReferences
{
    static readonly TimeSpan timeout = TimeSpan.FromMilliseconds(200);
    public static HashSet<string> LocalPaths(string html, string documentPath)
    {
        // Explicit file URIs resolve %-encoded HTML URLs; implicit DOS-path Uris
        // otherwise double-escape percent signs when combining relative sources.
        var document = new Uri(new Uri(System.IO.Path.GetFullPath(documentPath)).AbsoluteUri);
        var folder = new Uri(document, ".");
        Uri Local(Uri uri) => uri.Host.Equals("sin-document.local", StringComparison.OrdinalIgnoreCase)
            ? new Uri(folder, uri.AbsolutePath.TrimStart('/') + uri.Query) : uri;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?(?:-->|$)|<(script|textarea)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase, timeout);
        var tags = Regex.Matches(html, "<[a-z][\\w:-]*\\b(?:[^>\"']|\"[^\"]*\"|'[^']*')*>", RegexOptions.IgnoreCase, timeout);
        var attributes = new List<(bool Base, Dictionary<string, string> Values)>();
        foreach (Match tag in tags)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Regex.Matches(tag.Value, "([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.None, timeout))
                values[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value);
            attributes.Add((Regex.IsMatch(tag.Value, @"^<base\b", RegexOptions.IgnoreCase, timeout), values));
        }
        var baseAddress = attributes.FirstOrDefault(t => t.Base && t.Values.ContainsKey("href")).Values?.GetValueOrDefault("href");
        var baseUri = Uri.TryCreate(document, baseAddress, out var explicitBase) ? Local(explicitBase) : document;
        void Add(string? source)
        {
            if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(baseUri, source, out var uri)) return;
            uri = Local(uri);
            if (uri.IsFile) paths.Add(System.IO.Path.GetFullPath(uri.LocalPath));
        }
        foreach (var tag in attributes.Where(t => !t.Base))
        {
            foreach (string name in new[] { "src", "href", "xlink:href", "poster", "data" }) Add(tag.Values.GetValueOrDefault(name));
            foreach (string candidate in tag.Values.GetValueOrDefault("srcset", "").Split(','))
                Add(candidate.Trim().Split(' ', '\t', '\n')[0]);
        }
        var css = attributes.Select(t => t.Values.GetValueOrDefault("style", "")).Concat(
            Regex.Matches(html, @"<style\b[^>]*>([\s\S]*?)</style\s*>", RegexOptions.IgnoreCase, timeout).Select(m => m.Groups[1].Value));
        foreach (string style in css)
            foreach (Match url in Regex.Matches(Regex.Replace(style, @"/\*[\s\S]*?\*/", "", RegexOptions.None, timeout),
                "url\\(\\s*(?:\"([^\"]*)\"|'([^']*)'|([^)]*?))\\s*\\)", RegexOptions.IgnoreCase, timeout))
                Add(WebUtility.HtmlDecode(url.Groups[1].Success ? url.Groups[1].Value : url.Groups[2].Success ? url.Groups[2].Value : url.Groups[3].Value.Trim()));
        return paths;
    }
}
