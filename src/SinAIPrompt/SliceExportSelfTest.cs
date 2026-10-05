using System.IO;

namespace SinAIPrompt;

internal static class SliceExportSelfTest
{
    internal static async Task Run(string storage, Action<bool, string> check)
    {
        string parent = Path.Combine(storage, "Slice export native");
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZAAAAABJRU5ErkJggg==";
        var export = new SliceExport();
        async Task<bool> Rejected(Func<Task> action)
        {
            try { await action(); return false; }
            catch (IOException) { return true; }
        }
        string token = await export.BeginAsync(parent, "Four: colors", 2);
        check(await Rejected(() => export.BeginAsync(parent, "Other", 1)), "A slice export owner rejects overlapping export batches");
        check(await Rejected(() => export.WriteAsync(token, 1, png)) && await Rejected(() => export.WriteAsync("expired", 0, png)),
            "Slice export rejects out-of-order writes and stale batch tokens");
        check(await Rejected(() => export.WriteAsync(token, 0, "data:image/png;base64,AA==")), "Slice export rejects invalid PNG data before creating an output file");
        await export.WriteAsync(token, 0, png); await export.WriteAsync(token, 1, png);
        var first = export.End(token);
        string[] files = Directory.GetFiles(first.folder, "*.png").Order().ToArray();
        check(first.saved == 2 && first.total == 2 && files.Select(Path.GetFileName).SequenceEqual(new[] { "Four_ colors - Cell 01.png", "Four_ colors - Cell 02.png" }) &&
            files.All(path => File.ReadAllBytes(path).SequenceEqual(Convert.FromBase64String(png[(png.IndexOf(',') + 1)..]))),
            "Slice export saves exact PNG bytes with sanitized image names and numbered cells");
        token = await export.BeginAsync(parent, "Four: colors", 2);
        await export.WriteAsync(token, 0, png);
        var stopped = export.End(token);
        check(stopped.folder != first.folder && stopped.saved == 1 && stopped.total == 2 && Directory.GetFiles(first.folder).Length == 2,
            "Repeated and stopped slice exports use separate folders and preserve previously saved cells");
        token = await export.BeginAsync(parent, "CON", 1);
        string reservedFolder = Directory.GetDirectories(parent, "Image - Slices*").Single();
        string collision = Path.Combine(reservedFolder, "Image - Cell 01.png");
        await File.WriteAllTextAsync(collision, "Existing file");
        check(await Rejected(() => export.WriteAsync(token, 0, png)) && await File.ReadAllTextAsync(collision) == "Existing file" && Directory.GetFiles(reservedFolder, "*.tmp").Length == 0,
            "Slice export never overwrites a destination created during export and removes its unfinished temporary file");
        check(export.End(token).saved == 0, "A failed slice is not reported as saved and its batch can be closed");
    }

    internal static Task<object[]> ReadFilesAsync(string folder) => Task.Run(() => !Directory.Exists(folder) ? Array.Empty<object>() :
        Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories).Order().Select(path => (object)new
        {
            folder = Path.GetFileName(Path.GetDirectoryName(path)), name = Path.GetFileName(path),
            data = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path))
        }).ToArray());
}
