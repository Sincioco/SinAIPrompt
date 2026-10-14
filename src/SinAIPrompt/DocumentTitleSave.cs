using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal sealed record FirstSaveName(string FileName, string? Path);

// First-save naming uses the document's Title paragraph and its existing draft
// timestamp. Folder choice and collision handling do not mutate the document.
internal static class DocumentTitleSave
{
    internal static async Task<FirstSaveName?> ResolveAsync(Document document, EditorView? view, string workingFolder, string autoSaveFolder)
    {
        if (document.Path != null || view == null) return null;
        string title = await view.ReadDocumentTitleAsync();
        var invalid = Path.GetInvalidFileNameChars();
        title = Regex.Replace(new string(title.Select(character => invalid.Contains(character) ? '-' : character).ToArray()), @"\s+", " ").Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (title.Length > 210) title = title[..210].TrimEnd(' ', '.');
        string prefix = document.Name.Length >= 18 && document.Name.Substring(15, 3) == " - " &&
            DateTime.TryParseExact(document.Name[..15], "yyyy-MM-dd HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? document.Name[..15] : document.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
        string stem = prefix + " - " + title, fileName = stem + ".html";
        string? folder = Directory.Exists(workingFolder) ? workingFolder : Directory.Exists(autoSaveFolder) ? autoSaveFolder : null;
        if (folder == null) return new(fileName, null);
        return await Task.Run(() =>
        {
            for (int number = 1; ; number++)
            {
                string name = number == 1 ? fileName : $"{stem} {number}.html";
                string path = Path.Combine(folder, name);
                if (!File.Exists(path) && !Directory.Exists(path) && !Directory.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(name))))
                    return new FirstSaveName(name, path);
            }
        });
    }
}
