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
        window.Documents.Add(first); window.Documents.Add(second);
        var outputs = new List<Document>();
        try
        {
            explorer.SetMode(false); App.Current.Preferences.AutoSaveDirectory = folder;
            var picks = explorer.DocumentSelection;
            picks.Set(second, true); picks.Set(first, true); picks.Set(second, false); picks.Set(second, true);
            check(picks.Order(first) == 1 && picks.Order(second) == 2, "Combine tracks selection order and appends a reselected document last");
            check(window.ActiveDocument == original, "Selecting Combine inputs leaves the active editor unchanged");
            check(((MenuItem)window.FindName("CombineMenu")).Header.ToString()!.Contains("Combine"), "Combine is available in the File menu");
            int editors = window.CreatedEditorCount;
            var combined = await window.CombineSelectedDocuments(); outputs.Add(combined);
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
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in outputs) window.RemoveDocument(document);
            window.RemoveDocument(first); window.RemoveDocument(second);
            explorer.DocumentSelection.Clear(); explorer.ExplorerSelection.Clear();
            App.Current.Preferences.AutoSaveDirectory = autoSave;
            if (root.Length > 0) await explorer.SetFolderAsync(root);
            explorer.SetMode(mode);
        }
    }
}
