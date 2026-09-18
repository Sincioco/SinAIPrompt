using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentPrivacySelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!;
        var settings = App.Current.Preferences;
        var explorer = window.Explorer;
        bool show = settings.ShowPrivateDocuments, mode = explorer.ExplorerMode;
        string root = settings.ExplorerDirectory;
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "privacy-fixture"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Private.html"); File.WriteAllText(path, "<p>Private saved text</p>");
        var document = TextFiles.Open(path); document.Edit("<p>Private unsaved text</p>");
        window.Documents.Add(document);
        void Show(bool value)
        {
            window.ShowPrivateDocumentsMenu.IsChecked = value;
            window.ShowPrivateDocumentsMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        try
        {
            check(new Settings().ShowPrivateDocuments && !document.IsPrivate, "Existing and new documents start visible and not private");
            Show(true); window.ActiveDocument = document;
            await window.CurrentView!.FirstPaint.WaitAsync(TimeSpan.FromSeconds(5));
            var menu = window.CreateDocumentMenu(document).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Private"));
            check(menu.IsCheckable && !menu.IsChecked, "Document and tab menus offer a checkable Private option");
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(document.IsPrivate && window.CreateDocumentMenu(document).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Private")).IsChecked,
                "Marking a document private updates its context-menu state");
            await explorer.SetFolderAsync(folder); explorer.SetMode(true);
            explorer.SetMultiFileSelection(true); explorer.DocumentSelection.Set(document, true); explorer.ExplorerSelection.Set(new PromptEntry(path, false), true);
            Show(false); window.UpdateLayout();
            check(!window.Tabs.Items.Contains(document) && !window.DocumentList.Items.Contains(document) && window.ActiveDocument != document,
                "Hiding private documents removes their tabs and list rows and switches away from private content");
            var next = new Document();
            using (var visibility = new DocumentPrivacy(new([original, document, next]), new ListBox(), settings))
                check(visibility.Adjacent(original, 1) == next && visibility.Adjacent(next, -1) == original,
                    "Keyboard document cycling skips hidden private tabs in both directions");
            check(explorer.Tree.Items.Cast<TreeViewItem>().Single(row => ((PromptEntry)row.Tag).Path == path).Visibility == Visibility.Collapsed &&
                explorer.DocumentSelection.Items.Count == 0 && explorer.ExplorerSelection.Items.Count == 0,
                "Private files also disappear from Explorer and are removed from bulk selections");
            check(window.Documents.Contains(document) && document.Dirty && document.Text.Contains("Private unsaved") && File.ReadAllText(path).Contains("Private saved"),
                "Hiding preserves the document, unsaved text and original file");
            var restored = JsonSerializer.Deserialize<WindowSession>(JsonSerializer.Serialize(window.Snapshot()))!;
            var restoredSettings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
            check(restored.Documents.Single(doc => doc.Id == document.Id).IsPrivate && !restoredSettings.ShowPrivateDocuments,
                "Private markings and Show Private Documents survive session/settings serialization");
            Show(true); window.UpdateLayout();
            check(window.Tabs.Items.Contains(document) && window.DocumentList.Items.Contains(document) && explorer.Tree.Items.Cast<TreeViewItem>().Single(row => ((PromptEntry)row.Tag).Path == path).Visibility == Visibility.Visible,
                "Show Private Documents restores the same documents in both navigation lists and tabs");
            window.CreateDocumentMenu(document).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Private")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Show(false);
            check(!document.IsPrivate && window.Tabs.Items.Contains(document), "Unmarking Private keeps the document visible when private documents are hidden");
        }
        finally
        {
            Show(show); window.ActiveDocument = original; window.RemoveDocument(document);
            explorer.SetMultiFileSelection(false);
            if (root.Length > 0) await explorer.SetFolderAsync(root);
            explorer.SetMode(mode);
        }
    }
}
