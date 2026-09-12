using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentCommandSelfTest
{
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
    }

    sealed class ControlKeyboard() : KeyboardDevice(InputManager.Current)
    {
        protected override KeyStates GetKeyStatesFromSystem(Key key) => key == Key.LeftCtrl ? KeyStates.Down : KeyStates.None;
    }
}
