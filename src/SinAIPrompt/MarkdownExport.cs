using System.IO;
using System.Text;
using System.Text.Json;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public sealed partial class EditorView
{
    internal async Task<string> ConvertMarkdownAsync(string text)
    {
        await initialized.Task;
        string response = await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", Json(new {
            expression = $"import('./markdown.js').then(module=>module.markdownDocument({Json(text)}))", awaitPromise = true, returnByValue = true }));
        using var result = JsonDocument.Parse(response);
        if (!result.RootElement.GetProperty("result").TryGetProperty("value", out var value)) throw new IOException("Could not convert the Markdown document.");
        return value.GetString() ?? throw new IOException("Markdown conversion returned no HTML.");
    }
    internal async Task SaveMarkdownAsync(string path, bool absoluteImages = false)
    {
        if (string.Equals(Path.GetFullPath(path), Document.Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a separate .md file so the HTML document is preserved.");
        await initialized.Task; await FlushAsync();
        if (!IsVisual) LoadHtml();
        saveAsPath = path;
        try
        {
            string? folder = absoluteImages ? new Uri(Path.GetDirectoryName(Path.GetFullPath(path))! + Path.DirectorySeparatorChar).AbsoluteUri : null;
            string key = JsonSerializer.Deserialize<string>(await Browser.ExecuteScriptAsync($"window.editor.beginMarkdown({Json(folder)})"))!;
            string markdown = await AwaitExportAsync(key);
            await Task.Run(() => TextFiles.AtomicWrite(path, new UTF8Encoding(false).GetBytes(markdown)));
        }
        finally { saveAsPath = null; }
    }
}
