using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentContentSelfTest
{
    internal static async Task DateHeading(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!; var document = view.Document;
        await view.FlushAsync();
        string original = document.Text;
        int format = App.Current.Preferences.DateTimeFormat;
        var now = new DateTime(2026, 10, 11, 17, 37, 0);
        string expected = JsonSerializer.Serialize(DateTimeFormats.Format(now, 0));
        async Task Load(string html, int offset = 0)
        {
            string expression = $"(async()=>{{await window.editor.ready();await window.editor.load({JsonSerializer.Serialize(html)});const d=document.querySelector('#document').contentDocument,p=d.querySelector('#date');d.body.focus();d.getSelection().collapse(p.firstChild,{offset});d.dispatchEvent(new Event('selectionchange'));}})()";
            string response = await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, awaitPromise = true }));
            using var result = JsonDocument.Parse(response);
            if (result.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException("Date-heading fixture load failed: " + error);
        }
        async Task<bool> Is(string expression)
        {
            bool success = await view.Browser.ExecuteScriptAsync($"(()=>{{const d=document.querySelector('#document').contentDocument;return {expression};}})()") == "true";
            if (!success)
            {
                string state = await view.Browser.ExecuteScriptAsync("(()=>{const d=document.querySelector('#document').contentDocument,s=d.getSelection();return {html:d.body.innerHTML,selection:s.anchorNode?.parentElement?.outerHTML,offset:s.anchorOffset,notice:document.querySelector('#notice')?.textContent,editable:d.body.isContentEditable};})()");
                File.WriteAllText(Path.Combine(App.Current.Store.DirectoryPath, "date-heading-diagnostic.json"), JsonSerializer.Serialize(new { expression, state }));
            }
            return success;
        }
        try
        {
            App.Current.Preferences.DateTimeFormat = 4;
            await Load("<p id='before'>Earlier</p><p id='date'><br></p><p id='after'>Later</p>");
            check(window.TryInsertShortcut(Key.D, ModifierKeys.Control, now), "Ctrl+D is handled by the existing native date shortcut");
            await view.FlushAsync();
            check(await Is($"d.querySelector('#date').dataset.sinStyle==='heading2'&&d.querySelector('#date').textContent==={expected}&&d.querySelector('#date').getAttribute('aria-level')==='2'"),
                "Ctrl+D preserves the long date format and applies the Heading2 paragraph style");
            check(await Is("d.querySelector('#date').nextElementSibling.dataset.sinStyle==='normal'&&d.querySelector('#date').nextElementSibling.textContent===''&&d.querySelector('#date').nextElementSibling.contains(d.getSelection().anchorNode)&&d.querySelector('#before').textContent==='Earlier'&&d.querySelector('#after').textContent==='Later'"),
                "Ctrl+D creates a Normal paragraph at the caret while preserving surrounding paragraphs");
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText", "{\"text\":\"Continue here\"}");
            check(await Is("(()=>{const p=d.querySelector('#date').nextElementSibling,s=d.defaultView.getComputedStyle(p);return p.textContent==='Continue here'&&s.fontSize==='16px'&&s.fontWeight==='400'&&s.borderBottomStyle==='none';})()"),
                "Actual typing after Ctrl+D stays in the new Normal paragraph with its regular font and no heading border");
            check(await Is($"window.editor.outline.items().some(item=>item.text==={expected}&&item.level===2)") && App.Current.Preferences.DateTimeFormat == 4,
                "Ctrl+D adds its date to the Heading2 outline without changing the format remembered by F5");
            await view.Browser.ExecuteScriptAsync("for(let i=0;i<8;i++)window.editor.command('undo')");
            check(await Is("d.body.textContent==='EarlierLater'&&!d.querySelector('[data-sin-style]')"), "Undo can restore the document before the date heading and following typing");

            await Load("<p id='date'>Before after</p>", 7);
            window.TryInsertShortcut(Key.D, ModifierKeys.Control, now);
            await view.FlushAsync();
            check(await Is($"d.body.firstElementChild.textContent==='Before '+{expected}&&d.body.lastElementChild.textContent==='after'&&d.body.lastElementChild.dataset.sinStyle==='normal'"),
                "Ctrl+D in the middle of a paragraph moves the following text into Normal without losing text");
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText", "{\"text\":\"New \"}");
            check(await Is("d.body.lastElementChild.textContent==='New after'"), "Ctrl+D keeps the caret before existing text in the following Normal paragraph");

            await Load("<p id='date'><br></p>");
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\",\"key\":\"d\",\"code\":\"KeyD\",\"windowsVirtualKeyCode\":68,\"modifiers\":2}");
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\",\"key\":\"d\",\"code\":\"KeyD\",\"windowsVirtualKeyCode\":68,\"modifiers\":2}");
            bool inserted = false;
            for (int i = 0; i < 50 && !inserted; i++) { inserted = await Is("!!d.querySelector('[data-sin-style=heading2]')&&d.body.lastElementChild.dataset.sinStyle==='normal'"); if (!inserted) await Task.Delay(20); }
            check(inserted, "A real browser Ctrl+D key travels through the native bridge and inserts Heading2 followed by Normal");
            await view.FlushAsync(); string locked = document.Text;
            document.IsReadOnly = true; document.Notify();
            window.TryInsertShortcut(Key.D, ModifierKeys.Control, now);
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertDateHeading','Forbidden')"); await view.FlushAsync();
            check(document.Text == locked, "Locked documents reject both native and browser date-heading commands");
            document.IsReadOnly = false; document.Notify();

            await Load("<p id='date'><br></p>");
            window.InsertDate(now: now);
            await view.FlushAsync();
            check(await Is("d.body.children.length===1&&!d.querySelector('[data-sin-style]')&&d.body.textContent==='2026-10-11 1737'"), "F5's existing insertion keeps the remembered date format and plain paragraph behavior");
            await view.SetSourceAsync(true); view.Editor.Select(0, view.Editor.Text.Length);
            window.TryInsertShortcut(Key.D, ModifierKeys.Control, now);
            check(view.Editor.Text == DateTimeFormats.Format(now, 0), "Source-mode Ctrl+D still inserts the existing plain date text without HTML markup");
        }
        finally
        {
            document.IsReadOnly = false; document.Notify(); App.Current.Preferences.DateTimeFormat = format;
            await view.SetSourceAsync(false); view.AcceptHtml(original); view.ReloadSavedHtml();
        }
    }

    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!; var doc = view.Document;
        string original = doc.Text;
        bool explorer = App.Current.Preferences.ExplorerMode, content = App.Current.Preferences.ContentView;
        double contentWidth = App.Current.Preferences.ContentWidth;
        try
        {
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "window.editor.load('<h1>Outline heading</h1><h2>Outline detail</h2>')", awaitPromise = true }));
            await view.FlushAsync();
            window.Explorer.SetMode(false);
            window.ContentViewMenu.IsChecked = true; window.ContentViewMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await window.ContentsView.UpdateAsync();
            check(window.ContentsView.Visibility == Visibility.Visible && window.DocumentList.IsVisible && window.ContentsView.Items.Count == 2,
                $"Content View displays the active heading list in a separate pane alongside Document List (pane={window.ContentsView.Visibility}, navigation={window.DocumentList.IsVisible}, headings={window.ContentsView.Items.Count})");
            window.Explorer.SetMode(true);
            check(window.ContentsView.IsVisible && window.Explorer.Tree.IsVisible && !window.DocumentList.IsVisible,
                "Switching to Prompt Explorer leaves the independent Content View visible");
            window.SetDocumentList(false); window.UpdateLayout(); await Task.Delay(60);
            check(window.ContentsView.IsVisible && await view.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left===220") == "true",
                "Content View remains visible and reserves its own width when Navigation Pane is hidden");
            var splitter = window.ContentsView.Children.OfType<Thumb>().Single();
            splitter.RaiseEvent(new DragDeltaEventArgs(120, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            window.UpdateLayout(); await Task.Delay(60);
            check(Math.Abs(window.ContentsView.ActualWidth - 340) < 1 && App.Current.Preferences.ContentWidth == 340 &&
                await view.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left===340") == "true",
                "Dragging the Content View splitter expands the pane and updates the browser document inset immediately");
            window.ContentsView.SetVisible(false); window.ContentsView.SetVisible(true); window.UpdateLayout();
            var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(App.Current.Preferences))!;
            check(window.ContentsView.ActualWidth == 340 && restored.ContentWidth == 340,
                "Content View remembers its resized width across hide/show and saved preferences");
            var heading = (TextBlock)((ListBoxItem)window.ContentsView.Items[1]).Content;
            heading.Text = "A long outline heading remains readable by wrapping within the resized Content View instead of being cut off";
            window.UpdateLayout();
            check(heading.ActualHeight > 30 && heading.ActualWidth < window.ContentsView.ActualWidth,
                "Long Content View headings wrap within the available pane width");
            splitter.RaiseEvent(new DragDeltaEventArgs(10000, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            window.UpdateLayout(); await Task.Delay(60);
            check(window.Workspace.ActualWidth - window.ContentsView.ActualWidth >= 240,
                "Content View resizing reserves room for the document editor");
            window.ContentsView.Width = 340;
            window.SetDocumentList(true); window.Explorer.SetMode(false);
            double top = window.DocumentPane.Margin.Top;
            check(top > 50 && Math.Abs(view.ActualWidth - window.Workspace.ActualWidth) < 2 && await view.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left>100") == "true",
                "The ribbon spans the workspace while native navigation starts beneath it");
            await window.SaveDocument(doc);
            var lockMenu = window.CreateDocumentMenu(doc).Items.OfType<MenuItem>().Single(item => item.Header.ToString() == "_Lock Document");
            lockMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (int i = 0; i < 100 && !doc.IsReadOnly; i++) await Task.Delay(20);
            await Task.Delay(80);
            check(DocumentAccess.IsReadOnly(doc.Path) && TextFiles.Open(doc.Path!).IsReadOnly && window.Title.Contains("[Read-Only]"),
                "The shared document/tab context menu locks the file, updates the title, and preserves the read-only attribute on reopen");
            check(window.CreateDocumentMenu(doc).Items.OfType<MenuItem>().Single(item => item.Header.ToString() == "_Unlock Document").IsEnabled,
                "A locked document or tab offers Unlock in its shared context menu");
            string locked = doc.Text;
            await window.ContentsView.UpdateAsync();
            check(window.ContentsView.Items.Count == 2 && window.ContentsView.Items.Cast<ListBoxItem>().All(item => item.IsEnabled),
                "Locked HTML documents retain their readable Content View headings");
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Forbidden')"); await view.FlushAsync();
            check(doc.Text == locked && view.Editor.IsReadOnly && await view.Browser.ExecuteScriptAsync("!document.querySelector('#document').contentDocument.body.isContentEditable") == "true",
                "File locking protects both visual and source editor surfaces");
            await view.SetSourceAsync(true);
            check((await view.SearchAsync(new("Outline", Action: "replaceAll", Replacement: "Changed"))).Message!.Contains("read-only") && doc.Text == locked,
                "Read-only source documents reject Replace All");
            DocumentAccess.Set(doc, false); await view.SetSourceAsync(false);
            string folder = Path.Combine(App.Current.Store.DirectoryPath, "Cleanup fixture"); Directory.CreateDirectory(folder);
            string parent = Path.Combine(folder, "Parent.html"), assets = Path.Combine(folder, "Parent"); Directory.CreateDirectory(assets);
            string used = Path.Combine(assets, "used.png"), draft = Path.Combine(assets, "draft.png"), unused = Path.Combine(assets, "unused.png");
            foreach (string path in new[] { used, draft, unused }) File.WriteAllBytes(path, [1, 2, 3]);
            File.WriteAllText(parent, "<img src='Parent/used.png'>");
            var candidates = await Task.Run(() => UnusedImages.Scan(folder, [(Path.Combine(folder, "Draft.html"), "<img src='Parent/draft.png'>")]));
            check(candidates.SequenceEqual([unused]), "Unused-image scanning protects references from disk and unsaved open document snapshots");
            File.WriteAllText(parent, "<img src='Parent/used.png'><img src='Parent/unused.png'>");
            check(!(await Task.Run(() => UnusedImages.Scan(folder, []))).Contains(unused), "A fresh cleanup scan protects an image referenced after the first review");
        }
        finally
        {
            if (DocumentAccess.IsReadOnly(doc.Path)) DocumentAccess.Set(doc, false);
            await view.SetSourceAsync(false); view.AcceptHtml(original); view.ReloadSavedHtml();
            window.Explorer.SetMode(explorer); window.ContentsView.SetVisible(content);
            App.Current.Preferences.ContentWidth = contentWidth; window.ContentsView.Width = contentWidth;
        }
    }
}
