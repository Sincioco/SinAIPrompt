using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentWorkflowSelfTest
{
    internal static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!;
        var now = DateTime.UtcNow;
        var old = new Document { DraftName = "Old", CreatedUtc = now.AddDays(-3), ModifiedUtc = now };
        var recent = new Document { DraftName = "Recent", CreatedUtc = now, ModifiedUtc = now.AddDays(-1) };
        var documents = new ObservableCollection<Document> { recent, old };
        var settings = new Settings();
        using (var order = new DocumentOrder(documents, settings, window.Dispatcher, () => { }))
        {
            order.Apply(); check(documents[0] == old, "Default document order is newest modification first");
            order.SetMode("created"); check(documents[0] == recent, "Document order can use creation date instead");
            order.TogglePin(old); check(documents[0] == old, "Pinned documents stay above the selected date order");
            var restored = JsonSerializer.Deserialize<Document>(JsonSerializer.Serialize(old))!;
            check(restored.Pinned && restored.CreatedUtc == old.CreatedUtc, "Pins and cached sort dates survive session serialization");
            order.TogglePin(old); check(documents[0] == recent, "Unpin restores the selected document date order");
        }
        string folder = Path.Combine(App.Current.Store.DirectoryPath, "rename-from-ribbon"); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Before.html"), images = Path.Combine(folder, "Before"); Directory.CreateDirectory(images);
        string png = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(original.Path!)!, Path.GetFileNameWithoutExtension(original.Path!)), "*.png")[0];
        File.Copy(png, Path.Combine(images, "picture.png"));
        File.WriteAllText(source, "<p>One image rename</p><img src=\"Before/picture.png\" data-sin-storage=\"separate\">");
        window.OpenPaths([source]); var renamed = window.ActiveDocument!; var view = window.CurrentView!;
        await view.ExportAsync();
        await view.Browser.ExecuteScriptAsync("window.editor.command('insertText',' unsaved content')");
        var completed = new TaskCompletionSource();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.Cast<Window>().SingleOrDefault(w => w.Title == "Rename File - Sin - AI Prompt");
            if (dialog?.IsLoaded != true) return;
            timer.Stop();
            var controls = ScreenCaptureSelfTest.Controls(dialog).ToArray();
            controls.OfType<TextBox>().Single().Text = "After # rename.html";
            dialog.Closed += (_, _) => completed.TrySetResult();
            controls.OfType<Button>().Single(b => b.Content.ToString() == "Rename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            timer.Start();
            await view.Browser.ExecuteScriptAsync("document.querySelector('[data-native-command=rename]').click()");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(12));
            await view.FlushAsync();
            check(renamed.Name == "After # rename.html" && !File.Exists(source) && File.Exists(Path.Combine(folder, "After # rename", "picture.png")),
                "Ribbon Rename completes its actual modal dialog and moves the HTML plus image folder without hanging");
            check(renamed.Text.Contains("After%20%23%20rename/picture.png") && renamed.Text.Contains("unsaved content") && renamed.Dirty,
                "Ribbon Rename refreshes encoded image references and preserves the unsaved edit");
            string markdown = Path.Combine(folder, "Markdown export.md");
            await view.SaveMarkdownAsync(markdown);
            string output = File.ReadAllText(markdown);
            check(output.Contains("![") && output.Contains("Markdown%20export/image-") && !output.Contains("base64") && Directory.GetFiles(Path.Combine(folder, "Markdown export"), "*.png").Length == 1,
                "Save As Markdown writes referenced PNG assets beside its .md file");
            check(renamed.Path!.EndsWith("After # rename.html") && renamed.Dirty && File.Exists(renamed.Path),
                "Markdown export preserves the open HTML path and unsaved edit state");
            string input = Path.Combine(folder, "Clipboard.md");
            File.WriteAllText(input, "# Clipboard Markdown\n\n**Text**\n\n![Picture](After%20%23%20rename/picture.png)");
            var previousClipboard = EditorClipboard.TestData;
            try
            {
                var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { input }); EditorClipboard.TestData = data;
                await view.Browser.ExecuteScriptAsync("window.editor.load('')");
                await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"window.editor.ready().then(()=>window.editor.command('paste'))\",\"awaitPromise\":true}");
                check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.querySelector('h1')?.textContent==='Clipboard Markdown' && document.querySelector('#document').contentDocument.images[0]?.src.startsWith('data:image/png;base64,')") == "true",
                    "Pasting a clipboard Markdown file formats its contents and embeds relative local images");
            }
            finally { EditorClipboard.TestData = previousClipboard; }
        }
        finally { timer.Stop(); window.ActiveDocument = original; window.RemoveDocument(renamed); }
    }
}
