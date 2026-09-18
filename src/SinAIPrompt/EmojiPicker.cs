using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class EmojiPicker
{
    internal static Window Create(Window owner, Document document, Settings settings, Action changed, bool openSystemPanel = true)
    {
        var dialog = Dialogs.Create(owner, "Choose Document Emoji", 530);
        var colors = (ColorEmoji)owner.FindResource("ColorEmoji");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Choose an emoji in the Windows panel, then click Apply.", TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Name = "EmojiInput", Text = document.Emoji, MaxLength = 64, FontSize = 22, Margin = new Thickness(0, 12, 0, 8) };
        AutomationProperties.SetName(input, "Selected emoji"); panel.Children.Add(input);
        var preview = new Image { Name = "EmojiPreview", Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(preview);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
        bool opened = false;
        void ShowWindowsPanel()
        {
            input.Focus(); input.SelectAll();
            opened = WindowsEmojiPanel.Show();
            notice.Text = opened ? "" : "Press Windows + . to open the Windows emoji panel, or paste an emoji here.";
        }
        panel.Children.Add(Dialogs.Button("Open Windows Emoji Panel", ShowWindowsPanel)); panel.Children.Add(notice);
        var recentLabel = new TextBlock { Text = "Recently used", Margin = new Thickness(0, 0, 0, 4) };
        var recent = new UniformGrid { Name = "RecentEmojis", Columns = 10, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(recentLabel); panel.Children.Add(recent);
        foreach (string value in settings.RecentEmojis.Distinct().Take(20))
        {
            var button = new Button
            {
                Content = new Image { Source = (System.Windows.Media.ImageSource?)colors.Convert(value, typeof(Image), null!, CultureInfo.CurrentCulture), Width = 30, Height = 30 },
                Tag = value, ToolTip = value, Margin = new Thickness(2), Padding = new Thickness(2), Height = 40,
                Style = (Style)owner.FindResource("FlatButton")
            };
            AutomationProperties.SetName(button, value);
            button.Click += (_, _) => Select(value); recent.Children.Add(button);
        }
        if (recent.Children.Count == 0) recentLabel.Visibility = recent.Visibility = Visibility.Collapsed;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = Dialogs.Button("Apply", () => Select(input.Text.Trim()), primary: true); apply.Name = "ApplyEmoji";
        actions.Children.Add(apply);
        actions.Children.Add(Dialogs.Button("Remove Emoji", () => Select("")));
        actions.Children.Add(Dialogs.Button("Cancel", () => dialog.Close(), cancel: true)); panel.Children.Add(actions);

        void Select(string value)
        {
            DocumentEmojis.Set(document, value, settings);
            if (value.Length > 0)
            {
                settings.RecentEmojis.RemoveAll(item => item == value);
                settings.RecentEmojis.Insert(0, value);
                if (settings.RecentEmojis.Count > 20) settings.RecentEmojis.RemoveRange(20, settings.RecentEmojis.Count - 20);
            }
            changed(); dialog.Close();
        }
        void UpdatePreview()
        {
            string value = input.Text.Trim();
            apply.IsEnabled = value.Length > 0 && StringInfo.ParseCombiningCharacters(value).Length == 1 && !value.Any(char.IsControl);
            preview.Source = apply.IsEnabled ? (System.Windows.Media.ImageSource?)colors.Convert(value, typeof(Image), null!, CultureInfo.CurrentCulture) : null;
        }
        input.TextChanged += (_, _) => UpdatePreview(); UpdatePreview();
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); if (openSystemPanel) ShowWindowsPanel(); };
        dialog.Closed += (_, _) => { if (opened) WindowsEmojiPanel.Hide(); };
        return dialog;
    }
}
