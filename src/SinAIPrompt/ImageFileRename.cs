using System.IO;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns the disk transaction only. The caller coordinates open documents and autosaves.
internal static class ImageFileRename
{
    internal static string Destination(string path, string name)
    {
        string destination = TextFiles.RenamePath(path, name);
        if (!Path.GetExtension(path).Equals(Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Keep the image's current file extension when renaming it.");
        if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase) && (File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException("That name already exists. Choose a different name.");
        return destination;
    }
    internal static void Commit(string path, string destination, Document? parent)
    {
        if (parent != null && TextFiles.ChangedOnDisk(parent)) throw new IOException("The parent HTML changed while preparing the rename. Reload it and try again.");
        Move(path, destination);
        try { if (parent?.Dirty == true) TextFiles.Save(parent, parent.Path!); }
        catch { Move(destination, path); throw; }
    }
    static void Move(string path, string destination)
    {
        if (path == destination) return;
        if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)) { File.Move(path, destination); return; }
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".sin-image-rename-" + Guid.NewGuid().ToString("N"));
        File.Move(path, temporary);
        try { File.Move(temporary, destination); }
        catch { File.Move(temporary, path); throw; }
    }
}
