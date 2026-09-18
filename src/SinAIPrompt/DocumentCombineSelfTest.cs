using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentCombineSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!;
        var processor = window.CurrentView!;
        var explorer = window.Explorer;
        bool mode = explorer.ExplorerMode;
        bool navigation = window.IsDocumentList;
        string root = App.Current.Preferences.ExplorerDirectory, autoSave = App.Current.Preferences.AutoSaveDirectory;
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "combine-fixture"); Directory.CreateDirectory(folder);
        string assets = Path.Combine(folder, "First"); Directory.CreateDirectory(assets);
        string image = Path.Combine(assets, "pixel.png");
        File.Copy(Path.Combine(App.Current.Store.DirectoryPath, "color-emoji.png"), image);
        string firstPath = Path.Combine(folder, "First.html"), secondPath = Path.Combine(folder, "Second.html");
        string firstHtml = "<p id='first'>First saved</p><ol start='8'><li value='8'>A<ul><li>Nested bullet</li></ul></li><li>B</li></ol><img src='First/pixel.png'>";
        string secondHtml = "<p>Second saved</p><ol start='20'><li value='20'>C</li></ol><ul><li>Ordinary bullet</li></ul>";
        File.WriteAllText(firstPath, firstHtml); File.WriteAllText(secondPath, secondHtml);
        var first = TextFiles.Open(firstPath); first.Edit(first.Text.Replace("First saved", "First unsaved"));
        var second = TextFiles.Open(secondPath);
        second.Emoji = "🦊";
        window.Documents.Add(first); window.Documents.Add(second);
        var outputs = new List<Document>();
        try
        {
            window.MultiFileSelectionMenu.IsChecked = true;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            explorer.SetMode(false); App.Current.Preferences.AutoSaveDirectory = folder;
            window.SetDocumentList(true); window.UpdateLayout();
            var scroll = NavigationSelfTest.Descendants(window.DocumentList).OfType<ScrollViewer>().First();
            scroll.ScrollToBottom(); window.UpdateLayout();
            check(scroll.VerticalOffset > 0, "Combine scroll regression starts at the bottom of a long Document List");
            var picks = explorer.DocumentSelection;
            picks.Set(second, true); picks.Set(first, true); picks.Set(second, false); picks.Set(second, true);
            check(picks.Order(first) == 1 && picks.Order(second) == 2, "Combine tracks selection order and appends a reselected document last");
            check(window.ActiveDocument == original, "Selecting Combine inputs leaves the active editor unchanged");
            check(((MenuItem)window.FindName("CombineMenu")).Header.ToString()!.Contains("Combine"), "Combine is available in the File menu");
            check(window.CreateDocumentMenu(first, true).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "_Combine Selected Documents…")) &&
                !window.CreateDocumentMenu(original, true).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "_Combine Selected Documents…")),
                "Right-clicking a checked document offers Combine while an unchecked document does not");
            int editors = window.CreatedEditorCount;
            var combined = await window.CombineSelectedDocuments(); outputs.Add(combined);
            await window.CurrentView!.FirstPaint.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(80); window.UpdateLayout();
            check(scroll.VerticalOffset == 0 && window.Documents.IndexOf(combined) == window.Documents.Count(doc => doc.Pinned),
                "Combining documents scrolls to the top with the new output immediately below pins");
            check(window.CreatedEditorCount == editors + 1 && picks.Items.Count == 0, "Combining creates only the output editor and clears the completed selection");
            check(Regex.IsMatch(combined.Name, @"^\d{4}-\d{2}-\d{2} \d{4} - Combined\.html$") && File.Exists(combined.Path),
                "Combined HTML is saved using the current date/time and Combined name");
            check(combined.Text.IndexOf("First unsaved", StringComparison.Ordinal) < combined.Text.IndexOf("Second saved", StringComparison.Ordinal) && combined.Text.Contains("data:image/png;base64,"),
                "Combined content follows selection order, includes unsaved edits and embeds relative source images");
            string parsed = "new DOMParser().parseFromString(" + JsonSerializer.Serialize(combined.Text) + ",'text/html')";
            string evaluate = "(()=>{const d=" + parsed + ";const lists=[...d.querySelectorAll('ol')];return lists.map(l=>l.start).join(',')==='1,3'&&!d.querySelector('li[value]')&&d.querySelectorAll('ul').length===2;})()";
            check(await processor.Browser.ExecuteScriptAsync(evaluate) == "true", "Numbered lists continue across source documents while nested and ordinary bullets remain intact");
            check(File.ReadAllText(firstPath) == firstHtml && File.ReadAllText(secondPath) == secondHtml && first.Dirty,
                "Combine preserves source files and their unsaved state");
            check(first.Emoji == "❌" && second.Emoji == "🦊" && combined.Emoji == "",
                "Successful Combine marks only unlabeled source documents with the cross mark and preserves existing emojis");
            window.ActiveDocument = original;
            await explorer.SetFolderAsync(folder); explorer.SetMode(true);
            explorer.ExplorerSelection.Set(new PromptEntry(secondPath, false), true);
            explorer.ExplorerSelection.Set(new PromptEntry(firstPath, false), true);
            var reverse = await window.CombineSelectedDocuments(); outputs.Add(reverse);
            check(reverse.Text.IndexOf("Second saved", StringComparison.Ordinal) < reverse.Text.IndexOf("First unsaved", StringComparison.Ordinal),
                "Prompt Explorer combines in its own selection order and reuses open unsaved content");
            check(reverse.Name == Path.GetFileNameWithoutExtension(combined.Name) + " 1.html", "A duplicate Combined name receives a numeric suffix without overwriting the original");
            var draft = await DocumentCombine.CreateAsync([new("<p>Draft one</p>", new Uri(firstPath).AbsoluteUri), new("<p>Draft two</p>", new Uri(secondPath).AbsoluteUri)], processor, null, []);
            check(draft.Path == null && draft.Dirty && draft.Name.EndsWith(" - Combined"), "Combining without a save folder creates a correctly named unsaved HTML draft");
            string thirdPath = Path.Combine(folder, "Closed third.html"), fourthPath = Path.Combine(folder, "Closed fourth.html");
            File.WriteAllText(thirdPath, "<p>Closed third</p>"); File.WriteAllText(fourthPath, "<p>Closed fourth</p>");
            DocumentEmojis.Set(new Document { Path = fourthPath }, "💡", App.Current.Preferences);
            explorer.ExplorerSelection.Set(new PromptEntry(thirdPath, false), true);
            explorer.ExplorerSelection.Set(new PromptEntry(fourthPath, false), true);
            var combineItem = explorer.CreateFileMenu(new(thirdPath, false)).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Combine Selected Documents…"));
            var previousDocuments = window.Documents.ToHashSet(); int previousEditors = window.CreatedEditorCount;
            combineItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (int i = 0; i < 150 && window.Documents.All(previousDocuments.Contains); i++) await Task.Delay(20);
            var fromMenu = window.Documents.Single(doc => !previousDocuments.Contains(doc)); outputs.Add(fromMenu);
            await window.CurrentView!.FirstPaint.WaitAsync(TimeSpan.FromSeconds(5));
            check(fromMenu.Text.Contains("Closed third") && fromMenu.Text.Contains("Closed fourth") && window.CreatedEditorCount == previousEditors + 1 &&
                !window.Documents.Any(doc => doc.Path == thirdPath || doc.Path == fourthPath),
                "Explorer's actual Combine context command creates only the result without opening its source files");
            check(DocumentEmojis.Read(App.Current.Preferences, thirdPath) == "❌" && DocumentEmojis.Read(App.Current.Preferences, fourthPath) == "💡",
                "Unopened combined files receive the cross mark while existing persisted emojis are preserved");
            var settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(App.Current.Preferences))!;
            var reopened = TextFiles.Open(thirdPath); DocumentEmojis.Restore(reopened, settings);
            check(reopened.Emoji == "❌" && File.ReadAllText(thirdPath) == "<p>Closed third</p>",
                "Combined-source marks survive settings serialization and reopening without changing source HTML");
            await explorer.RefreshAsync(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var thirdRow = explorer.Tree.Items.Cast<TreeViewItem>().Single(row => ((PromptEntry)row.Tag).Path == thirdPath);
            check(((StackPanel)thirdRow.Header).Children.OfType<Image>().Any(icon => System.Windows.Automation.AutomationProperties.GetName(icon) == "❌" && icon.Source != null),
                "Prompt Explorer renders the combined-source cross mark using the existing colorful emoji renderer");
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in outputs) window.RemoveDocument(document);
            window.RemoveDocument(first); window.RemoveDocument(second);
            explorer.DocumentSelection.Clear(); explorer.ExplorerSelection.Clear();
            window.MultiFileSelectionMenu.IsChecked = false;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            App.Current.Preferences.AutoSaveDirectory = autoSave;
            if (root.Length > 0) await explorer.SetFolderAsync(root);
            explorer.SetMode(mode);
            window.SetDocumentList(navigation);
        }
    }
}
