using System.Text;
using System.Text.RegularExpressions;

namespace SinAIPrompt.Core;

public readonly record struct SearchMatch(int Index, int Length);
public static class SearchEngine
{
    public static List<SearchMatch> FindAll(string text, string query, bool matchCase, bool wholeWord, bool regex = false)
    {
        List<SearchMatch> result = []; if (query.Length == 0) return result;
        if (regex) return Expression(query, matchCase, wholeWord).Matches(text).Cast<Match>().Where(m => m.Length > 0).Select(m => new SearchMatch(m.Index, m.Length)).ToList();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int start = 0; start <= text.Length - query.Length;)
        {
            int i = text.IndexOf(query, start, comparison); if (i < 0) break;
            if (!wholeWord || ((i == 0 || !IsWord(text[i - 1])) && (i + query.Length == text.Length || !IsWord(text[i + query.Length])))) result.Add(new(i, query.Length));
            start = i + query.Length;
        }
        return result;
    }
    static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    static Regex Expression(string query, bool matchCase, bool wholeWord) => new(wholeWord ? @"(?<![\p{L}\p{N}_])(?:" + query + @")(?![\p{L}\p{N}_])" : query,
        RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(250));
    public static string ReplaceAll(string text, string query, string replacement, bool matchCase, bool wholeWord, out int count, bool regex = false)
    {
        if (regex) { int replaced = 0; string output = Expression(query, matchCase, wholeWord).Replace(text, m => { if (m.Length == 0) return m.Value; replaced++; return m.Result(replacement); }); count = replaced; return output; }
        var matches = FindAll(text, query, matchCase, wholeWord); count = matches.Count; var b = new StringBuilder(text);
        foreach (var m in matches.AsEnumerable().Reverse()) { b.Remove(m.Index, m.Length); b.Insert(m.Index, replacement); }
        return b.ToString();
    }
}
