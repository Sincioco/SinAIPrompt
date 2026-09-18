using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class FileSelectionSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var explorer = window.Explorer;
        var original = window.ActiveDocument!;
        bool mode = explorer.ExplorerMode, navigation = window.IsDocumentList;
        string root = App.Current.Preferences.ExplorerDirectory;
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "bulk-recycle"); Directory.CreateDirectory(folder);
        string firstPath = Path.Combine(folder, "First.html"), secondPath = Path.Combine(folder, "Second.html"), textPath = Path.Combine(folder, "Readme.md");
        foreach (string path in new[] { firstPath, secondPath, textPath }) File.WriteAllText(path, "<p>Saved fixture</p>");
        var first = TextFiles.Open(firstPath); first.Edit("<p>Unsaved changes</p>"); first.AutoSave = true;
        var second = TextFiles.Open(secondPath);
        var draft = new Document { DraftName = "Unsaved recycle fixture", Text = "Keep until confirmed" };
        window.Documents.Add(first); window.Documents.Add(second); window.Documents.Add(draft);
        try
        {
            window.MultiFileSelectionMenu.IsChecked = true;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            explorer.SetMode(false);
            window.SetDocumentList(true);
            explorer.DocumentSelection.Set(first, true); explorer.DocumentSelection.Set(draft, true);
            var picks = explorer.DocumentSelection.Items;
            int confirmations = 0;
            int canceled = await window.DeleteFiles(picks, (files, drafts, dirty) =>
            {
                confirmations++;
                check(files.SequenceEqual([firstPath]) && drafts.SequenceEqual([draft.Name]) && dirty, "Bulk confirmation distinguishes saved files, unsaved drafts and dirty changes");
                window.FlushAutoSaves(true); return false;
            });
            check(canceled == 0 && confirmations == 1 && File.Exists(firstPath) && window.Documents.Contains(draft) && explorer.DocumentSelection.Items.Count == 2,
                "Cancel preserves checked files, unsaved documents and their selection");
            check(window.CreateDocumentMenu(first, true).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "_Delete Selected Files…")) && window.DeleteSelectedFilesMenu.IsEnabled,
                "Document List context menu and File menu expose bulk deletion in selection mode");
            check(await window.DeleteFiles(picks, (_, _, _) => true) == 2 && !File.Exists(firstPath) && !window.Documents.Contains(first) && !window.Documents.Contains(draft),
                "Confirmed mixed deletion recycles the saved file and closes the unsaved document without a Save dialog");
            window.FlushAutoSaves(true);
            check(!File.Exists(firstPath) && explorer.DocumentSelection.Items.Count == 0 && File.Exists(secondPath) && window.Documents.Contains(original),
                "Deleted files are not recreated by autosave and unchecked documents remain intact");

            await explorer.SetFolderAsync(folder); explorer.SetMode(true);
            explorer.ExplorerSelection.Set(new PromptEntry(secondPath, false), true);
            explorer.ExplorerSelection.Set(new PromptEntry(textPath, false), true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var markdownBox = NavigationSelfTest.Descendants(explorer.Tree).OfType<CombineSelectionBox>().Single(box => box.Item is PromptEntry entry && entry.Path == textPath);
            check(markdownBox.Visibility == Visibility.Visible && markdownBox.IsChecked == true, "Explorer permits selecting Markdown and other file types for deletion");
            bool rejected = false;
            try { await window.CombineSelectedDocuments(); } catch (ArgumentException) { rejected = true; }
            check(rejected && File.Exists(textPath), "Combine explains unsupported selections without changing non-HTML files");
            await window.OpenExplorerFile(textPath, CancellationToken.None);
            check(await window.DeleteFiles(explorer.ExplorerSelection.Items, (_, _, _) => true) == 2 && !File.Exists(secondPath) && !File.Exists(textPath) && window.CurrentView != null,
                "Explorer batch deletion recycles both file types, closes open tabs and exits deleted previews");
            check(explorer.ExplorerSelection.Items.Count == 0, "Successfully recycled Explorer files are removed from the checked selection");

            window.Documents.Add(draft);
            window.MultiFileSelectionMenu.IsChecked = false;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(window.CreateDocumentMenu(draft).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Delete…")).IsEnabled,
                "An unsaved document can be deleted from its own context menu even when multi-file selection is off");
            bool dialogChecked = false;
            var choose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            choose.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.Cast<Window>().SingleOrDefault(w => w.Title == "Delete Selected Files - Sin - AI Prompt");
                if (dialog?.IsLoaded != true) return;
                choose.Stop();
                var controls = ScreenCaptureSelfTest.Controls(dialog).ToArray();
                dialogChecked = controls.OfType<TextBlock>().Any(text => text.Text.Contains("Close unsaved: " + draft.Name)) &&
                    controls.OfType<Button>().Any(button => Equals(button.Content, "Cancel") && button.IsDefault);
                controls.OfType<Button>().Single(button => Equals(button.Content, "Delete")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            try { choose.Start(); check(await window.DeleteFiles([draft]) == 1 && dialogChecked, "Deleting an unsaved document shows its name and a Cancel-default confirmation before closing it"); }
            finally { choose.Stop(); }

            File.WriteAllText(firstPath, "Fixture"); File.WriteAllText(secondPath, "Locked fixture");
            window.MultiFileSelectionMenu.IsChecked = true;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            explorer.ExplorerSelection.Set(new PromptEntry(firstPath, false), true); explorer.ExplorerSelection.Set(new PromptEntry(secondPath, false), true);
            // A missing later file captures partial progress without a Windows error dialog.
            File.Delete(secondPath);
            bool failed = false;
            try { await window.DeleteFiles(explorer.ExplorerSelection.Items, (_, _, _) => true); } catch (IOException) { failed = true; }
            check(failed && !File.Exists(firstPath) && explorer.ExplorerSelection.Items.Count == 1 && ((PromptEntry)explorer.ExplorerSelection.Items[0]).Path == secondPath,
                "A batch failure retains unprocessed selections and does not undo successful recycling");
        }
        finally
        {
            window.ActiveDocument = original;
            foreach (var document in new[] { first, second, draft }) if (window.Documents.Contains(document)) window.RemoveDocument(document);
            window.MultiFileSelectionMenu.IsChecked = false;
            window.MultiFileSelectionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (root.Length > 0) await explorer.SetFolderAsync(root);
            explorer.SetMode(mode); window.SetDocumentList(navigation);
        }
    }
}
