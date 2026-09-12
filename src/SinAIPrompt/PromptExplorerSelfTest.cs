using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class PromptExplorerSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!;
        var explorer = (PromptExplorer)window.FindName("Explorer");
        var settings = App.Current.Preferences;
        string oldRoot = settings.ExplorerDirectory;
        bool oldMode = settings.ExplorerMode, oldFolders = settings.ExplorerShowFolders, oldVisible = window.IsDocumentList;
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "explorer-fixture");
        Directory.CreateDirectory(folder);
        string parent = Path.Combine(folder, "Prompt #1.html"), assets = Path.Combine(folder, "Prompt #1");
        Directory.CreateDirectory(assets); Directory.CreateDirectory(Path.Combine(folder, "Normal folder"));
        Directory.CreateDirectory(Path.Combine(folder, "Hidden folder"));
        File.SetAttributes(Path.Combine(folder, "Hidden folder"), FileAttributes.Directory | FileAttributes.Hidden);
        string imagePath = Path.Combine(assets, "image #1.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16).ToArray(), 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(imagePath)) encoder.Save(file);
        string reference = "Prompt%20%231/image%20%231.png";
        string initial = $"<p>Saved content</p><img src=\"{reference}\"><img src=\"{reference}?v=1#pixel\"><p>{reference}</p><img src=\"https://example.invalid/{reference}\">";
        File.WriteAllText(parent, initial);
        File.WriteAllText(Path.Combine(folder, "Older.html"), "<p>Older</p>");
        File.SetLastWriteTimeUtc(Path.Combine(folder, "Older.html"), DateTime.UtcNow.AddDays(-1));
        foreach (string name in new[] { "notes.txt", "notes.md", "picture.jpg", "animation.gif", "reference.pdf", "ignore.exe", "ignore.json", "hidden.html", "system.html" }) File.WriteAllText(Path.Combine(folder, name), "Preview text");
        File.SetAttributes(Path.Combine(folder, "hidden.html"), FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(folder, "system.html"), FileAttributes.System);
        Document? opened = null;
        try
        {
            var entries = PromptDirectory.Read(folder, true, "newest");
            check(entries.Count == 8 && entries[0].Name == "Normal folder" && !entries.Any(e => e.Name is "Prompt #1" or "ignore.exe" or "hidden.html" or "system.html" or "Hidden folder"),
                "Explorer filters extensions, hidden/system entries and supporting image folders");
            check(entries.Where(e => e.IsHtml).First().Path == parent && entries.Single(e => e.Path == parent).ImageFolder == assets,
                "Explorer sorts prompts newest first and associates their image folder");
            check(!PromptDirectory.Read(folder, false, "newest").Any(e => e.IsFolder), "Hide folders retains prompt files");
            check(PromptDirectory.Read(folder, false, "manual", manualOrder: [Path.Combine(folder, "Older.html"), parent]).Take(2).Select(e => e.Name).SequenceEqual(["Older.html", "Prompt #1.html"]),
                "Explorer honors the existing manual document order ahead of other files");
            check(PromptDirectory.Read(assets, true, "newest").Single().ParentHtml == parent, "Opening an image folder directly retains its parent HTML ownership");
            check(new Settings().ExplorerMode, "Prompt Explorer is the default navigation mode");
            window.SetDocumentList(true); explorer.SetMode(true); settings.ExplorerShowFolders = true;
            await explorer.SetFolderAsync(folder);
            int editors = window.CreatedEditorCount, documents = window.Documents.Count;
            check(explorer.Entries.Count == 8 && window.CreatedEditorCount == editors && window.Documents.Count == documents,
                "Listing a working folder creates no documents or editors");
            var tree = (TreeView)explorer.FindName("Tree");
            var htmlRow = tree.Items.Cast<TreeViewItem>().Single(r => ((PromptEntry)r.Tag).Path == parent);
            await explorer.ExpandAsync(htmlRow); htmlRow.IsExpanded = true;
            var assetRow = (TreeViewItem)htmlRow.Items[0]; await explorer.ExpandAsync(assetRow); assetRow.IsExpanded = true;
            check(((PromptEntry)assetRow.Tag).Path == assets && ((PromptEntry)((TreeViewItem)assetRow.Items[0]).Tag).Path == imagePath,
                "Expanding HTML reveals its indented image folder and image children");
            check(!((PromptEntry)((TreeViewItem)assetRow.Items[0]).Tag).IsUnused, "Referenced image files are not marked unused");
            var unusedSnapshot = PromptDirectory.Read(assets, true, "newest", parent, openHtml: new Dictionary<string, string> { [parent] = "<p>Cut image; not saved yet</p>" });
            check(unusedSnapshot.Single().IsUnused, "Unused image detection uses current unsaved document contents");
            var baseReferences = ImageReferences.LocalPaths($"<base href='Prompt%20%231/'><img src='image%20%231.png?v=1#test'><script>var x=\"<img src='fake.png'>\"</script>", parent);
            check(baseReferences.SetEquals([imagePath]), "Usage detection handles encoded paths, explicit bases and suffixes while ignoring scripts");
            window.UpdateLayout(); assetRow.IsSelected = true;
            check(window.CurrentView!.PathStatus.Text == assets, "Folder navigation displays its path in the muted status bar: " + window.CurrentView.PathStatus.Text);
            await window.OpenExplorerFile(imagePath, CancellationToken.None);
            var preview = (ExplorerPreview)((EditorSurface)window.FindName("EditorHost")).Content!;
            check(window.CurrentView == null && window.Documents.Count == documents && preview.PathStatus.Text == imagePath &&
                ScreenCaptureSelfTest.Controls(preview).OfType<Image>().Any(i => i.Source != null), "Images render in a separate read-only document-area preview");
            await window.OpenExplorerFile(Path.Combine(folder, "notes.md"), CancellationToken.None);
            preview = (ExplorerPreview)((EditorSurface)window.FindName("EditorHost")).Content!;
            var markdownBrowser = ScreenCaptureSelfTest.Controls(preview).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Single();
            await MarkdownImportSelfTest.WaitFor(markdownBrowser, "document.querySelector('#preview')?.contentDocument?.body?.textContent==='Preview text'");
            check(await markdownBrowser.ExecuteScriptAsync("!document.querySelector('#preview').contentDocument.body.isContentEditable") == "true" && window.Documents.Count == documents,
                "Markdown previews render read-only HTML without replacing the underlying document");
            string pdf = Path.Combine(folder, "reference.pdf"); WritePdf(pdf);
            await window.OpenExplorerFile(pdf, CancellationToken.None);
            preview = (ExplorerPreview)((EditorSurface)window.FindName("EditorHost")).Content!;
            var pdfBrowser = ScreenCaptureSelfTest.Controls(preview).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Single();
            for (int i = 0; i < 100; i++)
            {
                if (await pdfBrowser.ExecuteScriptAsync("!!document.querySelector('embed[type=\"application/pdf\"]')") == "true") break;
                await Task.Delay(30);
            }
            check(await pdfBrowser.ExecuteScriptAsync("!!document.querySelector('embed[type=\"application/pdf\"]')") == "true" && window.Documents.Count == documents,
                "PDF uses the local built-in viewer without becoming an editable HTML document");
            await Task.Delay(1200);
            using (var output = File.Create(Path.Combine(App.Current.Store.DirectoryPath, "explorer-pdf.png")))
                await pdfBrowser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, output);
            window.ActiveDocument = original;
            check(window.CurrentView != null && window.CreatedEditorCount == editors, "Returning from preview reuses the existing editor");
            await window.OpenExplorerFile(parent, CancellationToken.None); opened = window.ActiveDocument!;
            var view = window.CurrentView!;
            for (int i = 0; i < 150 && view.Browser.CoreWebView2 == null; i++) await Task.Delay(20);
            await view.RenameImageFileAsync(initial, parent, imagePath, imagePath); // Wait for the editor bridge to be ready.
            await view.Browser.CoreWebView2!.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"window.editor.ready()\",\"awaitPromise\":true}");
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Unsaved Explorer edit')");
            await window.RenameExplorerImage(new(imagePath, false, parent), "renamed #2.png");
            string renamed = Path.Combine(assets, "renamed #2.png"), updatedReference = "Prompt%20%231/renamed%20%232.png";
            string saved = File.ReadAllText(parent);
            check(File.Exists(renamed) && !File.Exists(imagePath) && saved.Contains(updatedReference + "?v=1#pixel") && opened.Text.Contains(updatedReference),
                "Image rename updates encoded references in saved and open parent HTML");
            check(opened.Dirty && opened.Text.Contains("Unsaved Explorer edit") && !saved.Contains("Unsaved Explorer edit") && saved.Contains("<p>" + reference + "</p>") && saved.Contains("https://example.invalid/" + reference),
                "Image rename preserves unsaved edits, ordinary text and unrelated external URLs");
            check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.querySelector('img').getAttribute('src').includes('renamed%20%232.png')") == "true",
                "Renamed image appears immediately in the live parent document");
            string absolute = new Uri(renamed).AbsoluteUri;
            string adjusted = await view.RenameImageFileAsync($"<img src=\"{absolute}?v=1\"><base href=\"Prompt%20%231/\"><img src=\"renamed%20%232.png\">", parent, renamed, Path.Combine(assets, "next.png"));
            check(adjusted.Contains("next.png?v=1") && adjusted.Contains("src=\"next.png\""), "Image reference rewriting handles absolute URLs and explicit local base elements");
            string occupied = Path.Combine(assets, "occupied.png"); File.Copy(renamed, occupied);
            bool collision = false; try { await window.RenameExplorerImage(new(renamed, false, parent), "occupied.png"); } catch (IOException) { collision = true; }
            check(collision && File.Exists(renamed), "Image rename rejects collisions without moving the original");
            File.SetAttributes(parent, FileAttributes.ReadOnly);
            bool rollback = false;
            try { await window.RenameExplorerImage(new(renamed, false, parent), "rollback.png"); } catch (UnauthorizedAccessException) { rollback = true; } catch (IOException) { rollback = true; }
            finally { File.SetAttributes(parent, FileAttributes.Normal); }
            check(rollback && File.Exists(renamed) && !File.Exists(Path.Combine(assets, "rollback.png")) && File.ReadAllText(parent) == saved,
                "Failed parent save rolls the image rename back");
            await explorer.RefreshAsync();
            htmlRow = tree.Items.Cast<TreeViewItem>().Single(r => ((PromptEntry)r.Tag).Path == parent);
            check(htmlRow.IsExpanded && ((TreeViewItem)htmlRow.Items[0]).IsExpanded, "Explorer refresh preserves expanded branches");
            assetRow = (TreeViewItem)htmlRow.Items[0];
            var unusedRow = assetRow.Items.Cast<TreeViewItem>().Single(r => ((PromptEntry)r.Tag).Path == occupied);
            check(((PromptEntry)unusedRow.Tag).IsUnused && ((StackPanel)unusedRow.Header).Children.OfType<TextBlock>().Single().Foreground == Brushes.Red,
                "Prompt Explorer renders an unreferenced image filename in red");
            string liveHtml = opened.Text; opened.Text = "<p>Image removed without saving</p>";
            await explorer.RefreshAsync();
            htmlRow = tree.Items.Cast<TreeViewItem>().Single(r => ((PromptEntry)r.Tag).Path == parent);
            check(((TreeViewItem)htmlRow.Items[0]).Items.Cast<TreeViewItem>().All(r => ((PromptEntry)r.Tag).IsUnused),
                "Explorer updates image usage from an unsaved open document without creating editors");
            opened.Text = liveHtml; await explorer.RefreshAsync();
            await FileActions(window, explorer, folder, check);
            explorer.SetMode(false);
            check(((ListBox)window.FindName("DocumentList")).IsVisible && !tree.IsVisible && window.Documents.Contains(original), "Mode toggle restores the original open Document List");
            explorer.SetMode(true); window.UpdateLayout(); await Task.Delay(100);
            var origin = explorer.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(explorer);
            var capture = ScreenCapture.Capture(new Int32Rect((int)origin.X, (int)origin.Y, (int)(explorer.ActualWidth * dpi.DpiScaleX), (int)(explorer.ActualHeight * dpi.DpiScaleY)));
            File.WriteAllBytes(Path.Combine(App.Current.Store.DirectoryPath, "prompt-explorer.png"), Convert.FromBase64String(ScreenCapture.Png(capture).Split(',')[1]));
        }
        finally
        {
            window.ActiveDocument = original;
            if (opened != null) window.RemoveDocument(opened);
            explorer.SetMode(oldMode); settings.ExplorerShowFolders = oldFolders;
            await explorer.SetFolderAsync(oldRoot); window.SetDocumentList(oldVisible);
        }
    }

    static async Task FileActions(MainWindow window, PromptExplorer explorer, string folder, Action<bool, string> check)
    {
        string path = Path.Combine(folder, "Recycle # fixture.html"), assets = Path.Combine(folder, "Recycle # fixture");
        Directory.CreateDirectory(assets);
        string image = Path.Combine(assets, "used.png"); File.Copy(Path.Combine(folder, "Prompt #1", "renamed #2.png"), image);
        string reference = "Recycle%20%23%20fixture/used.png";
        File.WriteAllText(path, $"<img src='{reference}'>");
        var assetEntry = new PromptEntry(assets, true, path);
        var items = explorer.CreateFileMenu(new(path, false)).Items.OfType<MenuItem>().Select(i => i.Header.ToString()).ToArray();
        check(items.SequenceEqual(["Rename…", "Delete to Recycle Bin…", "Show in File Explorer", "Open Containing Folder"]), "Explorer file menu exposes rename, recycle and both Windows Explorer actions");
        check(explorer.CreateFileMenu(assetEntry).Items.OfType<MenuItem>().Any(i => i.Header.ToString() == "Delete to Recycle Bin…") &&
            !explorer.CreateFileMenu(new(folder, true)).Items.OfType<MenuItem>().Any(i => i.Header.ToString() == "Delete to Recycle Bin…"), "Folder deletion is offered only within a document's asset tree");
        check(ExplorerFileOperations.LocationCommand(path, true).Arguments == $"/select,\"{path}\"" &&
            ExplorerFileOperations.LocationCommand(path, false).Arguments == $"\"{folder}\"", "Show in Explorer selects the file; Open Containing Folder opens its parent");
        foreach (string html in new[] { $"<img src='{reference}'>", $"<div style=\"background:url('{reference}')\"></div>", $"<a href='{reference}'>Image</a>", $"<style>p{{background:url('{reference}')}}</style>" })
        {
            bool blocked = false;
            try { ExplorerFileOperations.RequireUnusedFolder(assetEntry, [(path, html)]); } catch (IOException) { blocked = true; }
            check(blocked && File.Exists(image), "Valid image, CSS and link references block recycling the asset folder");
        }
        bool outside = false;
        try { ExplorerFileOperations.RequireUnusedFolder(new(folder, true, path), [(path, "")]); } catch (IOException) { outside = true; }
        check(outside, "Folder deletion rejects a target outside the document asset directory");
        check(!await window.DeleteExplorerEntry(assetEntry, (_, _) => false) && Directory.Exists(assets), "Cancel leaves the folder and files intact");
        var previous = window.ActiveDocument!;
        window.OpenPaths([path]); var document = window.ActiveDocument!; var view = window.CurrentView!;
        try
        {
            await view.ExportAsync();
            bool blocked = false;
            try { await window.DeleteExplorerEntry(assetEntry, (_, _) => true); } catch (IOException) { blocked = true; }
            check(blocked && Directory.Exists(assets), "Live valid references prevent actual folder recycling");
            await view.Browser.ExecuteScriptAsync("window.editor.command('selectAll');window.editor.command('insertHTML'," + JsonSerializer.Serialize("<p>Removed image</p><img src='Recycle%20%23%20fixture/missing.png'>") + ")");
            check(await window.DeleteExplorerEntry(assetEntry, (_, _) => true) && !Directory.Exists(assets) && File.Exists(path),
                "Removing the last valid live reference allows recycling the folder; broken references do not block it and parent HTML remains");
            bool dirtyWarning = false;
            check(!await window.DeleteExplorerEntry(new(path, false), (_, dirty) => { dirtyWarning = dirty; return false; }) && dirtyWarning && window.Documents.Contains(document),
                "Cancelling file deletion preserves an unsaved open document and reports its dirty state");
            check(await window.DeleteExplorerEntry(new(path, false), (_, _) => true) && !File.Exists(path) && !window.Documents.Contains(document),
                "Confirmed Explorer deletion recycles an open file and closes its document");
            string text = Path.Combine(folder, "recycle-preview.txt"); File.WriteAllText(text, "Preview fixture");
            await window.OpenExplorerFile(text, CancellationToken.None);
            check(await window.DeleteExplorerEntry(new(text, false), (_, _) => true) && !File.Exists(text) && window.CurrentView != null,
                "Deleting a previewed file closes its preview and returns to the editor");
        }
        finally { window.ActiveDocument = previous; if (window.Documents.Contains(document)) window.RemoveDocument(document); }
    }

    static void WritePdf(string path)
    {
        string stream = "BT /F1 24 Tf 40 150 Td (Local PDF preview) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 320 220] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream"];
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = pdf.Length; pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); File.WriteAllText(path, pdf.ToString(), System.Text.Encoding.ASCII);
    }
}
