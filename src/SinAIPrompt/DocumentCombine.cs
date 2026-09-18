using System.IO;
using System.Text.Json;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Combination owns snapshots, name reservation and HTML composition. Existing
// editor/native bridge handles image reads; sources are never modified or saved.
internal static class DocumentCombine
{
    internal sealed record Source(string html, string @base);

    internal static async Task<Document> CreateAsync(IReadOnlyList<Source> sources, EditorView processor, string? folder, IEnumerable<string> openNames)
    {
        if (sources.Count < 2) throw new ArgumentException("Select at least two HTML documents using the checkboxes beside their names.");
        await processor.Initialization;
        string expression = "import('./document-combine.js').then(module=>module.combineDocuments(" + JsonSerializer.Serialize(sources) + "))";
        string response = await processor.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
        using var result = JsonDocument.Parse(response);
        if (result.RootElement.TryGetProperty("exceptionDetails", out var error))
            throw new IOException("Could not combine the documents. Check their images and stylesheets. " + error.ToString());
        string html = result.RootElement.GetProperty("result").GetProperty("value").GetString()!;
        return await Task.Run(() => Save(html, folder, openNames.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    static Document Save(string html, string? folder, HashSet<string> names)
    {
        string stem = DateTime.Now.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture) + " - Combined";
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        for (int number = 0; ; number++)
        {
            string name = stem + (number == 0 ? "" : " " + number);
            if (names.Contains(name) || names.Contains(name + ".html")) continue;
            var document = new Document { DraftName = name, Text = html, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
            if (string.IsNullOrWhiteSpace(folder)) return document;
            string path = Path.Combine(folder, name + ".html");
            if (Directory.Exists(path) || Directory.Exists(Path.Combine(folder, name))) continue;
            try { using var reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            document.Path = path; document.Fingerprint = TextFiles.Hash([]);
            try { TextFiles.Save(document, path); document.AutoSave = true; return document; }
            catch
            {
                // Only remove our empty reservation, never a concurrent edit.
                if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
                throw;
            }
        }
    }
}
