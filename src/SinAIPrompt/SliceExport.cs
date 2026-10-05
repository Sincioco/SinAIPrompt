using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SinAIPrompt;

// One editor owns one short-lived export batch. Browser messages never choose a
// file path: only the folder picker and this owner construct output destinations.
internal sealed class SliceExport
{
    sealed record Batch(string Token, string Folder, string Name, int Count)
    {
        internal int Saved;
    }
    Batch? batch;
    bool busy;
    internal sealed record Result(string folder, int saved, int total);

    internal static string? ChooseFolder(Window owner)
    {
        var picker = new OpenFolderDialog { Title = "Save All Slices — Choose a destination folder" };
        return picker.ShowDialog(owner) == true ? picker.FolderName : null;
    }

    internal async Task<string> BeginAsync(string parent, string imageName, int count)
    {
        if (busy || batch != null) throw new IOException("An image-cell export is already running.");
        if (count is < 1 or > 1089) throw new ArgumentOutOfRangeException(nameof(count)); // Up to 33 columns × 33 rows.
        busy = true;
        try
        {
            batch = await Task.Run(() =>
            {
                string name = SafeName(imageName), stem = name + " - Slices";
                string folder = Path.Combine(parent, stem);
                for (int number = 2; Directory.Exists(folder) || File.Exists(folder); number++)
                    folder = Path.Combine(parent, $"{stem} ({number})");
                Directory.CreateDirectory(folder);
                return new Batch(Guid.NewGuid().ToString("N"), folder, name, count);
            });
            return batch.Token;
        }
        finally { busy = false; }
    }

    internal async Task WriteAsync(string token, int index, string data)
    {
        var current = Require(token);
        if (busy || index != current.Saved || index >= current.Count)
            throw new IOException("The image cells must be saved once, in order.");
        busy = true;
        try
        {
            await Task.Run(() =>
            {
                const string prefix = "data:image/png;base64,";
                if (!data.StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("The image cell is not a PNG.");
                byte[] bytes = Convert.FromBase64String(data[prefix.Length..]);
                if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new IOException("The image cell is not a PNG.");
                string number = (index + 1).ToString("D" + Math.Max(2, current.Count.ToString().Length));
                string path = Path.Combine(current.Folder, $"{current.Name} - Cell {number}.png");
                string temporary = Path.Combine(current.Folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
                    File.Move(temporary, path); // Never replace a file, including one created during export.
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
            current.Saved++;
        }
        finally { busy = false; }
    }

    internal Result End(string token)
    {
        var current = Require(token);
        if (busy) throw new IOException("An image cell is still being saved.");
        batch = null;
        return new(current.Folder, current.Saved, current.Count);
    }
    Batch Require(string token) => batch is { } current && current.Token == token ? current : throw new IOException("The image-cell export is no longer active.");
    static string SafeName(string name)
    {
        string value = new(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        value = value[..Math.Min(value.Length, 80)].TrimEnd('.', ' ');
        try { Core.TextFiles.ValidateFileName(value); return value; }
        catch (ArgumentException) { return "Image"; }
    }
}
