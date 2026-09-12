using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class LinkPreviewSelfTest
{
    public static async Task Run(Action<bool, string> check)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8)));
        using var stream = new MemoryStream(); encoder.Save(stream);
        using var handler = new PreviewResponses(stream.ToArray());
        using var client = new HttpClient(handler);
        var preview = await LinkPreview.FetchAsync("https://preview.invalid/page", client);
        check(preview?.Title == "Example & title" && preview.Image.StartsWith("data:image/png;base64,") && handler.Addresses.Last() == "https://preview.invalid/photo.png",
            "Link preview reads reordered metadata attributes, resolves relative images and embeds a decoded PNG");
        foreach (string url in new[] { "https://youtu.be/AbcD_123-xy?t=3", "https://www.youtube.com/watch?v=AbcD_123-xy", "https://youtube.com/shorts/AbcD_123-xy", "https://www.youtube.com/embed/AbcD_123-xy" })
        {
            preview = await LinkPreview.FetchAsync(url, client);
            check(preview?.Image.StartsWith("data:image/png;base64,") == true && handler.Addresses.Last() == "https://i.ytimg.com/vi/AbcD_123-xy/hqdefault.jpg", "YouTube thumbnail supports " + new Uri(url).Host + new Uri(url).AbsolutePath);
        }
        check(LinkPreview.YouTubeId(new Uri("https://youtube.com.evil.invalid/watch?v=AbcD_123-xy")) == null, "Only exact YouTube hosts receive YouTube thumbnail handling");
        check(await LinkPreview.FetchAsync("file:///private.png", client) == null && await LinkPreview.FetchAsync("https://preview.invalid/large", client) == null,
            "Thumbnail retrieval rejects non-web addresses and oversized responses");
        check(await LinkPreview.FetchAsync("https://preview.invalid/missing", client) == null, "Missing link thumbnails fall back without failing link insertion");
        var metadata = LinkPreview.Metadata("<title>Title</title><meta name='twitter:image' content='/twitter.png'>", new Uri("https://preview.invalid/path/page"));
        check(metadata.Title == "Title" && metadata.Image == "https://preview.invalid/twitter.png", "Twitter thumbnail metadata and page titles are supported");
    }

    sealed class PreviewResponses(byte[] png) : HttpMessageHandler
    {
        public List<string> Addresses { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri; Addresses.Add(url);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (url.EndsWith("/page")) response.Content = new StringContent("<meta content='Example &amp; title' property='og:title'><meta content='photo.png' property='og:image'>", System.Text.Encoding.UTF8, "text/html");
            else if (url.EndsWith("/large")) { response.Content = new ByteArrayContent([]); response.Content.Headers.ContentLength = 3_000_000; }
            else if (url.EndsWith(".png") || url.EndsWith(".jpg")) { response.Content = new ByteArrayContent(png); response.Content.Headers.ContentType = new("image/png"); }
            else response.StatusCode = HttpStatusCode.NotFound;
            return Task.FromResult(response);
        }
    }
}
