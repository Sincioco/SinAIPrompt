using System.IO;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Embedded originals are immutable. Only user-owned working copies enter a session.
internal static class BundledDocuments
{
    internal const string Instructions = "2026-09-16 1025 - AI Instructions.html";
    internal const string Formatting = "AI Prompt - Output Formatting.html";

    internal static string WorkingDirectory(Settings settings, string storageDirectory) =>
        !string.IsNullOrWhiteSpace(settings.ExplorerDirectory) ? settings.ExplorerDirectory :
        !string.IsNullOrWhiteSpace(settings.AutoSaveDirectory) ? settings.AutoSaveDirectory :
        Path.Combine(storageDirectory, "Reference Documents");

    internal static Task<IReadOnlyList<string>> EnsureAsync(Session session, string directory) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var errors = new List<string>();
        foreach (string name in new[] { Instructions, Formatting })
        {
            try
            {
                var existing = session.Windows.SelectMany(w => w.Documents)
                    .Where(d => d.Path != null && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (existing.Length > 0)
                {
                    foreach (var document in existing)
                    {
                        EnsureFile(name, document.Path!);
                        document.Pinned = true;
                        // Keep recovered text and its fingerprint, including unsaved changes.
                    }
                    continue;
                }
                string path = Path.Combine(directory, name);
                EnsureFile(name, path);
                var copy = TextFiles.Open(path);
                copy.Pinned = true;
                if (session.Windows.Count == 0) session.Windows.Add(new WindowSession { DocumentList = true });
                session.Windows[0].Documents.Add(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { errors.Add(name + ": " + ex.Message); }
        }
        return errors;
    });

    internal static Task<Document> CreateCopyAsync(string name, string directory, IEnumerable<string> openNames)
    {
        var reserved = openNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.Run(() =>
        {
            byte[] bytes = ReadOriginal(name);
            Directory.CreateDirectory(directory);
            for (int number = 1; ; number++)
            {
                string copyName = number == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)} {number}.html";
                string path = Path.Combine(directory, copyName);
                if (reserved.Contains(copyName) || File.Exists(path) || Directory.Exists(path)) continue;
                try { Publish(path, bytes); }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { continue; }
                var copy = TextFiles.Open(path);
                copy.Pinned = true;
                return copy;
            }
        });
    }

    static void EnsureFile(string name, string path)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try { Publish(path, ReadOriginal(name)); }
        catch (IOException) when (File.Exists(path)) { } // Another writer won; preserve its version.
    }

    static byte[] ReadOriginal(string name)
    {
        if (name is not (Instructions or Formatting)) throw new ArgumentException("Unknown bundled document.", nameof(name));
        using var source = typeof(BundledDocuments).Assembly.GetManifestResourceStream("SinAIPrompt.BundledDocuments." + name)
            ?? throw new IOException("The bundled document is missing from this build.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    static void Publish(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            TextFiles.AtomicWrite(temporary, bytes);
            File.Move(temporary, path); // Never replace a user's version, even after a naming race.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
