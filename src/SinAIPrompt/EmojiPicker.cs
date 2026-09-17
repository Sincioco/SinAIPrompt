using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Each dialog owns its search/page state. The document and settings own saved values.
internal static class EmojiPicker
{
    const int PageSize = 50, RecentLimit = 20;
    static readonly (string Glyph, string Name)[] Catalog = """
        😀|Grinning happy smile
        😃|Smiling excited
        😄|Laughing joy
        😁|Beaming grin
        😆|Laughing squint
        😅|Relieved sweat smile
        😂|Tears of joy laugh
        🙂|Slight smile
        😉|Wink
        😊|Blushing smile
        😎|Cool sunglasses
        🤔|Thinking question
        🤩|Star eyes excited
        🥳|Party celebration
        😍|Heart eyes love
        😴|Sleeping rest
        😇|Angel halo
        🤖|Robot AI artificial intelligence
        👻|Ghost spooky
        👽|Alien space
        👍|Thumbs up approved
        👎|Thumbs down rejected
        👏|Clapping applause
        🙌|Raised hands celebration
        🙏|Thank you prayer
        💪|Strong muscle
        👀|Eyes review watch
        🧠|Brain ideas thinking
        ❤️|Red heart love
        💛|Yellow heart
        💚|Green heart
        💙|Blue heart
        💜|Purple heart
        🖤|Black heart
        ⭐|Star favorite
        🌟|Glowing star
        ✨|Sparkles magic
        🔥|Fire hot urgent
        💡|Light bulb idea
        🎯|Target goal objective
        ✅|Check completed done
        ❌|Cross failed no
        ⚠️|Warning issue
        ❓|Question help
        ❗|Exclamation important
        🔄|Refresh in progress
        🚧|Construction blocked
        🧪|Test experiment
        🔧|Wrench fix tools
        🏗️|Building architecture
        📌|Pin reminder
        📋|Clipboard tasks plan
        📝|Memo writing notes
        📄|Document page
        📑|Bookmarks reference
        📚|Books library learning
        📖|Open book read
        🔖|Bookmark saved
        📁|Folder files
        🗂️|Index organization
        🗃️|Archive storage
        🗑️|Trash discard
        🔒|Locked private secure
        🔑|Key access
        💻|Computer code software
        🖥️|Desktop monitor
        🖱️|Mouse input
        ⌨️|Keyboard typing
        📱|Phone mobile
        ⚙️|Gear settings
        🛠️|Tools maintenance
        🐛|Bug debugging
        📦|Package commit delivery
        🚀|Rocket launch release
        🌐|Globe web internet
        🔗|Link connection
        📊|Chart analytics data
        📈|Chart growth progress
        💾|Save disk
        📅|Calendar date schedule
        ⏰|Alarm time reminder
        ⏳|Hourglass waiting
        🏠|Home house
        🏢|Office work business
        💼|Briefcase project work
        💰|Money budget finance
        🛒|Shopping cart
        🎨|Art palette design
        🖼️|Picture image photo
        📷|Camera photography
        🎬|Movie clapper video
        🎵|Music note audio
        🎮|Game controller play
        🏆|Trophy achievement
        🎁|Gift present
        🌱|Seedling new growth
        🌳|Tree nature
        ☀️|Sun bright day
        🌙|Moon night
        🌈|Rainbow colors
        """.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split('|')).Select(parts => (parts[0], parts[1])).ToArray();

    internal static Window Create(Window owner, Document document, Settings settings, Action changed)
    {
        var dialog = Dialogs.Create(owner, "Choose Document Emoji", 530);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Search emojis", Margin = new Thickness(0, 0, 0, 6) });
        var search = new TextBox { Name = "EmojiSearch", Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(search, "Search emojis");
        panel.Children.Add(search);
        var recentLabel = new TextBlock { Text = "Recently used", Margin = new Thickness(0, 0, 0, 4) };
        var recent = new UniformGrid { Name = "RecentEmojis", Columns = 10, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(recentLabel); panel.Children.Add(recent);
        var choices = new UniformGrid { Name = "EmojiChoices", Columns = 10, Rows = 5, Height = 220 };
        panel.Children.Add(choices);
        var navigation = new DockPanel { Margin = new Thickness(0, 12, 0, 16) };
        int page = 0;
        var previous = new Button { Content = "Previous", Name = "PreviousPage", MinWidth = 90, Padding = new Thickness(12, 6, 12, 6) };
        var next = new Button { Content = "Next", Name = "NextPage", MinWidth = 90, Padding = new Thickness(12, 6, 12, 6) };
        var pageLabel = new TextBlock { Name = "EmojiPage", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        DockPanel.SetDock(previous, Dock.Left); DockPanel.SetDock(next, Dock.Right);
        navigation.Children.Add(previous); navigation.Children.Add(next); navigation.Children.Add(pageLabel);
        panel.Children.Add(navigation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Dialogs.Button("Remove Emoji", () => Select("")));
        actions.Children.Add(Dialogs.Button("Cancel", () => dialog.Close(), cancel: true));
        panel.Children.Add(actions);

        void Select(string value)
        {
            document.Emoji = value; document.Notify();
            if (value.Length > 0)
            {
                settings.RecentEmojis.RemoveAll(item => item == value);
                settings.RecentEmojis.Insert(0, value);
                if (settings.RecentEmojis.Count > RecentLimit)
                    settings.RecentEmojis.RemoveRange(RecentLimit, settings.RecentEmojis.Count - RecentLimit);
            }
            changed(); dialog.Close();
        }
        Button Choice((string Glyph, string Name) entry)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = entry.Glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 23 },
                Tag = entry.Glyph, ToolTip = entry.Name, Margin = new Thickness(2), Padding = new Thickness(2),
                Height = 40, Style = (Style)owner.FindResource("FlatButton")
            };
            AutomationProperties.SetName(button, entry.Name);
            button.Click += (_, _) => Select(entry.Glyph);
            return button;
        }
        void Refresh()
        {
            string query = search.Text.Trim();
            var matches = Catalog.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Glyph.Contains(query)).ToArray();
            int pages = Math.Max(1, (matches.Length + PageSize - 1) / PageSize);
            page = Math.Clamp(page, 0, pages - 1);
            choices.Children.Clear();
            foreach (var entry in matches.Skip(page * PageSize).Take(PageSize)) choices.Children.Add(Choice(entry));
            previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < pages;
            pageLabel.Text = matches.Length == 0 ? "No matching emojis" : $"Page {page + 1} of {pages} · {matches.Length} emojis";
        }
        foreach (string value in settings.RecentEmojis.Distinct().Take(RecentLimit))
        {
            var entry = Catalog.FirstOrDefault(item => item.Glyph == value);
            if (entry.Glyph != null) recent.Children.Add(Choice(entry));
        }
        if (recent.Children.Count == 0) recentLabel.Visibility = recent.Visibility = Visibility.Collapsed;
        search.TextChanged += (_, _) => { page = 0; Refresh(); };
        previous.Click += (_, _) => { page--; Refresh(); };
        next.Click += (_, _) => { page++; Refresh(); };
        dialog.Content = panel; dialog.Loaded += (_, _) => search.Focus();
        Refresh();
        return dialog;
    }
}
