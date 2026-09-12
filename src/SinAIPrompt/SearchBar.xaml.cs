using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SinAIPrompt;

public partial class SearchBar : UserControl
{
    internal Func<EditorView?> GetEditor { get; set; } = () => null;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    int revision;
    bool updatingResults;
    EditorView? searchedEditor;

    public SearchBar()
    {
        InitializeComponent();
        debounce.Tick += async (_, _) => { debounce.Stop(); await ExecuteAsync("count"); };
    }
    internal void Show(bool replace)
    {
        Visibility = Visibility.Visible; ReplacePanel.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        FindBox.Focus(); FindBox.SelectAll(); Refresh();
    }
    internal void Refresh()
    {
        if (Visibility != Visibility.Visible) return;
        revision++; debounce.Stop(); debounce.Start();
    }
    internal async Task<SearchResult> ExecuteAsync(string action, int target = 0)
    {
        if (action is "next" or "previous" && Visibility != Visibility.Visible) Show(false);
        debounce.Stop(); int current = ++revision;
        var editor = GetEditor(); if (editor == null) return new();
        var query = new SearchRequest(FindBox.Text, MatchCase.IsChecked == true, WholeWord.IsChecked == true,
            Regex.IsChecked == true, Wrap.IsChecked == true, action, ReplaceBox.Text, target);
        try
        {
            if (searchedEditor != null && searchedEditor != editor) await searchedEditor.SearchAsync(new("", Action: "clear"));
            searchedEditor = editor;
            var result = await editor.SearchAsync(query);
            if (current == revision && GetEditor() == editor)
            {
                Status.Text = result.Message ?? (query.Query.Length == 0 ? "" : result.Count == 0 ? "No results" : result.Index > 0 ? $"{result.Index} of {result.Count} results" : $"{result.Count} results");
                updatingResults = true;
                try { if (result.Results != null || result.Count == 0) Results.ItemsSource = result.Results; Results.SelectedIndex = result.Index - 1; }
                finally { updatingResults = false; }
                if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
            }
            return result;
        }
        catch (Exception ex) { if (current == revision) Status.Text = ex.Message; return new(Message: ex.Message); }
    }
    internal async void Close()
    {
        Visibility = Visibility.Collapsed; debounce.Stop(); revision++; Results.ItemsSource = null;
        var editor = searchedEditor; searchedEditor = null;
        if (editor != null) await editor.SearchAsync(new("", Action: "clear"));
        GetEditor()?.FocusEditing();
    }
    async void ResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingResults && Results.SelectedItem is SearchHit hit) await ExecuteAsync("select", hit.Index);
    }
    async void NextClick(object sender, RoutedEventArgs e) => await ExecuteAsync("next");
    async void PreviousClick(object sender, RoutedEventArgs e) => await ExecuteAsync("previous");
    async void ReplaceClick(object sender, RoutedEventArgs e) => await ExecuteAsync("replace");
    async void ReplaceAllClick(object sender, RoutedEventArgs e) => await ExecuteAsync("replaceAll");
    void CloseClick(object sender, RoutedEventArgs e) => Close();
    void OptionsClick(object sender, RoutedEventArgs e) => Options.Visibility = Options.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    void ToggleReplace(object sender, RoutedEventArgs e) => ReplacePanel.Visibility = ReplacePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    void SearchChanged(object sender, TextChangedEventArgs e) => Refresh();
    void OptionChanged(object sender, RoutedEventArgs e) => Refresh();
    async void FindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await ExecuteAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "previous" : "next"); }
        else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
}
