using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class SearchSelfTest
{
    internal static async Task Source(MainWindow window, Action<bool, string> check)
    {
        var view = window.CurrentView!;
        string original = view.Document.Text;
        await view.SetSourceAsync(true);
        view.Editor.Text = "One one stone\r\nOne";
        var bar = (SearchBar)window.FindName("SearchPanel");
        bar.FindBox.Text = "one";
        view.Editor.Select(0, 0);
        check(await window.FindNext() && view.Editor.SelectionStart == 0 && view.Editor.SelectionLength == 3,
            "Source search selects the first case-insensitive match");
        check(await window.FindNext() && view.Editor.SelectionStart == 4, "Source search advances to the next match");
        check((await view.SearchAsync(new("one", WholeWord: true, Action: "replaceAll", Replacement: "two"))).Count == 3 && view.Editor.Text.Contains("stone"),
            "Source Replace All preserves whole-word boundaries");
        view.Editor.Text = original;
        await view.SetSourceAsync(false);
        await Task.Delay(150);
        await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentDocument.body.dispatchEvent(new KeyboardEvent('keydown',{key:'f',ctrlKey:true,bubbles:true}))");
        for (int i = 0; i < 50 && bar.Visibility != System.Windows.Visibility.Visible; i++) await Task.Delay(20);
        check(view.IsVisual && bar.Visibility == System.Windows.Visibility.Visible && bar.ActualWidth == 280, "Ctrl+F opens the left Find pane without switching to HTML source");
        check((await view.SearchAsync(new("[", Regex: true))).Message?.Contains("Invalid") == true, "Visual regex errors reach the native search bar without hanging");
        await view.Browser.ExecuteScriptAsync("window.editor.load('<p>Line one</p><p style=\"height:1200px\">Between results</p><p>Line two</p>')");
        await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"window.editor.ready()\",\"awaitPromise\":true}");
        bar.FindBox.Text = "Line";
        var found = await bar.ExecuteAsync("count");
        check(found.Count == 2 && bar.Results.Items.Count == 2 && bar.Status.Text == "2 results", "Find pane displays the result count and matching excerpts");
        bar.Results.SelectedIndex = 1;
        for (int i = 0; i < 50 && !bar.Status.Text.StartsWith("2 of"); i++) await Task.Delay(20);
        await Task.Delay(350);
        check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentWindow.scrollY > 100") == "true", "Clicking a Find result smoothly scrolls the visual document to that match");
        check(await view.Browser.ExecuteScriptAsync("document.querySelector('#document').contentWindow.CSS.highlights.get('sin-search')?.size === 2") == "true", "Find highlights every occurrence in the visual document");
        window.UpdateLayout();
        var origin = bar.PointToScreen(new System.Windows.Point()); var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(bar);
        var bitmap = ScreenCapture.Capture(new System.Windows.Int32Rect((int)origin.X, (int)origin.Y, (int)(bar.ActualWidth * dpi.DpiScaleX), (int)(bar.ActualHeight * dpi.DpiScaleY)));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(App.Current.Store.DirectoryPath, "search-bar.png"), Convert.FromBase64String(ScreenCapture.Png(bitmap).Split(',')[1]));
        bar.Close();
        for (int i = 0; i < 50; i++)
        {
            if (await view.Browser.ExecuteScriptAsync("!document.querySelector('#document').contentWindow.CSS.highlights.has('sin-search')") == "true") break;
            await Task.Delay(20);
        }
        check(await view.Browser.ExecuteScriptAsync("!document.querySelector('#document').contentWindow.CSS.highlights.has('sin-search')") == "true", "Closing Find removes its yellow document highlighting");
        await view.Browser.ExecuteScriptAsync($"window.editor.load({System.Text.Json.JsonSerializer.Serialize(original)})");
        await view.Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", "{\"expression\":\"window.editor.ready()\",\"awaitPromise\":true}");
    }
}
