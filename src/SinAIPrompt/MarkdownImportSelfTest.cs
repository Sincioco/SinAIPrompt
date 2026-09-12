using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class MarkdownImportSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!; var view = window.CurrentView!;
        string saved = original.Text, previousFolder = App.Current.Preferences.ExplorerDirectory;
        bool mode = window.Explorer.ExplorerMode, visible = window.IsDocumentList;
        var documents = window.Documents.ToArray();
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "markdown-import"); Directory.CreateDirectory(folder);
        string markdown = Path.Combine(folder, "ReadMe.md"), htmlPath = Path.Combine(folder, "Drop.html");
        string source = "# Markdown fixture\n\n**Bold text** and [a link](Drop.html).\n\n## Detail\n\n![Local image](pixel.png)\n\n<script>window.untrustedRan=true</script>";
        File.WriteAllText(markdown, source); File.WriteAllText(htmlPath, "<h1>Dropped HTML</h1>");
        File.WriteAllBytes(Path.Combine(folder, "pixel.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
        try
        {
            await window.OpenPathsAsync([markdown]);
            string converted = Path.Combine(folder, "ReadMe - Converted.html");
            check(window.ActiveDocument!.Path == converted && File.Exists(converted) && File.ReadAllText(markdown) == source,
                "File/Open converts Markdown into a sibling Name - Converted.html and preserves the source");
            string first = File.ReadAllText(converted);
            check(first.Contains("data-sin-style-mode=\"modern\"") && first.Contains("<h1>Markdown fixture</h1>") && first.Contains("<strong>Bold text</strong>") && first.Contains("src=\"pixel.png\""),
                "Converted HTML has Modern styling, Markdown formatting and relative local-image references");
            await window.OpenPathsAsync([markdown]);
            check(window.ActiveDocument!.Path == Path.Combine(folder, "ReadMe - Converted (2).html") && File.ReadAllText(converted) == first,
                "Repeated conversion chooses a new filename without overwriting an earlier HTML document");
            window.ActiveDocument = original;
            int count = window.Documents.Count;
            window.SetDocumentList(true); window.Explorer.SetMode(true); await window.Explorer.SetFolderAsync(folder);
            window.UpdateLayout();
            var row = window.Explorer.Tree.Items.Cast<TreeViewItem>().Single(item => ((PromptEntry)item.Tag).Path == markdown);
            row.IsSelected = true;
            var preview = await Preview(window);
            await WaitFor(preview, "document.querySelector('#preview')?.contentDocument?.images[0]?.naturalWidth===1");
            check(await preview.ExecuteScriptAsync("(()=>{const f=document.querySelector('#preview'),d=f.contentDocument;return !d.body.isContentEditable&&d.documentElement.dataset.sinStyleMode==='modern'&&d.defaultView.getComputedStyle(d.body).fontFamily.includes('Segoe UI')&&f.sandbox.value==='allow-same-origin'&&!d.defaultView.untrustedRan})()") == "true" && window.Documents.Count == count,
                "Clicking Markdown in Explorer shows Modern read-only HTML with local images and sandboxed content");
            window.Explorer.SetMode(false); window.DocumentList.ScrollIntoView(original); window.UpdateLayout();
            var originalRow = (ListBoxItem)window.DocumentList.ItemContainerGenerator.ContainerFromItem(original);
            originalRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(window.CurrentView == view, "Clicking the already-selected HTML document leaves the Markdown preview");
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Saved without reopening Markdown')");
            await window.SaveDocument(original); await window.Explorer.RefreshAsync(); await Task.Delay(650);
            check(window.CurrentView == view && window.ActiveDocument == original && window.Title.Contains(original.Name) && File.ReadAllText(original.Path!).Contains("Saved without reopening Markdown"),
                "Saving HTML after leaving a Markdown preview stays on the HTML document through Explorer refresh");
            window.Explorer.SetMode(true); await window.Explorer.RefreshAsync(); await Task.Delay(650);
            check(window.CurrentView == view, "Restoring Explorer's selection after refresh never reopens an old preview");
            await Drop(view.Browser, [markdown], 600, 300);
            preview = await Preview(window);
            await WaitFor(preview, "document.querySelector('#preview')?.contentDocument?.querySelector('h1')?.textContent==='Markdown fixture'");
            check(window.Documents.Count == count && Directory.GetFiles(folder, "*Converted*.html").Length == 2 && File.ReadAllText(markdown) == source,
                "Dropping Markdown onto the editor shows a read-only Modern preview without creating a converted file");
            // The drop disposes this preview. Its DevTools reply may never arrive;
            // the observable result is the newly opened HTML editor below.
            _ = Drop(preview, [htmlPath], 120, 120);
            for (int attempt = 0; attempt < 200 && window.ActiveDocument?.Path != htmlPath; attempt++) await Task.Delay(20);
            check(window.ActiveDocument?.Path == htmlPath && window.CurrentView != null,
                "Dropping HTML onto a Markdown preview opens the editable HTML document");
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in window.Documents.Except(documents).ToArray()) window.RemoveDocument(document);
            await view.Browser.ExecuteScriptAsync("window.editor.load(" + JsonSerializer.Serialize(saved) + ")"); view.AcceptHtml(saved);
            if (previousFolder.Length > 0) await window.Explorer.SetFolderAsync(previousFolder);
            window.Explorer.SetMode(mode); window.SetDocumentList(visible);
        }
    }
    static async Task<WebView2> Preview(MainWindow window)
    {
        for (int attempt = 0; attempt < 250; attempt++)
        {
            if (window.EditorHost.Content is ExplorerPreview preview && ScreenCaptureSelfTest.Controls(preview).OfType<WebView2>().FirstOrDefault() is { CoreWebView2: not null } browser) return browser;
            await Task.Delay(20);
        }
        throw new TimeoutException("Markdown preview did not open.");
    }
    internal static async Task WaitFor(WebView2 browser, string expression)
    {
        for (int attempt = 0; attempt < 250; attempt++)
        {
            if (await browser.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Preview did not render: " + expression);
    }
    static async Task Drop(WebView2 browser, string[] files, int x, int y)
    {
        foreach (string type in new[] { "dragEnter", "dragOver", "drop" })
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent", JsonSerializer.Serialize(new { type, x, y, data = new { items = Array.Empty<object>(), files, dragOperationsMask = 1 } }));
    }
}
