using System.Text.Json;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal sealed record SearchRequest(string Query, bool MatchCase = false, bool WholeWord = false, bool Regex = false,
    bool Wrap = true, string Action = "count", string? Replacement = null, int Target = 0);
internal sealed record SearchHit(int Index, string Before, string Match, string After)
{
    internal static SearchHit FromText(string text, int start, int length, int index)
    {
        int end = start + length, nextLine = text.IndexOf('\n', end), previousLine = start == 0 ? -1 : text.LastIndexOf('\n', start - 1);
        return new(index, text[Math.Max(start - 45, previousLine + 1)..start].Replace('\r', ' '),
            text.Substring(start, Math.Min(length, 100)).Replace('\r', ' ').Replace('\n', ' '),
            text[end..Math.Min(end + 70, nextLine < 0 ? text.Length : nextLine)].Replace('\r', ' '));
    }
}
internal sealed record SearchResult(int Count = 0, int Index = 0, string? Message = null, IReadOnlyList<SearchHit>? Results = null);

public sealed partial class EditorView
{
    internal async Task<SearchResult> SearchAsync(SearchRequest request)
    {
        if (disposed) return new();
        if (IsVisual)
        {
            await initialized.Task;
            string args = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string raw = await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                Json(new { expression = $"window.editor.search({args})", awaitPromise = true, returnByValue = true }));
            using var response = JsonDocument.Parse(raw);
            if (response.RootElement.TryGetProperty("exceptionDetails", out _)) return new(Message: "Search could not finish.");
            return response.RootElement.GetProperty("result").GetProperty("value").Deserialize<SearchResult>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        if (request.Action == "clear" || request.Query.Length == 0) return new();
        string snapshot = Editor.Text;
        try
        {
            var matches = await Task.Run(() => SearchEngine.FindAll(snapshot, request.Query, request.MatchCase, request.WholeWord, request.Regex));
            if (Editor.Text != snapshot || IsVisual) return new();
            if (request.Action == "replaceAll")
            {
                string replacement = await Task.Run(() => SearchEngine.ReplaceAll(snapshot, request.Query, request.Replacement ?? "", request.MatchCase, request.WholeWord, out _, request.Regex));
                if (Editor.Text != snapshot) return new(Message: "Document changed. Search again.");
                Editor.BeginChange(); Editor.SelectAll(); Editor.SelectedText = replacement; Editor.EndChange();
                return new(matches.Count, Message: $"Replaced {matches.Count} occurrences");
            }
            var results = matches.Select((m, i) => SearchHit.FromText(snapshot, m.Index, m.Length, i + 1)).ToArray();
            if (request.Action == "count") return new(matches.Count, Results: results);
            if (request.Action == "replace" && matches.Any(m => m.Index == Editor.SelectionStart && m.Length == Editor.SelectionLength))
            {
                Editor.SelectedText = SearchEngine.ReplaceAll(Editor.SelectedText, request.Query, request.Replacement ?? "", request.MatchCase, request.WholeWord, out _, request.Regex);
                return await SearchAsync(request with { Action = "next" });
            }
            bool previous = request.Action == "previous";
            int origin = previous ? Editor.SelectionStart : Editor.SelectionStart + Editor.SelectionLength;
            int index = previous ? matches.FindLastIndex(m => m.Index < origin) : matches.FindIndex(m => m.Index >= origin);
            if (request.Action == "select") index = request.Target > 0 && request.Target <= matches.Count ? request.Target - 1 : -1;
            if (index < 0 && request.Wrap) index = previous ? matches.Count - 1 : matches.Count == 0 ? -1 : 0;
            if (index < 0) return new(matches.Count, Message: matches.Count == 0 ? "No results" : "Reached the end of the search");
            var match = matches[index]; Editor.Select(match.Index, match.Length); Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(match.Index));
            return new(matches.Count, index + 1, Results: results);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return new(Message: ex is ArgumentException ? "Invalid regular expression." : "Search took too long. Try a simpler expression."); }
    }
}
