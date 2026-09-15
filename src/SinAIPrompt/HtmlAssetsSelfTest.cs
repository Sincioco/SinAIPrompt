using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class HtmlAssetsSelfTest
{
    internal static async Task Paste(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!; var browser = view.Browser;
        string original = view.Document.Text;
        var clipboard = EditorClipboard.TestData;
        string path = Path.Combine(App.Current.Store.DirectoryPath, "Photos image #1.png");
        var pixels = new byte[320 * 180 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 255; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(320, 180, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4)));
        using (var output = File.Create(path)) encoder.Save(output);
        string fragment = "<img src=\"" + new Uri(path).AbsoluteUri + "\">";
        void CopyPhoto()
        {
            // Actual Windows Photos format: spaced comment markers and byte offsets,
            // with both Bitmap and FileDrop alternatives alongside the image-only HTML.
            const string header = "Version:1.0\r\nStartHTML:{0:D8}\r\nEndHTML:{1:D8}\r\nStartFragment:{2:D8}\r\nEndFragment:{3:D8}\r\n";
            const string prefix = "<!DOCTYPE><HTML><HEAD></HEAD><BODY><!--StartFragment -->", suffix = "<!--EndFragment --></BODY></HTML>";
            string body = "<p>" + fragment + "</p>";
            int startHtml = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
            int start = startHtml + Encoding.UTF8.GetByteCount(prefix), end = start + Encoding.UTF8.GetByteCount(body);
            var data = new DataObject();
            data.SetData(DataFormats.Html, string.Format(header, startHtml, end + Encoding.UTF8.GetByteCount(suffix), start, end) + prefix + body + suffix);
            data.SetData(DataFormats.FileDrop, new[] { path }); data.SetImage(encoder.Frames[0]);
            EditorClipboard.TestData = data;
        }
        async Task Evaluate(string expression) => await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true }));
        try
        {
            view.FocusEditing();
            await Evaluate("window.editor.load('<p><br></p>').then(()=>window.editor.focus())");
            await browser.ExecuteScriptAsync("window.editor.setImageStorage('inline')");
            CopyPhoto();
            await Evaluate("window.editor.command('paste')");
            string inspected = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = "(async()=>{const image=document.querySelector('#document').contentDocument.images[0];await image?.decode().catch(()=>{});return image?.naturalWidth===320&&image.naturalHeight===180&&image.src.startsWith('data:image/png;')&&image.style.width==='320px';})()",
                awaitPromise = true, returnByValue = true
            }));
            using var pasteResult = JsonDocument.Parse(inspected);
            check(pasteResult.RootElement.GetProperty("result").GetProperty("value").GetBoolean(),
                "Pasting standalone Photos-style file HTML produces a readable stored PNG at its actual dimensions");
            string portable = "<img src=\"data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)) + "\" data-sin-storage=\"inline\" data-sin-annotation=\"{}\" style=\"width:64px\">";
            EditorClipboard.Copy(portable, "", portable, "a-different-document");
            await Evaluate("window.editor.load('<p><br></p>').then(()=>window.editor.focus()).then(()=>window.editor.command('paste'))");
            check(await browser.ExecuteScriptAsync("(()=>{const image=document.querySelector('#document').contentDocument.images[0];return image?.style.width==='64px'&&image.dataset.sinAnnotation==='{}';})()") == "true",
                "Cross-document copies retain annotation metadata and intentional image size without another storage prompt");
            CopyPhoto();
            await browser.ExecuteScriptAsync("window.editor.setImageStorage('separate')");
            await Evaluate("window.editor.load('<p><br></p>').then(()=>window.editor.focus()).then(()=>window.editor.command('paste')).then(()=>window.editor.command('paste'))");
            check(await browser.ExecuteScriptAsync("(()=>{const images=document.querySelector('#document').contentDocument.images;return images.length===2&&images[0].getAttribute('src')===images[1].getAttribute('src')&&images[0].dataset.sinStorage==='separate';})()") == "true",
                "Repeated Photos-style pastes reuse the same separately stored PNG through image hashing");
            await Evaluate("window.editor.load(" + JsonSerializer.Serialize(fragment) + ")");
            check(await browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.images[0].naturalWidth===0") == "true",
                "Existing local-file image fixture reproduces the broken browser image");
            await browser.ExecuteScriptAsync("window.photoEdit=window.editor.openAnnotation(document.querySelector('#document').contentDocument.images[0]);void 0");
            bool shown = false;
            for (int i = 0; i < 100 && !shown; i++) { await Task.Delay(20); shown = await browser.ExecuteScriptAsync("!!document.querySelector('dialog.annotation [data-action=apply]')") == "true"; }
            check(shown, "Annotation can decode an existing broken local-file image through the native image reader");
            await browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=apply]').click()");
            await Evaluate("window.photoEdit");
            check(await browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.images[0].style.width==='320px'") == "true",
                "Applying annotation to a broken image restores decoded width instead of preserving the tiny error placeholder");
        }
        finally
        {
            EditorClipboard.TestData = clipboard;
            await browser.ExecuteScriptAsync("window.editor.setImageStorage(" + JsonSerializer.Serialize(App.Current.Preferences.ImageStorage) + ")");
            await Evaluate("window.editor.load(" + JsonSerializer.Serialize(original) + ")");
            view.AcceptHtml(original);
        }
    }

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
        string external = await Task.Run(() => ImageExternalViewer.Prepare(folder, image));
        check(external.EndsWith(".png") && File.Exists(external) && Path.GetDirectoryName(external) == Path.Combine(folder, "Image previews") &&
            ImageExternalViewer.Prepare(folder, image) == external, "External image viewing prepares a reusable decoded PNG in application storage");
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

    internal static async Task References(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!; var browser = view.Browser.CoreWebView2;
        string original = view.Document.Text, folder = Path.Combine(App.Current.Store.DirectoryPath, "Original images");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Reference # photo.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgr32, null, new byte[16 * 8 * 4], 16 * 4)));
        using (var output = File.Create(path)) encoder.Save(output);
        byte[] originalBytes = File.ReadAllBytes(path);
        using var references = new OriginalImages(folder);
        string otherDrive = Path.GetPathRoot(path) == "C:\\" ? "D:\\" : "C:\\";
        check(references.Reference(Path.Combine(otherDrive, "Prompt.html"), new Uri(path).AbsoluteUri, false, null) == new Uri(path).AbsoluteUri,
            "A cross-drive image reference falls back to a valid absolute URI");
        var clipboard = EditorClipboard.TestData;
        async Task Evaluate(string expression)
        {
            string response = await browser.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true }));
            using var parsed = JsonDocument.Parse(response);
            if (parsed.RootElement.TryGetProperty("exceptionDetails", out var failure)) throw new Exception(failure.ToString());
        }
        async Task WaitFor(string selector)
        {
            for (int i = 0; i < 150; i++) { if (await view.Browser.ExecuteScriptAsync("!!document.querySelector(" + JsonSerializer.Serialize(selector) + ")") == "true") return; await Task.Delay(20); }
            throw new Exception("Image reference control did not appear: " + selector);
        }
        try
        {
            foreach (string mode in new[] { "relative", "absolute" })
            {
                await Evaluate("window.editor.load('<p><br></p>').then(()=>window.editor.focus())");
                await view.Browser.ExecuteScriptAsync("window.editor.setImageStorage('');window.referenceInsert=window.editor.insertImage(" + JsonSerializer.Serialize(new Uri(path).AbsoluteUri) + ");void 0");
                await WaitFor("dialog button[value=reference]");
                await view.Browser.ExecuteScriptAsync("document.querySelector('dialog button[value=reference]').click()");
                await WaitFor("dialog button[value=" + mode + "]");
                await view.Browser.ExecuteScriptAsync("document.querySelector('dialog button[value=" + mode + "]').click()");
                await Evaluate("window.referenceInsert");
                await Evaluate("document.querySelector('#document').contentDocument.images[0].decode()");
                string html = JsonSerializer.Deserialize<string>(await view.Browser.ExecuteScriptAsync("window.editor.html()"))!;
                File.WriteAllText(Path.Combine(folder, mode + "-reference.html"), html);
                check(html.Contains("data-sin-storage=\"reference\"") && !html.Contains("sin-original.local") && !html.Contains("data:image/") &&
                    Core.ImageReferences.LocalPaths(html, view.Document.Path!).SetEquals([path]), "An " + mode + " original-image reference saves a valid path without embedding pixels or runtime mapping URLs");
                check(await view.Browser.ExecuteScriptAsync("(()=>{const img=document.querySelector('#document').contentDocument.images[0];return img.naturalWidth===16&&img.currentSrc.includes('sin-original.local')&&img.getAttribute('src').startsWith(" + JsonSerializer.Serialize(mode == "relative" ? "../" : "file:") + ");})()") == "true",
                    "The sandbox displays an " + mode + " original image outside the document folder");
                await Evaluate("window.editor.load(" + JsonSerializer.Serialize(html) + ")");
                await Evaluate("document.querySelector('#document').contentDocument.images[0].decode()");
                await Evaluate("(()=>{const doc=document.querySelector('#document').contentDocument;doc.images[0].click();doc.dispatchEvent(new Event('selectionchange'));return window.editor.command('cut').then(()=>window.editor.command('paste'));})()");
                check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.images.length===1&&!document.querySelector('dialog[open]')") == "true",
                    "Cut/paste retains the " + mode + " original reference without another storage prompt");
                string destination = Path.Combine(App.Current.Store.DirectoryPath, "Other folder", "Nested", "Saved elsewhere.html");
                string relocated = (await view.PrepareSaveAsAsync(destination))!;
                check(Core.ImageReferences.LocalPaths(relocated, destination).SetEquals([path]) && !relocated.Contains("data:image/") && !Directory.Exists(Path.Combine(Path.GetDirectoryName(destination)!, "Saved elsewhere")),
                    "Save As preserves the " + mode + " original reference without copying an image folder");
                string standalone = await view.ExportAsync();
                check(standalone.Contains("data:image/png;base64,") && !standalone.Contains("sin-original.local") && !standalone.Contains("data-sin-storage=\"reference\""),
                    "Standalone export embeds portable pixels from the " + mode + " original reference");
                string markdown = Path.Combine(App.Current.Store.DirectoryPath, "Reference exports", mode + ".md");
                await view.SaveMarkdownAsync(markdown);
                check(File.ReadAllText(markdown).Contains("![") && Directory.GetFiles(Path.Combine(Path.GetDirectoryName(markdown)!, mode), "*.png").Length == 1,
                    "Markdown export writes an independent PNG from the " + mode + " original reference");
            }
            await view.Browser.ExecuteScriptAsync("window.editor.setImageStorage('inline');window.referenceEdit=window.editor.openAnnotation(document.querySelector('#document').contentDocument.images[0]);void 0");
            await WaitFor("dialog.annotation [data-action=apply]");
            await view.Browser.ExecuteScriptAsync("document.querySelector('dialog.annotation [data-action=apply]').click()");
            await Evaluate("window.referenceEdit");
            check(File.ReadAllBytes(path).SequenceEqual(originalBytes) && await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.images[0].dataset.sinStorage==='inline'") == "true",
                "Annotating a referenced image creates an independent edited image and leaves the original file unchanged");
        }
        finally
        {
            EditorClipboard.TestData = clipboard;
            await view.Browser.ExecuteScriptAsync("window.editor.setImageStorage(" + JsonSerializer.Serialize(App.Current.Preferences.ImageStorage) + ")");
            await Evaluate("window.editor.load(" + JsonSerializer.Serialize(original) + ")"); view.AcceptHtml(original);
        }
    }
}
