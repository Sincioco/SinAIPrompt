using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Fetch only when the user inserts a link. Returned PNGs are embedded in the
// document, so reopening it never contacts the source site or loads its scripts.
internal static class LinkPreview
{
    internal sealed record Preview([property: JsonPropertyName("title")] string Title, [property: JsonPropertyName("image")] string Image);
    static readonly HttpClient http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
        MaxAutomaticRedirections = 5
    });
    static readonly TimeSpan regexTimeout = TimeSpan.FromMilliseconds(150);

    public static async Task<Preview?> FetchAsync(string address, HttpClient? client = null)
    {
        if (!WebUri(address, out var page)) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            client ??= http;
            string title = "", imageAddress = "";
            var video = YouTubeId(page!);
            if (video != null) imageAddress = $"https://i.ytimg.com/vi/{video}/hqdefault.jpg";
            else
            {
                var content = await ReadAsync(client, page!, 2_000_000, timeout.Token);
                if (content.Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return new("", await Task.Run(() => Thumbnail(content.Bytes), timeout.Token));
                if (!content.Type.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;
                var metadata = Metadata(Encoding.UTF8.GetString(content.Bytes), content.Address);
                title = metadata.Title; imageAddress = metadata.Image;
            }
            if (!WebUri(imageAddress, out var image)) return null;
            var bytes = await ReadAsync(client, image!, 8_000_000, timeout.Token);
            if (!bytes.Type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            return new(title, await Task.Run(() => Thumbnail(bytes.Bytes), timeout.Token));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Missing/private/blocked metadata is ordinary: the link still works.
            return null;
        }
    }

    internal static Preview Metadata(string html, Uri page)
    {
        string title = "", image = "", fallback = "";
        foreach (Match tag in Regex.Matches(html, @"<(?:meta|link)\b[^>]*>", RegexOptions.IgnoreCase, regexTimeout))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Regex.Matches(tag.Value, "([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.None, regexTimeout))
                attributes[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value);
            string name = attributes.GetValueOrDefault("property", attributes.GetValueOrDefault("name", "")).ToLowerInvariant();
            string value = attributes.GetValueOrDefault("content", "");
            if (name == "og:title" && title.Length == 0) title = value;
            if (name is "og:image" or "og:image:url" or "og:image:secure_url" && image.Length == 0) image = value;
            if (name is "twitter:image" or "twitter:image:src" && fallback.Length == 0) fallback = value;
            if (attributes.GetValueOrDefault("rel", "").Equals("image_src", StringComparison.OrdinalIgnoreCase) && fallback.Length == 0)
                fallback = attributes.GetValueOrDefault("href", "");
        }
        if (title.Length == 0)
            title = WebUtility.HtmlDecode(Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, regexTimeout).Groups[1].Value).Trim();
        string candidate = image.Length > 0 ? image : fallback;
        return new(title, candidate.Length > 0 && Uri.TryCreate(page, candidate, out var url) && WebUri(url.AbsoluteUri, out _) ? url.AbsoluteUri : "");
    }

    internal static string? YouTubeId(Uri url)
    {
        string host = url.IdnHost.ToLowerInvariant(), path = url.AbsolutePath.Trim('/'), value = "";
        if (host is "youtu.be" or "www.youtu.be") value = path.Split('/')[0];
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            var parts = path.Split('/');
            if (parts.Length > 1 && parts[0] is "shorts" or "embed" or "live") value = parts[1];
            else foreach (string pair in url.Query.TrimStart('?').Split('&'))
                if (pair.StartsWith("v=", StringComparison.Ordinal)) { value = Uri.UnescapeDataString(pair[2..]); break; }
        }
        return Regex.IsMatch(value, @"^[A-Za-z0-9_-]{11}$", RegexOptions.None, regexTimeout) ? value : null;
    }

    static bool WebUri(string value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;

    static async Task<(byte[] Bytes, string Type, Uri Address)> ReadAsync(HttpClient client, Uri url, int limit, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SinAIPrompt/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Preview exceeds size limit.");
        using var input = await response.Content.ReadAsStreamAsync(cancel);
        using var output = new MemoryStream();
        var buffer = new byte[16_384]; int count;
        while ((count = await input.ReadAsync(buffer, cancel)) != 0)
        {
            if (output.Length + count > limit) throw new IOException("Preview exceeds size limit.");
            output.Write(buffer, 0, count);
        }
        return (output.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "", response.RequestMessage?.RequestUri ?? url);
    }

    static string Thumbnail(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 640; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 8_000_000) throw new IOException("Preview is too tall.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
}
