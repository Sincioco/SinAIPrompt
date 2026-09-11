using System.IO;
using System.Text.Json;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class StorageLocation
{
    internal static string? TestPointerPath { get; set; }
    static string PointerPath => TestPointerPath ?? Path.Combine(AppContext.BaseDirectory, "data-location.json");
    public static string Read()
    {
        if (File.Exists(PointerPath))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(PointerPath));
            string? folder = json.RootElement.GetProperty("folder").GetString();
            if (!string.IsNullOrWhiteSpace(folder)) return Path.GetFullPath(folder, AppContext.BaseDirectory);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sin - AI Prompt");
    }
    public static void Write(string folder) => TextFiles.AtomicWrite(PointerPath, JsonSerializer.SerializeToUtf8Bytes(new { folder }), true);
}
