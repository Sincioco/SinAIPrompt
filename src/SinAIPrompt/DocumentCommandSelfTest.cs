using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentCommandSelfTest
{
    public static async Task NewPromptReady(MainWindow window, Action<bool, string> check)
    {
        var previous = window.ActiveDocument!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var draft = window.NewDocument();
        check(System.Text.RegularExpressions.Regex.IsMatch(draft.Name, @"^\d{4}-\d{2}-\d{2} \d{4} - Prompt \d+$") &&
            draft.Name.EndsWith($" - Prompt {draft.UntitledNumber}"), "New prompt name includes its creation date, minute and sequence number");
        var view = window.CurrentView!;
        try
        {
            bool loaded = false;
            for (int i = 0; i < 200; i++)
            {
                await Task.Delay(20);
                if (view.Browser.CoreWebView2 != null && await view.Browser.ExecuteScriptAsync("!!window.editor && !!document.querySelector('#document').contentDocument?.body?.isContentEditable") == "true") { loaded = true; break; }
            }
            check(loaded, $"New prompt editor initializes ({watch.ElapsedMilliseconds} ms)");
            // No click, focus() call, selection setup, or editing command in this test:
            // real input must land in the blank paragraph created by New document.
            await view.Browser.CoreWebView2!.CallDevToolsProtocolMethodAsync("Input.insertText", "{\"text\":\"Ready to type\"}");
            check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.body.textContent === 'Ready to type'") == "true",
                "A new prompt accepts typing immediately without clicking the document");
            check(await view.Browser.ExecuteScriptAsync("document.querySelectorAll('.style-strip [data-style]').length === 5 && [...document.fonts].every(face => face.display === 'swap')") == "true",
                "All five Styles labels render without waiting for local Office fonts");
        }
        finally { window.ActiveDocument = previous; window.RemoveDocument(draft); }
    }

    public static async Task RenameDraft(MainWindow window, Action<bool, string> check)
    {
        var doc = window.ActiveDocument!;
        var view = window.CurrentView!;
        check(doc.Path == null && ((MenuItem)window.CreateDocumentMenu(doc).Items[0]).IsEnabled,
            "Rename is available for a new unsaved document");
        await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Draft rename keeps this text')");
        await view.FlushAsync();
        string originalText = doc.Text;
        int editors = window.CreatedEditorCount;
        await window.RenameDocumentFile(doc, "My renamed draft");
        check(doc.Name == "My renamed draft" && window.Title.EndsWith(doc.Name) && doc.Path == null &&
            doc.Text == originalText && doc.Dirty && window.CreatedEditorCount == editors,
            "Draft rename updates its title without saving, losing edits, or creating an editor");
        check(App.Current.SaveState(), "Renamed draft can be saved to session recovery");
        var recovered = App.Current.Store.Read<Session>("session.json").Windows[0].Documents.Single(d => d.Id == doc.Id);
        check(recovered.Name == doc.Name && recovered.Path == null && recovered.Text == doc.Text && recovered.Dirty,
            "Session recovery preserves the draft name and unsaved text");
        check(JsonSerializer.Deserialize<Document>("{\"UntitledNumber\":42}")!.Name == "Prompt 42",
            "Older recovery records keep their default untitled names");
        foreach (string invalid in new[] { "", "..", "bad/name", "CON.html", "trailing.", ".html" })
        {
            bool rejected = false;
            try { await window.RenameDocumentFile(doc, invalid); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && doc.Name == "My renamed draft" && doc.Text == originalText && doc.Path == null,
                "Invalid draft name leaves the document unchanged: " + invalid);
        }
    }

    public static async Task SaveShortcut(MainWindow window, Action<bool, string> check)
    {
        var doc = window.ActiveDocument!;
        check(doc.Path != null && doc.DraftName == null && doc.Name == Path.GetFileName(doc.Path),
            "First save replaces the draft name with the actual file name");
        await window.CurrentView!.Browser.ExecuteScriptAsync("window.editor.command('insertText','Shortcut save regression')");
        var keyboard = new ControlKeyboard();
        KeyEventArgs KeyPress(Key key) => new(keyboard, PresentationSource.FromVisual(window), 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        var save = KeyPress(Key.S);
        window.RaiseEvent(save);
        // WebView receives the event immediately after this handler returns, before its await completes.
        check(save.Handled, "Ctrl+S is consumed before asynchronous editor synchronization can dispatch a duplicate save");
        bool saved = false;
        for (int i = 0; i < 100 && !saved; i++)
        {
            await Task.Delay(20);
            saved = File.ReadAllText(doc.Path!).Contains("Shortcut save regression");
        }
        check(saved, "The consumed Ctrl+S still saves the pending visual edit");
        var bold = KeyPress(Key.B);
        window.RaiseEvent(bold);
        check(!bold.Handled, "Browser formatting shortcuts still pass through the native window");
        var menu = window.CreateDocumentMenu(doc).Items.OfType<MenuItem>().ToArray();
        int pathItem = Array.FindIndex(menu, item => item.Header.ToString() == "Copy Full _Path");
        check(pathItem >= 0 && menu[pathItem + 1].Header.ToString() == "Copy For _AI Use" && menu[pathItem + 1].IsEnabled,
            "Copy For AI Use appears directly below Copy Full Path for a saved document");
        check(MainWindow.FullPathText(@"D:\My Prompts\Test.html") == "\"D:\\My Prompts\\Test.html\"" &&
            MainWindow.FullPathText(@"D:\Prompts\Test.html") == @"D:\Prompts\Test.html",
            "Copy Full Path quotes paths with spaces and leaves other paths unquoted");
        check(MainWindow.AiInstructionText(doc.Path!) == $"Read and execute the instructions in the \"{doc.Path}\" file.",
            "Copy For AI Use includes the complete quoted document path");
        await window.CurrentView!.Browser.ExecuteScriptAsync("window.editor.command('insertText','Toolbar save regression'); document.querySelector('[data-native-command=save]').click()");
        for (int i = 0; i < 100 && !File.ReadAllText(doc.Path!).Contains("Toolbar save regression"); i++) await Task.Delay(20);
        check(File.ReadAllText(doc.Path!).Contains("Toolbar save regression"), "Toolbar Save includes an edit made immediately before the click");
    }

    public static async Task Appearance(MainWindow window, Action<bool, string> check)
    {
        var preferences = App.Current.Preferences;
        var view = window.CurrentView!;
        preferences.ShowToolbar = false; window.ApplyPreferences();
        check(await view.Browser.ExecuteScriptAsync("document.querySelector('#toolbar').hidden") == "true", "Toolbar preference hides formatting without hiding the document");
        Exception? failure = null;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.Cast<Window>().SingleOrDefault(w => w.Title == "Settings - Sin - AI Prompt");
            if (dialog?.IsLoaded != true) return;
            timer.Stop();
            try
            {
                IEnumerable<DependencyObject> Controls(DependencyObject root)
                {
                    yield return root;
                    foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                        foreach (var descendant in Controls(child)) yield return descendant;
                }
                var controls = Controls(dialog).ToArray();
                var tabs = controls.OfType<TabControl>().Single();
                check(tabs.Items.Count == 3 && dialog.ActualHeight <= 570, "Settings groups options in three compact tabs");
                tabs.SelectedIndex = 1;
                var toolbar = controls.OfType<CheckBox>().Single(b => b.Content.ToString() == "Show Formatting Toolbar");
                check(toolbar.IsChecked == false, "Settings reflects the current toolbar visibility");
                toolbar.IsChecked = true;
                dialog.UpdateLayout();
                controls.OfType<Button>().Single(b => b.Content.ToString() == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        try { timer.Start(); SettingsDialog.Show(window); }
        finally { timer.Stop(); }
        if (failure != null) throw failure;
        window.ApplyPreferences();
        check(preferences.ShowToolbar && App.Current.Store.Read<Settings>("settings.json").ShowToolbar &&
            await view.Browser.ExecuteScriptAsync("!document.querySelector('#toolbar').hidden") == "true", "Settings restores the toolbar and persists the same visibility preference");
    }

    public static async Task DuplicateAndRevert(MainWindow window, Action<bool, string> check)
    {
        var source = window.ActiveDocument!; var view = window.CurrentView!;
        string originalAssets = Path.Combine(Path.GetDirectoryName(source.Path!)!, Path.GetFileNameWithoutExtension(source.Path!));
        string originalImage = Directory.GetFiles(originalAssets, "*.png")[0];
        string separate = "<p><img src=\"" + Uri.EscapeDataString(Path.GetFileName(originalAssets)) + "/" + Path.GetFileName(originalImage) + "\" data-sin-storage=\"separate\"></p>";
        await view.Browser.ExecuteScriptAsync("window.editor.command('insertHTML'," + JsonSerializer.Serialize(separate) + ")");
        await window.SaveDocument(source);
        string saved = File.ReadAllText(source.Path!);
        await view.Browser.ExecuteScriptAsync("window.editor.command('insertText',' Unsaved duplicate snapshot')");
        var copy = await window.DuplicateDocument(source);
        var copyView = window.CurrentView!;
        await copyView.FlushAsync();
        check(copy.Id != source.Id && copy.Name == Path.GetFileNameWithoutExtension(source.Name) + " 2" + Path.GetExtension(source.Name) && copy.Text.Contains("Unsaved duplicate snapshot"),
            "Duplicate keeps the name with a numbered suffix and includes the current unsaved edit");
        check(File.ReadAllText(source.Path!) == saved && source.Dirty && !copy.Dirty,
            "Duplicating a saved document writes the copy without saving or changing the original");
        string assets = Path.Combine(Path.GetDirectoryName(copy.Path!)!, Path.GetFileNameWithoutExtension(copy.Path!));
        check(Directory.Exists(assets) && Directory.GetFiles(assets, "*.png").Length > 0 && copy.Text.Contains(Uri.EscapeDataString(Path.GetFileNameWithoutExtension(copy.Path!)) + "/image-"),
            "A saved duplicate owns a separate image folder and references its copied images");
        window.ActiveDocument = source;
        var another = await window.DuplicateDocument(source);
        check(another.Name == Path.GetFileNameWithoutExtension(source.Name) + " 3" + Path.GetExtension(source.Name), "Duplicate numbering skips existing files and open document names");
        window.ActiveDocument = source; window.RemoveDocument(another); window.RemoveDocument(copy);
        await view.FlushAsync(); string changed = source.Text;
        bool asked = false;
        check(!await window.RevertDocument(source, () => { asked = true; return false; }) && asked && source.Text == changed,
            "Revert asks for confirmation and cancellation preserves unsaved changes");
        check(await window.RevertDocument(source, () => true), "Confirmed Revert loads the last saved document");
        await view.FlushAsync();
        check(!source.Dirty && !source.Text.Contains("Unsaved duplicate snapshot"), "Revert synchronizes the visual editor and clears the modified state");
        var menu = window.CreateDocumentMenu(source).Items.OfType<MenuItem>().ToArray();
        check(menu[^2].Header.ToString() == "_Revert To Last Saved…" && menu[^1].Header.ToString() == "_Close", "Revert appears directly above Close in the document/tab menu");

        var draft = window.NewDocument(); var draftView = window.CurrentView!;
        await draftView.FlushAsync();
        string imagePath = Directory.GetFiles(assets, "*.png")[0];
        string draftHtml = "<p>Draft with image</p><img src=\"" + new Uri(imagePath).AbsoluteUri + "\" data-sin-storage=\"separate\">";
        draftView.AcceptHtml(draftHtml); draftView.ReloadSavedHtml();
        var draftCopy = await window.DuplicateDocument(draft);
        check(draftCopy.Path == null && draftCopy.Dirty && draftCopy.Text.Contains("data:image/png;base64,") && draftCopy.Text.Contains("data-sin-storage=\"inline\""),
            "Duplicating an unsaved draft creates independent embedded image copies without a Save dialog");
        check(!await window.RevertDocument(draftCopy, () => throw new Exception("Draft must not prompt")), "Unsaved drafts cannot revert to a nonexistent saved file");
        window.ActiveDocument = source; window.RemoveDocument(draftCopy); window.RemoveDocument(draft);
    }

    sealed class ControlKeyboard() : KeyboardDevice(InputManager.Current)
    {
        protected override KeyStates GetKeyStatesFromSystem(Key key) => key == Key.LeftCtrl ? KeyStates.Down : KeyStates.None;
    }
}
