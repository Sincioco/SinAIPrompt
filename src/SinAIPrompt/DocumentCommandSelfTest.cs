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
        finally { window.RemoveDocument(draft); window.ActiveDocument = previous; }
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
        check(menu[2].Header.ToString() == "Copy Full _Path" && menu[3].Header.ToString() == "Copy For _AI Use" && menu[3].IsEnabled,
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

    sealed class ControlKeyboard() : KeyboardDevice(InputManager.Current)
    {
        protected override KeyStates GetKeyStatesFromSystem(Key key) => key == Key.LeftCtrl ? KeyStates.Down : KeyStates.None;
    }
}
