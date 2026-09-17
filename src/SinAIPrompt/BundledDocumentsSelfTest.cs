using System.IO;
using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class BundledDocumentsSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "bundled-documents");
        string[] names = [BundledDocuments.Instructions, BundledDocuments.Formatting];
        var originals = names.ToDictionary(name => name, Original);
        var active = new Document { Text = "Keep the active recovery", SavedText = "Keep the active recovery" };
        var session = new Session { Windows = [new WindowSession { Documents = [active] }] };
        int editors = window.CreatedEditorCount;
        var errors = await BundledDocuments.EnsureAsync(session, folder);
        var references = session.Windows[0].Documents.Where(document => document != active).ToArray();
        check(errors.Count == 0 && references.Length == 2 && references.All(document => document.Pinned &&
            File.ReadAllBytes(document.Path!).SequenceEqual(originals[document.Name])),
            "Startup creates and pins exact embedded reference originals when their files are absent");
        check(session.Windows[0].ActiveIndex == 0 && session.Windows[0].Documents[0] == active && window.CreatedEditorCount == editors,
            "Adding startup references preserves the active recovery and creates no editors");
        errors = await BundledDocuments.EnsureAsync(session, folder);
        check(errors.Count == 0 && session.Windows[0].Documents.Count == 3 && references.All(session.Windows[0].Documents.Contains),
            "Repeated reference startup preserves document identities without adding duplicates");

        var recovered = references[0];
        string recovery = recovered.Text + "\n<p>Keep this unsaved edit.</p>", fingerprint = recovered.Fingerprint!;
        recovered.Text = recovery; recovered.Pinned = false;
        const string newer = "<p>Keep the user's newer reference.</p>";
        File.WriteAllText(recovered.Path!, newer);
        errors = await BundledDocuments.EnsureAsync(session, folder);
        check(errors.Count == 0 && recovered.Pinned && recovered.Dirty && recovered.Text == recovery &&
            recovered.Fingerprint == fingerprint && File.ReadAllText(recovered.Path!) == newer,
            "Startup preserves newer disk contents, unsaved recovery and its conflict fingerprint while restoring the pin");
        File.Delete(recovered.Path!);
        errors = await BundledDocuments.EnsureAsync(session, folder);
        check(errors.Count == 0 && File.ReadAllBytes(recovered.Path!).SequenceEqual(originals[recovered.Name]) &&
            recovered.Text == recovery && recovered.Dirty && recovered.Fingerprint == fingerprint && session.Windows[0].Documents.Count == 3,
            "A deleted reference is recreated from the bundle without discarding its open unsaved recovery");

        string existingFolder = Path.Combine(folder, "existing-files"); Directory.CreateDirectory(existingFolder);
        File.WriteAllText(Path.Combine(existingFolder, names[0]), newer);
        var empty = new Session();
        errors = await BundledDocuments.EnsureAsync(empty, existingFolder);
        check(errors.Count == 0 && empty.Windows.Count == 1 && empty.Windows[0].DocumentList &&
            empty.Windows[0].Documents.Count == 2 && empty.Windows[0].Documents.All(document => document.Pinned) &&
            empty.Windows[0].Documents.Single(document => document.Name == names[0]).Text == newer &&
            File.ReadAllText(Path.Combine(existingFolder, names[0])) == newer,
            "A fresh session opens and pins an existing newer reference without replacing it");

        string collisionFolder = Path.Combine(folder, "collisions"); Directory.CreateDirectory(collisionFolder);
        string stem = Path.GetFileNameWithoutExtension(names[0]);
        string occupied = Path.Combine(collisionFolder, stem + " 2.html"); File.WriteAllText(occupied, newer);
        Directory.CreateDirectory(Path.Combine(collisionFolder, stem + " 3.html"));
        var copy = await BundledDocuments.CreateCopyAsync(names[0], collisionFolder, [names[0].ToUpperInvariant()]);
        check(copy.Name == stem + " 4.html" && copy.Pinned && !copy.Dirty &&
            File.ReadAllBytes(copy.Path!).SequenceEqual(originals[names[0]]) && File.ReadAllText(occupied) == newer &&
            Directory.Exists(Path.Combine(collisionFolder, stem + " 3.html")),
            "Reference copies skip open names, existing files and directories without overwriting any version");
        var settings = new Settings { ExplorerDirectory = "working", AutoSaveDirectory = "autosave" };
        check(BundledDocuments.WorkingDirectory(settings, folder) == "working" &&
            BundledDocuments.WorkingDirectory(new Settings { AutoSaveDirectory = "autosave" }, folder) == "autosave" &&
            BundledDocuments.WorkingDirectory(new Settings(), folder) == Path.Combine(folder, "Reference Documents"),
            "Reference copies use the working folder, auto-save folder or isolated profile fallback");
        await HelpMenus(window, Path.Combine(folder, "help-menu"), originals, check);
    }

    static async Task HelpMenus(MainWindow window, string folder, Dictionary<string, byte[]> originals, Action<bool, string> check)
    {
        Directory.CreateDirectory(folder);
        var preferences = App.Current.Preferences;
        string previousFolder = preferences.ExplorerDirectory;
        bool previousMode = window.Explorer.ExplorerMode, previousVisible = window.IsDocumentList;
        bool previousListPreference = preferences.DocumentList;
        var recent = preferences.Recent.ToArray();
        var original = window.ActiveDocument!;
        var documents = window.Documents.ToArray();
        int editors = window.CreatedEditorCount;
        try
        {
            preferences.ExplorerDirectory = folder;
            check(Equals(window.AddInstructionsMenu.Header, "AI _Instructions") && Equals(window.AddFormattingMenu.Header, "AI _Chat Formatting"),
                "Help names the bundled references AI Instructions and AI Chat Formatting");
            foreach (var menu in new[] { window.AddInstructionsMenu, window.AddFormattingMenu })
            {
                string name = (string)menu.Tag, path = Path.Combine(folder, name);
                const string newer = "<p>This newer open document must be preserved.</p>";
                File.WriteAllText(path, newer);
                var existing = TextFiles.Open(path); window.Documents.Add(existing);
                int count = window.Documents.Count;
                menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for (int attempt = 0; attempt < 250 && !menu.IsEnabled; attempt++) await Task.Delay(20);
                check(menu.IsEnabled && window.Documents.Count == count + 1 && window.ActiveDocument != existing,
                    "Help re-adds a fresh reference while its newer version remains in the Document List: " + name);
                var added = window.ActiveDocument!;
                await window.CurrentView!.Initialization.WaitAsync(TimeSpan.FromSeconds(12));
                check(added.Pinned && added.Path == Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + " 2.html") &&
                    File.ReadAllBytes(added.Path!).SequenceEqual(originals[name]) && existing.Text == newer &&
                    File.ReadAllText(path) == newer && window.Documents.Contains(existing) && window.IsDocumentList && !window.Explorer.ExplorerMode,
                    "Help selects and pins the bundled copy without changing the newer open or saved content: " + name);
            }
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in window.Documents.Except(documents).ToArray()) window.RemoveDocument(document);
            preferences.ExplorerDirectory = previousFolder;
            preferences.Recent.Clear(); preferences.Recent.AddRange(recent);
            window.Explorer.SetMode(previousMode); window.SetDocumentList(previousVisible, false);
            preferences.DocumentList = previousListPreference;
        }
        check(window.CreatedEditorCount == editors && window.ActiveDocument == original,
            "Reference menu cleanup preserves the original active document and lazy editor count");
    }

    static byte[] Original(string name)
    {
        using var stream = typeof(BundledDocumentsSelfTest).Assembly.GetManifestResourceStream("SinAIPrompt.BundledDocuments." + name)
            ?? throw new IOException("Missing embedded reference: " + name);
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
}
