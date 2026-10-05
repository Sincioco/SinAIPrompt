using System.Security.Cryptography;

namespace SinAIPrompt.Core;

// Owns disk publication only. Callers rewrite detached HTML and update open models.
public static class PromptProjectMove
{
    public sealed record Snapshot(string SourcePath, string DestinationPath, string? SourceAssets,
        string? DestinationAssets, string Html, string EncodingName, string NewLine,
        string Fingerprint, FileAttributes Attributes);
    public sealed record Result(string SourcePath, string DestinationPath, string Html, string Fingerprint,
        DateTime CreatedUtc, DateTime ModifiedUtc, bool IsReadOnly, string? CleanupWarning = null);

    sealed class Prepared(Snapshot snapshot, string stage, string recovery)
    {
        public Snapshot Snapshot { get; } = snapshot;
        public string Stage { get; } = stage;
        public string Recovery { get; } = recovery;
        public string StagedHtml => Path.Combine(Stage, "document.html");
        public string StagedAssets => Path.Combine(Stage, "assets");
        public string OriginalHtml => Path.Combine(Recovery, "document.html");
        public string OriginalAssets => Path.Combine(Recovery, "assets");
        public bool HtmlBackedUp, AssetsBackedUp, HtmlPublished, AssetsPublished;
        public Result Result { get; set; } = null!;
    }

    public static Snapshot Read(string sourcePath, string destinationFolder)
    {
        string source = Path.GetFullPath(sourcePath), destination = Path.Combine(Path.GetFullPath(destinationFolder), Path.GetFileName(source));
        if (!Path.GetExtension(source).Equals(".html", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Only HTML documents can be moved into a project.");
        var document = TextFiles.Open(source);
        string assets = AssetFolder(source), targetAssets = AssetFolder(destination);
        return new(source, destination, Directory.Exists(assets) ? assets : null, Directory.Exists(assets) ? targetAssets : null,
            File.ReadAllText(source, TextFiles.EncodingFor(document.EncodingName)), document.EncodingName, document.NewLine,
            document.Fingerprint!, File.GetAttributes(source));
    }

    public static IReadOnlyList<Result> Commit(IReadOnlyList<Snapshot> snapshots, Action<int, int>? progress = null)
    {
        Preflight(snapshots);
        var prepared = new List<Prepared>();
        try
        {
            foreach (var snapshot in snapshots)
            {
                var item = new Prepared(snapshot, TemporaryPath(Path.GetDirectoryName(snapshot.DestinationPath)!),
                    TemporaryPath(Path.GetDirectoryName(snapshot.SourcePath)!));
                prepared.Add(item);
                Stage(item);
                progress?.Invoke(prepared.Count, snapshots.Count * 2);
            }
            // Verify the complete batch before reserving original paths. All copies
            // live on the destination volume, so publication uses atomic renames.
            foreach (var item in prepared)
            {
                VerifySource(item.Snapshot);
                if (item.Snapshot.SourceAssets is { } assets) VerifyTree(assets, item.StagedAssets);
            }
            foreach (var item in prepared)
            {
                File.Move(item.Snapshot.SourcePath, item.OriginalHtml); item.HtmlBackedUp = true;
                if (item.Snapshot.SourceAssets is { } assets) { Directory.Move(assets, item.OriginalAssets); item.AssetsBackedUp = true; }
            }
            foreach (var item in prepared)
            {
                VerifySource(item.Snapshot with { SourcePath = item.OriginalHtml });
                if (item.AssetsBackedUp) VerifyTree(item.OriginalAssets, item.StagedAssets);
            }
            for (int index = 0; index < prepared.Count; index++)
            {
                var item = prepared[index];
                if (item.Snapshot.DestinationAssets is { } assets) { Directory.Move(item.StagedAssets, assets); item.AssetsPublished = true; }
                File.Move(item.StagedHtml, item.Snapshot.DestinationPath); item.HtmlPublished = true;
                progress?.Invoke(snapshots.Count + index + 1, snapshots.Count * 2);
            }
        }
        catch (Exception error)
        {
            var failures = new List<string>();
            foreach (var item in prepared.AsEnumerable().Reverse()) Rollback(item, failures);
            if (failures.Count == 0)
            {
                foreach (var item in prepared) { Cleanup(item.Stage, failures); Cleanup(item.Recovery, failures); }
            }
            string recovery = failures.Count == 0 ? " All original documents and image folders remain at their source paths."
                : " Recovery copies were retained where needed: " + string.Join("; ", failures);
            throw new IOException("The project move could not complete. " + error.Message + recovery, error);
        }
        // Publication is complete. A locked recovery copy must not undo a batch
        // after earlier originals have already been cleaned up.
        foreach (var item in prepared)
        {
            var warnings = new List<string>(); Cleanup(item.Recovery, warnings); Cleanup(item.Stage, warnings);
            if (warnings.Count > 0) item.Result = item.Result with { CleanupWarning = string.Join("; ", warnings) };
        }
        return prepared.Select(item => item.Result).ToArray();
    }

    static void Preflight(IReadOnlyList<Snapshot> snapshots)
    {
        var sources = new List<string>(); var targets = new List<string>();
        foreach (var snapshot in snapshots)
        {
            string source = Path.GetFullPath(snapshot.SourcePath), destination = Path.GetFullPath(snapshot.DestinationPath);
            if (!Path.GetExtension(source).Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(source) != Path.GetFileName(destination)) throw new IOException("A project move must keep the HTML file name.");
            if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("The destination project folder no longer exists.");
            if (snapshot.SourceAssets != (Directory.Exists(AssetFolder(source)) ? AssetFolder(source) : null) ||
                snapshot.DestinationAssets != (snapshot.SourceAssets == null ? null : AssetFolder(destination)))
                throw new IOException("The document's corresponding image folder changed while preparing the move.");
            foreach (string path in new[] { destination, AssetFolder(destination) })
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The project already contains this file or its corresponding image folder: " + path);
                targets.Add(path);
            }
            sources.Add(source);
            if (snapshot.SourceAssets is { } assets) { CheckTree(assets); sources.Add(assets); }
            VerifySource(snapshot);
        }
        if (Overlaps(sources) || Overlaps(targets) || sources.Any(source => targets.Any(target => Overlap(source, target))))
            throw new IOException("The selected documents or their image folders overlap. Choose separate source files and a different project folder.");
    }

    static void Stage(Prepared item)
    {
        foreach (string path in new[] { item.Stage, item.Recovery })
        {
            Directory.CreateDirectory(path); File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        var snapshot = item.Snapshot;
        byte[] original = File.ReadAllBytes(snapshot.SourcePath);
        if (TextFiles.Hash(original) != snapshot.Fingerprint) throw new IOException("The document changed while preparing the move: " + snapshot.SourcePath);
        var encoding = TextFiles.EncodingFor(snapshot.EncodingName);
        byte[] preamble = encoding.GetPreamble();
        string previous = encoding.GetString(original, original.AsSpan().StartsWith(preamble) ? preamble.Length : 0,
            original.Length - (original.AsSpan().StartsWith(preamble) ? preamble.Length : 0));
        string normalized = TextFiles.Normalize(snapshot.Html);
        byte[] output = normalized == TextFiles.Normalize(previous) ? original : [.. preamble, .. encoding.GetBytes(normalized.Replace("\n", snapshot.NewLine))];
        TextFiles.AtomicWrite(item.StagedHtml, output);
        string fingerprint = TextFiles.Hash(output);
        if (HashFile(item.StagedHtml) != fingerprint) throw new IOException("The staged HTML copy could not be verified.");
        DateTime created = File.GetCreationTimeUtc(snapshot.SourcePath), modified = File.GetLastWriteTimeUtc(snapshot.SourcePath);
        File.SetCreationTimeUtc(item.StagedHtml, created); File.SetLastWriteTimeUtc(item.StagedHtml, modified);
        File.SetAttributes(item.StagedHtml, snapshot.Attributes);
        if (snapshot.SourceAssets is { } assets) CopyTree(assets, item.StagedAssets);
        item.Result = new(snapshot.SourcePath, snapshot.DestinationPath, normalized, fingerprint, created, modified,
            snapshot.Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    static void VerifySource(Snapshot snapshot)
    {
        var attributes = File.GetAttributes(snapshot.SourcePath);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked HTML files cannot be moved as project documents.");
        if (attributes != snapshot.Attributes) throw new IOException("The document's file attributes changed while preparing the move: " + snapshot.SourcePath);
        if (HashFile(snapshot.SourcePath) != snapshot.Fingerprint) throw new IOException("The document changed outside Sin - AI Prompt: " + snapshot.SourcePath);
    }
    static string AssetFolder(string html) => Path.Combine(Path.GetDirectoryName(html)!, Path.GetFileNameWithoutExtension(html));
    static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static bool Overlap(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(Path.TrimEndingDirectorySeparator(b) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(Path.TrimEndingDirectorySeparator(a) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static bool Overlaps(List<string> paths) => paths.Where((path, index) => paths.Skip(index + 1).Any(other => Overlap(path, other))).Any();
    static string TemporaryPath(string parent) => Path.Combine(parent, ".sin-project-" + Guid.NewGuid().ToString("N"));
    static void CheckTree(string source)
    {
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked image folders or files cannot be moved with a project: " + source);
        if (Directory.Exists(source)) foreach (string child in Directory.EnumerateFileSystemEntries(source)) CheckTree(child);
    }
    static void CopyTree(string source, string destination)
    {
        CheckTree(source); Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string folder in Directory.EnumerateDirectories(source)) CopyTree(folder, Path.Combine(destination, Path.GetFileName(folder)));
        Directory.SetCreationTimeUtc(destination, Directory.GetCreationTimeUtc(source)); Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
        File.SetAttributes(destination, File.GetAttributes(source));
    }
    static void VerifyTree(string source, string destination)
    {
        CheckTree(source); CheckTree(destination);
        var entries = Directory.GetFileSystemEntries(source).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var copied = Directory.GetFileSystemEntries(destination).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!entries.SequenceEqual(copied, StringComparer.OrdinalIgnoreCase)) throw new IOException("An image folder changed while preparing the project move: " + source);
        foreach (string name in entries!)
        {
            string from = Path.Combine(source, name), to = Path.Combine(destination, name);
            if (Directory.Exists(from)) VerifyTree(from, to);
            else if (!File.Exists(to) || HashFile(from) != HashFile(to)) throw new IOException("An image changed while preparing the project move: " + from);
        }
    }
    static void Rollback(Prepared item, List<string> failures)
    {
        void Restore(Action action, string path) { try { action(); } catch (Exception error) { failures.Add(path + ": " + error.Message); } }
        bool changed = false;
        // Another application can edit a just-published destination. Preserve the
        // whole published pair in that case instead of deleting its newer data.
        try
        {
            if (item.HtmlPublished && HashFile(item.Snapshot.DestinationPath) != item.Result.Fingerprint)
                throw new IOException("The destination was edited after publication; its newer copy has been retained.");
            if (item.AssetsPublished) VerifyTree(item.OriginalAssets, item.Snapshot.DestinationAssets!);
        }
        catch (Exception error) { changed = true; failures.Add(item.Snapshot.DestinationPath + ": " + error.Message); }
        if (!changed && item.HtmlPublished) Restore(() => File.Move(item.Snapshot.DestinationPath, item.StagedHtml), item.Snapshot.DestinationPath);
        if (!changed && item.AssetsPublished) Restore(() => Directory.Move(item.Snapshot.DestinationAssets!, item.StagedAssets), item.Snapshot.DestinationAssets!);
        if (item.HtmlBackedUp) Restore(() => File.Move(item.OriginalHtml, item.Snapshot.SourcePath), item.OriginalHtml);
        if (item.AssetsBackedUp) Restore(() => Directory.Move(item.OriginalAssets, item.Snapshot.SourceAssets!), item.OriginalAssets);
    }
    static void Cleanup(string path, List<string> failures)
    {
        try { if (Directory.Exists(path)) DeleteTree(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failures.Add(path + ": " + error.Message); }
    }
    static void DeleteTree(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path)) { File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); File.Delete(file); }
        foreach (string folder in Directory.EnumerateDirectories(path)) DeleteTree(folder);
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); Directory.Delete(path);
    }
}
