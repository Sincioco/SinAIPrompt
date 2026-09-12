using System.IO;
using System.Text;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Converts one source without changing it, then atomically publishes a new sibling HTML file.
internal static class MarkdownImport
{
    internal static bool IsMarkdown(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase);
    internal static async Task<string> ConvertAsync(string path, Func<string, Task<string>> convert)
    {
        path = Path.GetFullPath(path);
        string markdown = await Task.Run(() => TextFiles.Open(path).Text);
        string html = await convert(markdown);
        return await Task.Run(() =>
        {
            string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + " - Converted");
            string temporary = stem + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                TextFiles.AtomicWrite(temporary, new UTF8Encoding(false).GetBytes(html));
                for (int number = 1; ; number++)
                {
                    string destination = stem + (number == 1 ? "" : $" ({number})") + ".html";
                    try { File.Move(temporary, destination); return destination; }
                    catch (IOException) when (File.Exists(destination)) { }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }
}
