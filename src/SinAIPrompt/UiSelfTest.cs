using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class UiSelfTest
{
    public static async Task Run(App app)
    {
        var results = new List<string>(); MainWindow? window = null;
        using var startup = new EditorStartupSelfTest();
        string folder = app.Store.DirectoryPath;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); results.Add("PASS " + name); File.WriteAllLines(Path.Combine(folder, "ui-test-results.txt"), results); }
        try
        {
            await LinkPreviewSelfTest.Run(Check);
            await HtmlAssetsSelfTest.Run(folder, Check);
            string documents = Path.Combine(folder, "documents"); Directory.CreateDirectory(documents);
            var settings = new Settings { AutoSaveDirectory = documents };
            var first = DocumentFactory.Create(settings); settings.NextDocumentNumber = 1;
            var second = DocumentFactory.Create(settings);
            Check(first.Path!.EndsWith("Prompt 1.html") && second.Path!.EndsWith("Prompt 2.html"), "HTML numbering avoids existing files");
            first.Text = "<!doctype html><html><head><title>Test prompt</title></head><body><h1>Sin - AI Prompt</h1><p>Write, format, and illustrate your ideas.</p></body></html>";
            TextFiles.Save(first, first.Path!);
            first.Path = null; first.AutoSave = false;
            second.Text = "<p>Recovered unsaved second tab</p>"; second.AutoSave = false;
            string restoredHtml = "<p>Inactive recovery text</p><!--" + new string('A', 100_000) + "-->";
            var inactive = Enumerable.Range(3, 98).Select(n => new Document { UntitledNumber = n, Text = restoredHtml, SavedText = restoredHtml }).ToArray();
            TextFiles.Save(inactive[0], Path.Combine(documents, "Inactive.html"));
            File.WriteAllText(inactive[0].Path!, "<p>Updated on disk before launch</p>");
            var restoreClock = System.Diagnostics.Stopwatch.StartNew();
            window = new MainWindow(new WindowSession { Documents = [second, .. inactive, first], ActiveIndex = 99, Width = 1280, Height = 840, DocumentList = true });
            results.Add($"100-document window construction: {restoreClock.ElapsedMilliseconds} ms");
            app.MainWindow = window; window.Show();
            Check(window.ActiveDocument == first && window.Documents.Contains(second) && second.Dirty, "Startup selects the saved active tab and preserves inactive unsaved documents");
            Check(window.Documents.Count == 100 && window.CreatedEditorCount == 1 && inactive[0].Text == restoredHtml, "100-document startup creates only the last active editor and defers inactive file reads");
            var view = window.CurrentView!;
            Check(view.Editor.Text.Length == 0 && first.Text.Contains("Sin - AI Prompt"), "Startup keeps restored HTML in the document without filling the hidden source editor");
            bool loaded = false;
            for (int i = 0; i < 200; i++)
            {
                await Task.Delay(100);
                if (view.Browser.CoreWebView2 != null && await view.Browser.ExecuteScriptAsync("!!window.editor && !!document.querySelector('#document').contentDocument?.body?.isContentEditable") == "true") { loaded = true; break; }
            }
            Check(loaded, "Native WebView2 HTML editor initializes from installed Visual Studio components");
            await BundledDocumentsSelfTest.Run(window, Check);
            await NavigationSelfTest.Run(window, Check);
            EmojiPickerSelfTest.Run(window, Check);
            await BrandingSelfTest.Run(window, Check);
            await ScreenCaptureSelfTest.Run(window, Check);
            await DocumentCommandSelfTest.NewPromptReady(window, Check);
            await EditorStartupSelfTest.ClosingDuringStartup(window, Check);
            await DocumentCommandSelfTest.RenameDraft(window, Check);
            Check(await window.SaveDocument(first, destinationPath: Path.Combine(documents, "Prompt 1.html")), "First save changes the unsaved prompt's asset folder");
            await DocumentCommandSelfTest.SaveShortcut(window, Check);
            await HtmlAssetsSelfTest.Paste(window, Check);
            await HtmlAssetsSelfTest.References(window, Check);
            await DocumentCommandSelfTest.Appearance(window, Check);
            await ScreenCaptureSelfTest.RegionShortcut(window, Check);
            await DocumentContentSelfTest.Run(window, Check);
            await MarkdownImportSelfTest.Run(window, Check);
            await SearchSelfTest.Source(window, Check);
            var exceptionEvent = view.Browser.CoreWebView2!.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
            var browserErrors = new List<string>();
            exceptionEvent.DevToolsProtocolEventReceived += (_, e) => browserErrors.Add(e.ParameterObjectAsJson);
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
            int hiddenSourceUpdates = 0;
            view.Editor.TextChanged += (_, _) => hiddenSourceUpdates++;
            await view.Browser.ExecuteScriptAsync("import('./self-test.js').then(m=>m.run()).then(r=>window.smokeResult={results:r}).catch(e=>window.smokeResult={error:e.stack||e.message})");
            JsonElement result = default;
            for (int i = 0; i < 1200; i++)
            {
                string json = await view.Browser.ExecuteScriptAsync("window.smokeResult||null");
                if (json != "null") { result = JsonSerializer.Deserialize<JsonElement>(json); break; }
                await Task.Delay(100);
            }
            Check(result.ValueKind == JsonValueKind.Object, "Browser integration checks finish");
            if (result.TryGetProperty("error", out var error)) throw new Exception(error.GetString());
            foreach (var item in result.GetProperty("results").EnumerateArray()) results.Add("PASS " + item.GetString());
            var restoredColors = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(app.Preferences))!;
            Check(restoredColors.RecentColors.Count == 10 && restoredColors.RecentColors.SequenceEqual(app.Preferences.RecentColors), "The ten shared recent colors survive settings serialization");
            Check(hiddenSourceUpdates == 0, "Visual editing does not rebuild the hidden WPF source editor");
            await view.FlushAsync();
            Check(first.Text.Contains("data-sin-annotation") && first.Text.Contains("data-sin-code"), "Native document receives annotation and code metadata");
            Check(await window.SaveDocument(first), "Native save synchronizes visual HTML");
            Check(TextFiles.Open(first.Path!).Text.Contains("data-sin-annotation"), "HTML file retains editable annotation layers");
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Immediate save regression')");
            Check(await window.SaveDocument(first) && File.ReadAllText(first.Path!).Contains("Immediate save regression"), "Saving immediately after typing includes the pending edit");
            first.AutoSave = true;
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Deferred autosave regression')");
            for (int i = 0; i < 50 && !first.Text.Contains("Deferred autosave regression"); i++) await Task.Delay(20);
            Check(window.FlushAutoSaves(true) && File.ReadAllText(first.Path!).Contains("Deferred autosave regression"), "Deferred typing synchronization still queues native autosave");
            first.AutoSave = false;
            var portable = await view.ExportAsync();
            File.WriteAllText(Path.Combine(folder, "standalone.html"), portable);
            Check(portable.Contains("data:image/png;base64,"), "Standalone export contains embedded PNG");
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Source switch regression')");
            await view.SetSourceAsync(true);
            Check(!view.IsVisual && view.Editor.IsVisible, "Native View Source exposes editable HTML");
            view.Editor.Text = view.Editor.Text.Replace("Write, format", "Create, format");
            await Task.Delay(250);
            await view.SetSourceAsync(false); await Task.Delay(200);
            Check((await view.Browser.ExecuteScriptAsync("window.editor.html()")).Contains("Create, format"), "Source changes appear in visual editor");
            window.SetDocumentList(true);Check(window.IsDocumentList, "Cloned Document List navigation works");
            window.SetDocumentList(false);
            Check(await window.SaveDocument(first), "Save after source edits succeeds");
            await DocumentCommandSelfTest.DuplicateAndRevert(window, Check);
            await DocumentWorkflowSelfTest.Run(window, Check);
            await PromptExplorerSelfTest.Run(window, Check);
            string saved = File.ReadAllText(first.Path!); File.AppendAllText(first.Path!, "<!-- external -->");
            bool conflict = false;try { TextFiles.Save(first, first.Path!); } catch (IOException) { conflict = true; }
            Check(conflict, "External file conflict prevents silent overwrite");
            File.WriteAllText(first.Path!, saved);first.Fingerprint=TextFiles.Hash(File.ReadAllBytes(first.Path!));
            string imageFolder = Path.Combine(documents, "Prompt 1");
            Check(Directory.Exists(imageFolder) && Directory.GetFiles(imageFolder, "*.png").Length > 0, "Separate PNG stored under HTML filename folder");
            string movedFolder = Path.Combine(folder, "saved-as");Directory.CreateDirectory(movedFolder);
            // Exercise a real referenced image, preserving the separate-file choice across folders.
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertHTML','<p><img src=\"' + document.querySelector(\"#document\").contentDocument.querySelector(\"img\").getAttribute(\"src\") + '\" data-sin-storage=\"separate\"></p>')");
            string copied = Path.Combine(movedFolder, "Saved prompt.html");
            Check(await window.SaveDocument(first, destinationPath: copied), "Save As copies document and assets to another folder");
            await view.Browser.ExecuteScriptAsync("window.savedImagesReady=null;window.editor.ready().then(()=>Promise.all([...document.querySelector('#document').contentDocument.images].map(image=>image.decode()))).then(()=>window.savedImagesReady=true).catch(()=>window.savedImagesReady=false)");
            string savedImagesReady = "null";
            for (int i = 0; i < 100 && savedImagesReady == "null"; i++) { await Task.Delay(20); savedImagesReady = await view.Browser.ExecuteScriptAsync("window.savedImagesReady"); }
            Check(savedImagesReady == "true", "Images remain visible after Save As changes the asset folder");
            Check(Directory.GetFiles(Path.Combine(movedFolder, "Saved prompt"), "*.png").Length > 0 && File.Exists(Path.Combine(documents, "Prompt 1.html")), "Save As retains separate PNG storage and preserves the original file");
            var imageNames = Directory.GetFiles(Path.Combine(movedFolder, "Saved prompt"), "*.png").Select(Path.GetFileName).Order().ToArray();
            await view.Browser.ExecuteScriptAsync("window.editor.command('insertText','Keep this unsaved rename edit')");
            await window.RenameDocumentFile(first, "Renamed # prompt.html");
            string renamedFolder = Path.Combine(movedFolder, "Renamed # prompt");
            Check(File.Exists(first.Path!) && !File.Exists(copied) && !Directory.Exists(Path.Combine(movedFolder, "Saved prompt")) && Directory.GetFiles(renamedFolder, "*.png").Select(Path.GetFileName).Order().SequenceEqual(imageNames), "Rename moves the HTML file and its matching image folder without copying images");
            Check(File.ReadAllText(first.Path!).Contains("Renamed%20%23%20prompt/image-") && first.Text.Contains("Renamed%20%23%20prompt/image-"), "Rename updates saved and open image references with URL-encoded folder names");
            Check(first.Dirty && first.Text.Contains("Keep this unsaved rename edit") && !File.ReadAllText(first.Path!).Contains("Keep this unsaved rename edit"), "Rename preserves unsaved edits without writing them into the saved HTML");
            await view.Browser.ExecuteScriptAsync("window.savedImagesReady=null;window.editor.ready().then(()=>Promise.all([...document.querySelector('#document').contentDocument.images].map(image=>image.decode()))).then(()=>window.savedImagesReady=true).catch(()=>window.savedImagesReady=false)");
            savedImagesReady = "null";
            for (int i = 0; i < 100 && savedImagesReady == "null"; i++) { await Task.Delay(20); savedImagesReady = await view.Browser.ExecuteScriptAsync("window.savedImagesReady"); }
            Check(savedImagesReady == "true", "Images remain visible immediately after renaming");
            string occupiedImages = Path.Combine(movedFolder, "Occupied");Directory.CreateDirectory(occupiedImages);File.WriteAllText(Path.Combine(occupiedImages, "keep.txt"), "Keep");
            bool renameBlocked = false;try { await window.RenameDocumentFile(first, "Occupied.html"); } catch (IOException) { renameBlocked = true; }
            Check(renameBlocked && File.Exists(first.Path!) && Directory.Exists(renamedFolder) && !File.Exists(Path.Combine(movedFolder, "Occupied.html")) && File.ReadAllText(Path.Combine(occupiedImages, "keep.txt")) == "Keep", "Rename rejects an occupied image folder before moving any files");
            string beforeFailedRename = first.Path!;
            File.SetAttributes(beforeFailedRename, File.GetAttributes(beforeFailedRename) | FileAttributes.ReadOnly);
            bool renameRolledBack = false;
            try { await window.RenameDocumentFile(first, "Cannot write.html"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { renameRolledBack = true; }
            finally { File.SetAttributes(beforeFailedRename, FileAttributes.Normal); }
            Check(renameRolledBack && File.Exists(beforeFailedRename) && Directory.Exists(renamedFolder) && !File.Exists(Path.Combine(movedFolder, "Cannot write.html")) && !Directory.Exists(Path.Combine(movedFolder, "Cannot write")), "Failed HTML rewrite rolls back both the file and image folder rename");
            await view.SetSourceAsync(true);
            await window.RenameDocumentFile(first, "renamed # prompt.html");
            Check(Directory.GetDirectories(movedFolder).Any(path => Path.GetFileName(path) == "renamed # prompt") && view.Editor.Text.Contains("renamed%20%23%20prompt/image-") && first.Dirty, "Case-only rename updates the folder and source editor while preserving unsaved edits");
            await view.SetSourceAsync(false);
            Check(app.Store.Read<List<JsonElement>>("templates.json").Count > 0, "Object templates persist as JSON outside HTML documents");
            Check(app.SaveState(), "Session recovery written as JSON");
            var recovered = app.Store.Read<Session>("session.json");
            Check(recovered.Windows[0].Documents.Single(d => d.Id == first.Id).Text.Contains("data-sin-annotation"), "Session recovery includes image layers and code blocks");
            Check(recovered.Windows[0].Documents.Single(d => d.Id == second.Id).Text == second.Text && recovered.Windows[0].Documents.Single(d => d.Id == second.Id).Dirty, "Streaming session reads preserve inactive unsaved text");
            Check(await window.SaveDocument(second) && window.CreatedEditorCount == 1 && File.ReadAllText(second.Path!).Contains("Recovered unsaved second tab"), "Saving an unvisited tab preserves edits without creating its editor");
            TextFiles.Save(inactive[1], Path.Combine(documents, "Unvisited.html"));
            string unvisitedFolder = Path.Combine(documents, "Unvisited"); Directory.CreateDirectory(unvisitedFolder);
            File.Copy(Directory.GetFiles(Path.Combine(movedFolder, "renamed # prompt"), "*.png")[0], Path.Combine(unvisitedFolder, "image.png"));
            inactive[1].Text = "<p><img src=\"Unvisited/image.png\"></p>"; TextFiles.Save(inactive[1], inactive[1].Path!);
            await window.RenameDocumentFile(inactive[1], "Unvisited renamed.html");
            Check(window.CreatedEditorCount == 1 && inactive[1].Text.Contains("Unvisited%20renamed/image.png") && File.Exists(Path.Combine(documents, "Unvisited renamed", "image.png")), "Renaming an unvisited tab updates its image folder and references without loading its editor");
            window.ActiveDocument = inactive[0];
            Check(window.CreatedEditorCount == 2 && inactive[0].Text.Contains("Updated on disk before launch"), "Selecting an inactive tab creates its editor and reads its latest saved file");
            window.ActiveDocument = first;
            Check(await window.CloseDocument(inactive[^1]) && window.CreatedEditorCount == 2, "Closing an unvisited tab does not initialize its editor");
            StorageLocation.TestPointerPath = Path.Combine(folder, "test-data-location.json");
            string persistent = Path.Combine(folder, "persistent-profile");
            app.UseStorageFolder(persistent);
            Check(app.Store.DirectoryPath == persistent && StorageLocation.Read() == persistent, "Configurable application storage persists its location beside the app");
            Check(File.Exists(Path.Combine(persistent, "templates.json")) && File.Exists(Path.Combine(persistent, "session.json")), "Storage relocation copies settings, templates, and recovery JSON");
            Check(File.Exists(Path.Combine(folder, "settings.json")), "Storage relocation preserves the previous folder as a backup");
            string occupied = Path.Combine(folder, "occupied-profile");Directory.CreateDirectory(occupied);File.WriteAllText(Path.Combine(occupied, "templates.json"), "[]");
            bool blocked = false;try { app.UseStorageFolder(occupied); } catch (IOException) { blocked = true; }
            Check(blocked && !File.Exists(Path.Combine(occupied, "settings.json")), "Storage relocation preflights conflicts before copying any files");
            Check(browserErrors.Count == 0, "No browser runtime exceptions");
            Check(startup.Errors.Count == 0, "No unexpected native editor-startup dialogs: " + string.Join(" | ", startup.Errors));
            using (var capture = File.Create(Path.Combine(folder, "editor.png"))) await view.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
            results.Add("ALL CHECKS PASSED");
            app.Shutdown(0);
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
            if (window?.CurrentView?.Browser.CoreWebView2 != null)
            {
                File.WriteAllText(Path.Combine(folder, "failure.html"), JsonSerializer.Deserialize<string>(await window.CurrentView.Browser.ExecuteScriptAsync("window.editor?.html()||document.body.innerHTML")));
                try { using var capture = File.Create(Path.Combine(folder, "failure.png")); await window.CurrentView.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture); } catch { }
            }
            app.Shutdown(1);
        }
        finally { File.WriteAllLines(Path.Combine(folder, "ui-test-results.txt"), results); }
    }
}
