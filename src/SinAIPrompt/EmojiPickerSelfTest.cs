using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class EmojiPickerSelfTest
{
    internal static void Run(MainWindow owner, Action<bool, string> check)
    {
        var document = owner.ActiveDocument!;
        var settings = App.Current.Preferences;
        string originalEmoji = document.Emoji, originalText = document.Text, originalName = document.Name;
        var originalHistory = settings.RecentEmojis.ToArray();
        Window? dialog = null;
        T Named<T>(string name) where T : FrameworkElement => ScreenCaptureSelfTest.Controls(dialog!).OfType<T>().Single(control => control.Name == name);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Open() { dialog = EmojiPicker.Create(owner, document, settings, App.Current.MarkChanged); dialog.Show(); dialog.UpdateLayout(); }
        try
        {
            settings.RecentEmojis.Clear(); Open();
            var choices = Named<UniformGrid>("EmojiChoices");
            var firstPage = choices.Children.Cast<Button>().Select(button => (string)button.Tag).ToArray();
            check(firstPage.Length == 50 && !Named<Button>("PreviousPage").IsEnabled && Named<Button>("NextPage").IsEnabled,
                "Emoji picker shows 50 choices per page with correct initial pagination");
            Click(Named<Button>("NextPage"));
            check(choices.Children.Count == 50 && !choices.Children.Cast<Button>().Any(button => firstPage.Contains((string)button.Tag)) &&
                !Named<Button>("NextPage").IsEnabled && Named<Button>("PreviousPage").IsEnabled,
                "Emoji pagination reaches a distinct second page and stops at the last page");
            Named<TextBox>("EmojiSearch").Text = "rObOt";
            check(choices.Children.Count == 1 && (string)((Button)choices.Children[0]).Tag == "🤖" && Named<TextBlock>("EmojiPage").Text.StartsWith("Page 1"),
                "Emoji search matches names without case sensitivity and resets pagination");
            Named<TextBox>("EmojiSearch").Text = "no matching emoji name";
            check(choices.Children.Count == 0 && Named<TextBlock>("EmojiPage").Text == "No matching emojis",
                "Emoji search reports an empty result without stale choices");
            Named<TextBox>("EmojiSearch").Text = "🤖";
            Click((Button)choices.Children[0]);
            check(document.Emoji == "🤖" && document.Text == originalText && document.Name == originalName && settings.RecentEmojis.SequenceEqual(["🤖"]),
                "Choosing an emoji updates only document metadata and adds the newest recent choice");

            settings.RecentEmojis.Clear(); settings.RecentEmojis.AddRange(firstPage.Take(20));
            Open(); Named<TextBox>("EmojiSearch").Text = "Rocket";
            Click((Button)Named<UniformGrid>("EmojiChoices").Children[0]);
            Open(); Click((Button)Named<UniformGrid>("RecentEmojis").Children[4]);
            check(settings.RecentEmojis.Count == 20 && settings.RecentEmojis.Distinct().Count() == 20 && settings.RecentEmojis[0] == firstPage[3] &&
                settings.RecentEmojis[1] == "🚀" && !settings.RecentEmojis.Contains(firstPage[19]),
                "Recent emojis retain the last 20 unique choices, move reused entries first and evict the oldest");
            var savedSettings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
            var savedSession = JsonSerializer.Deserialize<Session>(JsonSerializer.Serialize(new Session { Windows = [owner.Snapshot()] }))!;
            check(savedSettings.RecentEmojis.SequenceEqual(settings.RecentEmojis) && savedSession.Windows[0].Documents.Single(d => d.Id == document.Id).Emoji == document.Emoji,
                "Emoji assignments and the shared recent history survive session/settings serialization");

            // Both navigation surfaces use this template; inspect its live binding updates.
            var template = (DataTemplate)owner.FindResource("DocumentTemplate");
            var row = (FrameworkElement)template.LoadContent(); row.DataContext = document;
            var emoji = ScreenCaptureSelfTest.Controls(row).OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0 && text.FontSize == 15);
            row.Measure(new Size(300, 36)); row.Arrange(new Rect(0, 0, 300, 36)); row.UpdateLayout();
            check(emoji.Text == document.Emoji && emoji.Visibility == Visibility.Visible && document.AccessibleName.StartsWith(document.Emoji),
                "Shared tab/Document List template places the emoji before the filename and exposes it to accessibility");
            Open(); Click(ScreenCaptureSelfTest.Controls(dialog!).OfType<Button>().Single(button => Equals(button.Content, "Remove Emoji")));
            row.UpdateLayout();
            check(document.Emoji == "" && emoji.Visibility == Visibility.Collapsed && settings.RecentEmojis.Count == 20,
                "Remove Emoji clears the label without leaving a blank icon slot or clearing history");

            Exception? failure = null;
            owner.Dispatcher.BeginInvoke(() =>
            {
                dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Title == "Choose Document Emoji");
                try { check(Named<UniformGrid>("RecentEmojis").Children.Count == 20, "Document context menu opens the picker with the 20 shared recent emojis"); }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            var menu = owner.CreateDocumentMenu(document).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Choose _Emoji…"));
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (failure != null) throw failure;
            check(document.Emoji == "", "Cancelling the document emoji picker leaves the assignment unchanged");
        }
        finally
        {
            dialog?.Close(); document.Emoji = originalEmoji; document.Notify();
            settings.RecentEmojis.Clear(); settings.RecentEmojis.AddRange(originalHistory);
        }
    }
}
