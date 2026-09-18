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
        var previousView = window.CurrentView!;
        bool previousNavigation = window.IsDocumentList, previousMode = window.Explorer.ExplorerMode;
        window.SetDocumentList(true); window.Explorer.SetMode(false); window.UpdateLayout();
        var scroll = NavigationSelfTest.Descendants(window.DocumentList).OfType<ScrollViewer>().First();
        scroll.ScrollToBottom(); window.UpdateLayout();
        check(scroll.VerticalOffset > 0, "New document scroll regression starts at the bottom of a long Document List");
        double previousRibbon = window.DocumentPane.Margin.Top;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var draft = window.NewDocument();
        check(System.Text.RegularExpressions.Regex.IsMatch(draft.Name, @"^\d{4}-\d{2}-\d{2} \d{4} - Prompt \d+$") &&
            draft.Name.EndsWith($" - Prompt {draft.UntitledNumber}"), "New prompt name includes its creation date, minute and sequence number");
        var view = window.CurrentView!;
        check(previousRibbon > 0 && window.DocumentPane.Margin.Top == previousRibbon && view.RibbonHeight == previousRibbon,
            "First visit reserves the current ribbon height before the new WebView has initialized");
        check(!view.FirstPaint.IsCompleted && NavigationSelfTest.RegionContains(previousView.Browser, 20, 20) &&
            !NavigationSelfTest.RegionContains(previousView.Browser, 500, 300),
            "A first visit retains only the previous painted ribbon while the selected document initializes");
        try
        {
            bool loaded = false;
            for (int i = 0; i < 200; i++)
            {
                await Task.Delay(20);
                if (view.Browser.CoreWebView2 != null && await view.Browser.ExecuteScriptAsync("!!window.editor && !!document.querySelector('#document').contentDocument?.body?.isContentEditable") == "true") { loaded = true; break; }
            }
            check(loaded, $"New prompt editor initializes ({watch.ElapsedMilliseconds} ms)");
            window.UpdateLayout();
            check(scroll.VerticalOffset == 0 && window.Documents.IndexOf(draft) == window.Documents.Count(doc => doc.Pinned),
                "Creating a document scrolls to the top with the new draft immediately below pinned documents");
            // No click, focus() call, selection setup, or editing command in this test:
            // real input must land in the blank paragraph created by New document.
            await view.Browser.CoreWebView2!.CallDevToolsProtocolMethodAsync("Input.insertText", "{\"text\":\"Ready to type\"}");
            check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.body.textContent === 'Ready to type'") == "true",
                "A new prompt accepts typing immediately without clicking the document");
            await view.FirstPaint.WaitAsync(TimeSpan.FromSeconds(3));
            check(NavigationSelfTest.RegionContains(view.Browser, 20, 20) && !NavigationSelfTest.RegionContains(previousView.Browser, 20, 20),
                "The selected ribbon replaces the fallback only after initial formatting has painted");
            check(await view.Browser.ExecuteScriptAsync("document.querySelectorAll('.style-strip [data-style]').length === 5 && [...document.fonts].every(face => face.display === 'swap')") == "true",
                "All five Styles labels render without waiting for local Office fonts");
            int unloaded = 0, visibilityChanges = 0; view.Unloaded += (_, _) => unloaded++;
            view.IsVisibleChanged += (_, _) => visibilityChanges++;
            nint browserHandle = view.Browser.Handle;
            await view.Browser.ExecuteScriptAsync("window.ribbonMoves=0;window.ribbonObserver=new MutationObserver(records=>window.ribbonMoves+=records.length);window.ribbonObserver.observe(document.querySelector('.ribbon-groups'),{childList:true})");
            for (int i = 0; i < 8; i++)
            {
                window.ActiveDocument = previous; await Task.Delay(16);
                window.ActiveDocument = draft; await Task.Delay(16);
            }
            check(unloaded == 0 && view.Browser.Handle == browserHandle && view.Parent == window.EditorHost && window.EditorHost.Children.Count == 2,
                "Rapid document switching retains loaded browser windows without visual-tree teardown");
            check(await view.Browser.ExecuteScriptAsync("window.ribbonObserver.disconnect();window.ribbonMoves===0") == "true",
                "Rapid document switching preserves ribbon groups without rebuilding their layout");
            check(visibilityChanges == 0, "Rapid document switching keeps painted browser surfaces visible instead of hiding and showing the ribbon");
            check(NavigationSelfTest.RegionContains(view.Browser, 20, 20) && !NavigationSelfTest.RegionContains(previousView.Browser, 20, 20) && !previousView.IsEnabled,
                "Only the selected document's native ribbon is exposed and accepts input");
            await view.SetSourceAsync(true);
            check(view.Editor.IsVisible && !view.Browser.IsVisible && !NavigationSelfTest.RegionContains(previousView.Browser, 20, 20),
                "Cached inactive browsers cannot cover the selected document's source editor");
            await view.SetSourceAsync(false);
            string previewPath = Path.Combine(App.Current.Store.DirectoryPath, "switch-preview.txt");
            File.WriteAllText(previewPath, "Read-only preview remains above retained editors");
            using var preview = new ExplorerPreview(previewPath, App.Current.Store.DirectoryPath, () => window.EditorHost.Content = view);
            window.EditorHost.Content = preview;
            await preview.LoadAsync(CancellationToken.None);
            check(!NavigationSelfTest.RegionContains(view.Browser, 20, 20) && !NavigationSelfTest.RegionContains(previousView.Browser, 20, 20),
                "Opening a file preview clips every inactive native browser out of the preview");
            window.EditorHost.Content = view;
            window.SetAnnotationMode(view, true);
            check(view.Parent == window.AnnotationHost && view.IsEnabled && !NavigationSelfTest.RegionContains(previousView.Browser, 20, 20),
                "Full-content annotation retains its active browser without exposing other documents");
            window.SetAnnotationMode(view, false);
            check(NavigationSelfTest.RegionContains(view.Browser, 20, 20) && view.IsEnabled && view.Parent == window.EditorHost,
                "Returning from preview and annotation restores the selected ribbon and input");
        }
        finally { window.ActiveDocument = previous; window.RemoveDocument(draft); window.Explorer.SetMode(previousMode); window.SetDocumentList(previousNavigation); }
        bool show = App.Current.Preferences.ShowToolbar, wrap = App.Current.Preferences.WrapToolbar;
        App.Current.Preferences.ShowToolbar = false; App.Current.Preferences.WrapToolbar = false;
        var hiddenDraft = window.NewDocument(); var hiddenView = window.CurrentView!;
        bool stayedHidden = hiddenView.RibbonHeight == 0 && window.DocumentPane.Margin.Top == 0;
        hiddenView.ChromeChanged += (_, _) => stayedHidden &= hiddenView.RibbonHeight == 0;
        try
        {
            bool loaded = false;
            for (int i = 0; i < 200 && !loaded; i++)
            {
                await Task.Delay(20);
                loaded = hiddenView.Browser.CoreWebView2 != null && await hiddenView.Browser.ExecuteScriptAsync("!!window.editor") == "true";
            }
            check(loaded && stayedHidden && await hiddenView.Browser.ExecuteScriptAsync("document.querySelector('#toolbar').hidden && document.querySelector('#toolbar').classList.contains('ribbon-nowrap')") == "true",
                "A first-visit editor honors hidden and unwrapped ribbon preferences without briefly expanding");
        }
        finally
        {
            App.Current.Preferences.ShowToolbar = show; App.Current.Preferences.WrapToolbar = wrap;
            window.ActiveDocument = previous; window.RemoveDocument(hiddenDraft);
        }
    }

    public static async Task RenameDraft(MainWindow window, Action<bool, string> check)
    {
        var doc = window.ActiveDocument!;
        var view = window.CurrentView!;
        check(doc.Path == null && ((MenuItem)window.CreateDocumentMenu(doc).Items[0]).IsEnabled,
            "Rename is available for a new unsaved document");
        await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Draft rename keeps this text')");
        await view.FlushAsync();
        check(window.Title.EndsWith(doc.Name + " *"), "Typing marks the current document title with an asterisk");
        string originalText = doc.Text;
        int editors = window.CreatedEditorCount;
        await window.RenameDocumentFile(doc, "My renamed draft");
        check(doc.Name == "My renamed draft" && window.Title.EndsWith(doc.Name + " *") && doc.Path == null &&
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
        check(window.Title.EndsWith(doc.Name) && !window.Title.EndsWith(" *"), "Saving removes the modified-document title asterisk");
        var bold = KeyPress(Key.B);
        window.RaiseEvent(bold);
        check(!bold.Handled, "Browser formatting shortcuts still pass through the native window");
        var menu = window.CreateDocumentMenu(doc).Items.OfType<MenuItem>().ToArray();
        int pathItem = Array.FindIndex(menu, item => item.Header.ToString() == "Copy Full _Path");
        check(pathItem >= 0 && menu[pathItem + 1].Header.ToString() == "Copy for _AI Use" && menu[pathItem + 1].IsEnabled,
            "Copy For AI Use appears directly below Copy Full Path for a saved document");
        check(MainWindow.FullPathText(@"D:\My Prompts\Test.html") == "\"D:\\My Prompts\\Test.html\"" &&
            MainWindow.FullPathText(@"D:\Prompts\Test.html") == @"D:\Prompts\Test.html",
            "Copy Full Path quotes paths with spaces and leaves other paths unquoted");
        check(MainWindow.AiInstructionText(doc.Path!) == $"Read and execute the instructions in the \"{doc.Path}\" file.",
            "Copy For AI Use includes the complete quoted document path");
        await window.CurrentView!.Browser.ExecuteScriptAsync("window.editor.command('insertText','Toolbar save regression'); document.querySelector('[data-native-command=save]').click()");
        for (int i = 0; i < 100 && !File.ReadAllText(doc.Path!).Contains("Toolbar save regression"); i++) await Task.Delay(20);
        check(File.ReadAllText(doc.Path!).Contains("Toolbar save regression"), "Toolbar Save includes an edit made immediately before the click");
        await window.CurrentView.Browser.ExecuteScriptAsync("window.editor.command('insertText','AI handoff current edit')");
        string? copied = null;
        try
        {
            await window.CopyForAiUse(doc, text => copied = text);
            check(copied == MainWindow.AiInstructionText(doc.Path!) && File.ReadAllText(doc.Path!).Contains("AI handoff current edit") && DocumentAccess.IsReadOnly(doc.Path),
                "Copy for AI Use saves pending visual edits, locks the file, then copies its instructions");
        }
        finally { DocumentAccess.Set(doc, false); }
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
                var inline = controls.OfType<CheckBox>().Single(b => b.Content.ToString()!.StartsWith("Always Embed Images"));
                var separate = controls.OfType<CheckBox>().Single(b => b.Content.ToString()!.StartsWith("Always Store Images"));
                check(inline.IsChecked == false && separate.IsChecked == false, "Both image storage defaults start turned off");
                inline.IsChecked = true; separate.IsChecked = true;
                check(inline.IsChecked == false && separate.IsChecked == true, "Separate image storage clears the embed preference");
                inline.IsChecked = true;
                check(separate.IsChecked == false, "Embedding image storage clears the separate-file preference");
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
        check(preferences.ImageStorage == "inline" && App.Current.Store.Read<Settings>("settings.json").ImageStorage == "inline", "The selected image storage default persists in application settings");
        preferences.ImageStorage = ""; window.ApplyPreferences();
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
