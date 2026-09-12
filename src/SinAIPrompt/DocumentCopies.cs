using System.IO;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Copies are independent documents. Saved copies own new image files; drafts
// embed their copied images until the user chooses a save location.
internal static class DocumentCopies
{
    internal static async Task<Document> CreateAsync(Document source, EditorView view, IEnumerable<string> openNames)
    {
        await view.FlushAsync();
        var names = openNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string extension = Path.GetExtension(source.Name), stem = Path.GetFileNameWithoutExtension(source.Name);
        string? parent = source.Path == null ? null : Path.GetDirectoryName(source.Path);
        string name; string? destination;
        for (int number = 2; ; number++)
        {
            name = $"{stem} {number}{extension}";
            destination = parent == null ? null : Path.Combine(parent, name);
            if (!names.Contains(name) && (destination == null || !File.Exists(destination) &&
                !Directory.Exists(destination) && !Directory.Exists(Path.Combine(parent!, Path.GetFileNameWithoutExtension(name))))) break;
        }
        string html = destination == null ? await view.ExportAsync(duplicate: true) : (await view.PrepareSaveAsAsync(destination))!;
        var copy = new Document { DraftName = name, Text = html, EncodingName = source.EncodingName,
            NewLine = source.NewLine, Zoom = source.Zoom, AutoSave = source.AutoSave && destination != null,
            CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        if (destination != null)
        {
            if (File.Exists(destination)) throw new IOException("The duplicate name was just used by another file. Try again.");
            await Task.Run(() => TextFiles.Save(copy, destination));
        }
        return copy;
    }
}
