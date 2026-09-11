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
        string folder = app.Store.DirectoryPath;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); results.Add("PASS " + name); File.WriteAllLines(Path.Combine(folder, "ui-test-results.txt"), results); }
        try
        {
            string documents = Path.Combine(folder, "documents"); Directory.CreateDirectory(documents);
            var settings = new Settings { AutoSaveDirectory = documents };
            var first = DocumentFactory.Create(settings); settings.NextDocumentNumber = 1;
            var second = DocumentFactory.Create(settings);
            Check(first.Path!.EndsWith("Prompt 1.html") && second.Path!.EndsWith("Prompt 2.html"), "HTML numbering avoids existing files");
            first.Text = "<!doctype html><html><head><title>Test prompt</title></head><body><h1>Sin - AI Prompt</h1><p>Write, format, and illustrate your ideas.</p></body></html>";
            TextFiles.Save(first, first.Path!);
            window = new MainWindow(new WindowSession { Documents = [first], Width = 1280, Height = 840 });
            app.MainWindow = window; window.Show();
            var view = window.CurrentView!;
            bool loaded = false;
            for (int i = 0; i < 200; i++)
            {
                await Task.Delay(100);
                if (view.Browser.CoreWebView2 != null && await view.Browser.ExecuteScriptAsync("!!window.editor && !!document.querySelector('#document').contentDocument?.body?.isContentEditable") == "true") { loaded = true; break; }
            }
            Check(loaded, "Native WebView2 HTML editor initializes from installed Visual Studio components");
            var exceptionEvent = view.Browser.CoreWebView2!.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
            var browserErrors = new List<string>();
            exceptionEvent.DevToolsProtocolEventReceived += (_, e) => browserErrors.Add(e.ParameterObjectAsJson);
            await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
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
            await view.FlushAsync();
            Check(first.Text.Contains("data-sin-annotation") && first.Text.Contains("data-sin-code"), "Native document receives annotation and code metadata");
            Check(await window.SaveDocument(first), "Native save synchronizes visual HTML");
            Check(TextFiles.Open(first.Path!).Text.Contains("data-sin-annotation"), "HTML file retains editable annotation layers");
            var portable = await view.ExportAsync();
            File.WriteAllText(Path.Combine(folder, "standalone.html"), portable);
            Check(portable.Contains("data:image/png;base64,"), "Standalone export contains embedded PNG");
            await view.SetSourceAsync(true);
            Check(!view.IsVisual && view.Editor.IsVisible, "Native View Source exposes editable HTML");
            view.Editor.Text = view.Editor.Text.Replace("Write, format", "Create, format");
            await view.SetSourceAsync(false); await Task.Delay(200);
            Check((await view.Browser.ExecuteScriptAsync("window.editor.html()")).Contains("Create, format"), "Source changes appear in visual editor");
            window.SetDocumentList(true);Check(window.IsDocumentList, "Cloned Document List navigation works");
            window.SetDocumentList(false);
            Check(await window.SaveDocument(first), "Save after source edits succeeds");
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
            Check(Directory.GetFiles(Path.Combine(movedFolder, "Saved prompt"), "*.png").Length > 0 && File.Exists(Path.Combine(documents, "Prompt 1.html")), "Save As retains separate PNG storage and preserves the original file");
            Check(app.Store.Read<List<JsonElement>>("templates.json").Count > 0, "Object templates persist as JSON outside HTML documents");
            Check(app.SaveState(), "Session recovery written as JSON");
            var recovered = app.Store.Read<Session>("session.json");
            Check(recovered.Windows[0].Documents[0].Text.Contains("data-sin-annotation"), "Session recovery includes image layers and code blocks");
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
