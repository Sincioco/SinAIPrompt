using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class PromptProjectSessionSelfTest
{
    internal static async Task Run(MainWindow window, string folder, Action<bool, string> check)
    {
        var original = window.ActiveDocument!; var explorer = window.Explorer; var settings = App.Current.Preferences;
        string oldRoot = settings.ExplorerDirectory; bool oldMode = explorer.ExplorerMode, oldVisible = window.IsDocumentList, oldSelection = explorer.DocumentSelection.Enabled;
        var projects = settings.ProjectFolders.ToArray(); var recent = settings.Recent.ToArray(); var emojis = new Dictionary<string, string>(settings.FileEmojis);
        string root = Path.Combine(folder, "project-session"); Directory.CreateDirectory(root);
        string sources = Path.Combine(root, "Original prompts"); Directory.CreateDirectory(sources);
        string project = PromptProjects.Create(root, "故事 #1"); PromptProjects.Remember(settings, project);
        string later = PromptProjects.Create(root, "Second destination"); PromptProjects.Remember(settings, later);
        string[] names = ["Open #1", "Lazy #2", "Closed #3", "Locked #4"];
        string[] paths = names.Select(name => Path.Combine(sources, name + ".html")).ToArray();
        var encoder = new PngBitmapEncoder();
        var pixels = Enumerable.Repeat((byte)255, 24 * 16 * 4).ToArray();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(24, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 24 * 4)));
        using var png = new MemoryStream(); encoder.Save(png);
        foreach (var (name, path) in names.Zip(paths))
        {
            string assets = Path.Combine(sources, name); Directory.CreateDirectory(assets); File.WriteAllBytes(Path.Combine(assets, "picture.png"), png.ToArray());
            File.WriteAllText(path, "<!doctype html><html><body><p>Saved " + name + "</p><img src=\"" + Uri.EscapeDataString(name) + "/picture.png\" data-sin-storage=\"separate\"><a href=\"../reference.txt\">Reference</a></body></html>");
        }
        File.WriteAllText(Path.Combine(root, "reference.txt"), "External reference");
        File.SetAttributes(paths[3], File.GetAttributes(paths[3]) | FileAttributes.ReadOnly);
        Document? visible = null, lazy = null, locked = null;
        try
        {
            await CreateDialog(window, root, settings, check);
            window.OpenPaths([paths[0]]); visible = window.ActiveDocument!; visible.AutoSave = false;
            var view = window.CurrentView!; await view.ExportAsync();
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText',' unsaved visible project text')"); await view.FlushAsync();
            lazy = TextFiles.Open(paths[1]); lazy.Edit(lazy.Text.Replace("</body>", "<p>Unsaved lazy project text</p></body>", StringComparison.Ordinal));
            locked = TextFiles.Open(paths[3]); window.Documents.Add(lazy); window.Documents.Add(locked);
            DocumentEmojis.Set(visible, "🌌", settings); settings.Recent.Insert(0, paths[0]);
            int editorCount = window.CreatedEditorCount;
            var closedEntry = new PromptEntry(paths[2], false);
            explorer.SetMultiFileSelection(true); window.MultiFileSelectionMenu.IsChecked = true;
            explorer.DocumentSelection.Set(visible, true); explorer.DocumentSelection.Set(lazy, true); explorer.DocumentSelection.Set(locked, true);
            explorer.ExplorerSelection.Set(new PromptEntry(paths[0], false), true); explorer.ExplorerSelection.Set(closedEntry, true);
            await explorer.SetFolderAsync(sources); window.SetDocumentList(true);
            var single = window.CreateProjectMenu([visible]);
            check(Destination(single, project).IsEnabled && Destination(window.CreateProjectMenu([locked]), project).IsEnabled,
                "Single saved and read-only prompts offer registered projects as move destinations");
            var documentMenu = window.CreateDocumentMenu(visible, true).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move to Project"));
            var explorerMenu = explorer.CreateFileMenu(closedEntry).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move to Project"));
            check(Destination(documentMenu, project).IsEnabled && Destination(explorerMenu, project).IsEnabled,
                "Document List and Prompt Explorer context menus offer project moves for their checked prompt batches");
            bool draftRejected = false, mixedRejected = false;
            try { await window.MovePromptsToProject([new Document()], project); } catch (IOException) { draftRejected = true; }
            try { await window.MovePromptsToProject([visible, new PromptEntry(Path.Combine(root, "reference.txt"), false)], project); } catch (IOException) { mixedRejected = true; }
            check(draftRejected && mixedRejected && File.Exists(paths[0]), "Project moves reject unsaved drafts and mixed non-HTML selections without moving a saved prompt");

            await window.MovePromptsToProject([visible, lazy, closedEntry, locked], project);
            string[] destinations = paths.Select(path => Path.Combine(project, Path.GetFileName(path))).ToArray();
            check(destinations.All(File.Exists) && paths.All(path => !File.Exists(path)) && names.All(name => Directory.Exists(Path.Combine(project, name)) && !Directory.Exists(Path.Combine(sources, name))),
                "Native project integration moves a mixed open/closed HTML batch and every corresponding image folder");
            check(visible.Path == destinations[0] && lazy.Path == destinations[1] && locked.Path == destinations[3] && window.ActiveDocument == visible && window.CurrentView == view,
                "Project moves update existing open models while keeping the active document and editor instance");
            check(visible.Dirty && visible.Text.Contains("unsaved visible project text") && lazy.Dirty && lazy.Text.Contains("Unsaved lazy project text") &&
                !File.ReadAllText(destinations[0]).Contains("unsaved visible project text") && !File.ReadAllText(destinations[1]).Contains("Unsaved lazy project text"),
                "Project integration preserves live and unopened unsaved buffers without writing them into the moved saved files");
            check(window.CreatedEditorCount == editorCount && !window.Documents.Any(document => document.Path == destinations[2]),
                "Moving unopened prompts creates neither hidden WebViews nor extra document models");
            check(locked.IsReadOnly && !locked.Dirty && File.GetAttributes(destinations[3]).HasFlag(FileAttributes.ReadOnly) &&
                visible.SavedText == TextFiles.Open(destinations[0]).Text && visible.Fingerprint == TextFiles.Open(destinations[0]).Fingerprint,
                "Moved models retain read-only protection and synchronized saved text/fingerprints");
            check(settings.Recent.Contains(destinations[0]) && !settings.Recent.Contains(paths[0]) && DocumentEmojis.Read(settings, destinations[0]) == "🌌" && DocumentEmojis.Read(settings, paths[0]) == "",
                "Project moves transfer recent-file and emoji metadata to the new paths");
            check(explorer.DocumentSelection.Items.Count == 0 && explorer.ExplorerSelection.Items.Count == 0 && window.OpenProgress.Visibility == Visibility.Collapsed,
                "Completed project moves clear old checked paths and dismiss their progress gauge");
            check(await ImageLoads(view), "The already-open document still loads its image from the moved project folder");
            string portable = await view.ExportAsync();
            check(portable.Contains("unsaved visible project text") && ImageReferences.LocalPaths(visible.Text, visible.Path!).Contains(Path.Combine(project, names[0], "picture.png")),
                "Moved live HTML keeps its editable content and relative image reference rooted at the new document path");

            await explorer.SetFolderAsync(project); explorer.SetMode(true); await explorer.RefreshAsync();
            check(destinations.All(path => explorer.Entries.Any(entry => entry.Path == path && entry.ImageFolder == Path.Combine(project, Path.GetFileNameWithoutExtension(path)))),
                "Prompt Explorer refresh exposes moved HTML entries with their paired image folders");
            await view.SetSourceAsync(true); view.Editor.AppendText("\n<p>Unsaved source-view project text</p>");
            explorer.DocumentSelection.Set(visible, true); explorer.DocumentSelection.Set(lazy, true);
            var selectedMove = window.CreateDocumentMenu(visible, true).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move to Project"));
            Destination(selectedMove, later).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => visible.Path == Path.Combine(later, Path.GetFileName(paths[0])) && lazy.Path == Path.Combine(later, Path.GetFileName(paths[1])) && window.OpenProgress.Visibility == Visibility.Collapsed);
            check(!view.IsVisual && visible.Dirty && visible.Text.Contains("Unsaved source-view project text") && TextFiles.Normalize(view.Editor.Text) == visible.Text && lazy.Text.Contains("Unsaved lazy project text"),
                "The actual checked Document List menu moves its whole batch while preserving active source-view and unopened unsaved edits");
            await view.SetSourceAsync(false); await view.ExportAsync();
            check(await ImageLoads(view), "Returning from source view after a project move reloads images at their new location");
            window.ActiveDocument = locked; var lockedView = window.CurrentView!; await lockedView.ExportAsync();
            await window.MovePromptsToProject([locked], later);
            check(locked.Path == Path.Combine(later, Path.GetFileName(paths[3])) && locked.IsReadOnly && !locked.Dirty && await ImageLoads(lockedView),
                "Moving the active clean read-only prompt preserves its lock, clean state and rendered image");
            var closedMove = explorer.CreateFileMenu(new PromptEntry(destinations[2], false)).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Move to Project"));
            Destination(closedMove, later).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => File.Exists(Path.Combine(later, Path.GetFileName(paths[2]))) && window.OpenProgress.Visibility == Visibility.Collapsed);
            check(!window.Documents.Any(document => document.Path == Path.Combine(later, Path.GetFileName(paths[2]))),
                "The actual single-prompt Explorer menu moves an unopened file without opening a document");
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in new[] { visible, lazy, locked }) if (document != null && window.Documents.Contains(document)) window.RemoveDocument(document);
            explorer.SetMultiFileSelection(oldSelection); window.MultiFileSelectionMenu.IsChecked = oldSelection;
            if (oldRoot.Length > 0) await explorer.SetFolderAsync(oldRoot); explorer.SetMode(oldMode); window.SetDocumentList(oldVisible);
            settings.ProjectFolders = projects.ToList(); settings.Recent = recent.ToList(); settings.FileEmojis = emojis;
        }
    }

    static MenuItem Destination(MenuItem menu, string path) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, path));
    static async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(50); }
        throw new TimeoutException("The project menu operation did not complete.");
    }
    static async Task CreateDialog(MainWindow window, string parent, Settings settings, Action<bool, string> check)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.Cast<Window>().SingleOrDefault(item => item.Title.StartsWith("Create Project", StringComparison.Ordinal));
            if (dialog?.IsLoaded != true) return;
            timer.Stop(); var controls = ScreenCaptureSelfTest.Controls(dialog).ToArray();
            controls.OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Project Name").Text = "Named from dialog";
            controls.OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "Project Parent Folder").Text = parent;
            controls.OfType<Button>().Single(item => Equals(item.Content, "Create")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            timer.Start(); string? created = await PromptProjectUi.CreateAsync(window, settings, parent);
            check(created == Path.Combine(parent, "Project - Named from dialog") && Directory.Exists(created) && settings.ProjectFolders.Contains(created),
                "The native naming dialog creates and registers the requested physical project folder");
        }
        finally { timer.Stop(); }
    }
    static async Task<bool> ImageLoads(EditorView view)
    {
        string response = await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
        {
            expression = "(async()=>{const doc=document.querySelector('#document').contentDocument,shown=doc.images[0];if(!shown)return false;const image=new doc.defaultView.Image();image.src=shown.src+(shown.src.includes('?')?'&':'?')+'projectMoveTest='+Date.now();try{await shown.decode();await image.decode();return shown.naturalWidth===24&&image.naturalWidth===24&&image.naturalHeight===16;}catch(error){return {src:shown.src,srcset:shown.srcset,width:shown.naturalWidth,error:String(error)};}})()",
            awaitPromise = true, returnByValue = true
        }));
        using var result = JsonDocument.Parse(response);
        File.WriteAllText(Path.Combine(App.Current.Store.DirectoryPath, "project-image-check.json"), response);
        return result.RootElement.GetProperty("result").TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
    }
}
