using System.Text.RegularExpressions;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// The settings record owns the shared history; palettes receive value snapshots.
internal static class RecentColors
{
    internal static IReadOnlyList<string> Read(Settings settings) => settings.RecentColors.ToArray();
    internal static void Use(Settings settings, string value)
    {
        if (!Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) return;
        value = value.ToLowerInvariant();
        settings.RecentColors.RemoveAll(color => color.Equals(value, StringComparison.OrdinalIgnoreCase));
        settings.RecentColors.Insert(0, value);
        if (settings.RecentColors.Count > 10) settings.RecentColors.RemoveRange(10, settings.RecentColors.Count - 10);
    }
}
