using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentContentSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!; var doc = view.Document;
        string original = doc.Text;
        bool explorer = App.Current.Preferences.ExplorerMode, content = App.Current.Preferences.ContentView;
        try
        {
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "window.editor.load('<h1>Outline heading</h1><h2>Outline detail</h2>')", awaitPromise = true }));
            await view.FlushAsync();
            window.Explorer.SetMode(false);
            window.ContentViewMenu.IsChecked = true; window.ContentViewMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await window.ContentsView.UpdateAsync();
            check(window.ContentsView.Visibility == Visibility.Visible && window.DocumentList.IsVisible && window.ContentsView.Items.Count == 2,
                "Content View displays the active heading list in a separate pane alongside Document List");
            window.Explorer.SetMode(true);
            check(window.ContentsView.IsVisible && window.Explorer.Tree.IsVisible && !window.DocumentList.IsVisible,
                "Switching to Prompt Explorer leaves the independent Content View visible");
            window.SetDocumentList(false); window.UpdateLayout(); await Task.Delay(60);
            check(window.ContentsView.IsVisible && await view.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left===220") == "true",
                "Content View remains visible and reserves its own width when Navigation Pane is hidden");
            window.SetDocumentList(true); window.Explorer.SetMode(false);
            double top = window.DocumentPane.Margin.Top;
            check(top > 50 && Math.Abs(view.ActualWidth - window.Workspace.ActualWidth) < 2 && await view.Browser.ExecuteScriptAsync("document.querySelector('#document').getBoundingClientRect().left>100") == "true",
                "The ribbon spans the workspace while native navigation starts beneath it");
            await window.SaveDocument(doc);
            await DocumentLock.ChangeAsync(window, doc, true, () => window.SaveDocument(doc));
            await Task.Delay(80);
            check(DocumentAccess.IsReadOnly(doc.Path) && TextFiles.Open(doc.Path!).IsReadOnly && window.Title.Contains("[Read-Only]"),
                "Lock persists the Windows read-only attribute and title indicator, and reopening reads it");
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
        }
    }
}
