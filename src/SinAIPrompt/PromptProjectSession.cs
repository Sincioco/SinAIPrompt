using System.IO;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal sealed record ProjectReferenceMove(string oldUrl, string newUrl, bool directory);

// Coordinates one move using explicit open models and an existing HTML parser.
// The window owns navigation/autosave; Core owns the filesystem transaction.
internal static class PromptProjectSession
{
    internal sealed record OpenDocument(Document Document, Func<EditorView?> GetEditor)
    {
        internal EditorView? Editor => GetEditor();
    }
    internal sealed record Outcome(IReadOnlyList<PromptProjectMove.Result> Files, IReadOnlyList<string> RefreshWarnings);

    internal static async Task<Outcome> MoveAsync(
        IReadOnlyList<string> paths, string destination, IReadOnlyList<OpenDocument> open,
        EditorView parser, IProgress<(int Done, int Total)> progress, Action changed)
    {
        foreach (var item in open) if (item.Editor != null) await item.Editor.FlushAsync();
        var snapshots = await Task.Run(() => paths.Select(path => PromptProjectMove.Read(path, destination)).ToArray());
        foreach (var snapshot in snapshots)
            if (open.Any(item => Same(item.Document.Path, snapshot.SourcePath) && item.Document.Fingerprint != null && item.Document.Fingerprint != snapshot.Fingerprint))
                throw new IOException("A selected document changed outside Sin - AI Prompt. Reload it before moving: " + snapshot.SourcePath);
        var moves = snapshots.SelectMany(snapshot => snapshot.SourceAssets == null
            ? new[] { new ProjectReferenceMove(Url(snapshot.SourcePath), Url(snapshot.DestinationPath), false) }
            : new[] { new ProjectReferenceMove(Url(snapshot.SourcePath), Url(snapshot.DestinationPath), false),
                new ProjectReferenceMove(Url(snapshot.SourceAssets) + "/", Url(snapshot.DestinationAssets!) + "/", true) }).ToArray();
        for (int i = 0; i < snapshots.Length; i++)
        {
            var snapshot = snapshots[i];
            snapshots[i] = snapshot with { Html = await parser.RewriteProjectReferencesAsync(snapshot.Html, snapshot.SourcePath, snapshot.DestinationPath, moves) };
        }
        var buffers = new Dictionary<Document, (string Original, string Updated, bool CleanLocked)>();
        foreach (var item in open)
        {
            var snapshot = snapshots.First(snapshot => Same(item.Document.Path, snapshot.SourcePath));
            string original = item.Document.Text;
            buffers[item.Document] = (original, TextFiles.Normalize(await parser.RewriteProjectReferencesAsync(original, snapshot.SourcePath, snapshot.DestinationPath, moves)), item.Document.IsReadOnly && !item.Document.Dirty);
        }
        var results = await Task.Run(() => PromptProjectMove.Commit(snapshots, (done, total) => progress.Report((done, total))));
        // Publish model paths together before any awaited browser refresh. Recovery
        // and pending edits now refer to the successfully published destinations.
        var affected = open.Select(item => (Item: item, Result: results.First(result => Same(item.Document.Path, result.SourcePath)))).ToArray();
        foreach (var (item, result) in affected)
        {
            var document = item.Document;
            document.Path = result.DestinationPath; document.SavedText = result.Html;
            document.Fingerprint = result.Fingerprint; document.CreatedUtc = result.CreatedUtc;
            document.ModifiedUtc = result.ModifiedUtc; document.IsReadOnly = result.IsReadOnly;
            var buffer = buffers[document];
            if (document.Text == buffer.Original) document.Text = buffer.Updated;
            document.Notify();
        }
        changed();
        var warnings = new List<string>();
        foreach (var (item, result) in affected)
        {
            try
            {
                if (item.Editor != null) await item.Editor.MoveProjectReferencesAsync(result.SourcePath, result.DestinationPath, moves, buffers[item.Document].CleanLocked);
                else if (item.Document.Text != buffers[item.Document].Updated)
                    item.Document.Text = TextFiles.Normalize(await parser.RewriteProjectReferencesAsync(item.Document.Text, result.SourcePath, result.DestinationPath, moves));
            }
            catch (Exception error)
            {
                item.Document.AutoSave = false;
                warnings.Add(item.Document.Name + ": " + error.Message + " The file moved, but this tab could not refresh. Its unsaved text is still open; autosave is off until its references can be checked.");
            }
            item.Document.Notify();
        }
        changed();
        return new(results, warnings);
    }

    static string Url(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
    static bool Same(string? first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
