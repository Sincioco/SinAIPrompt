using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class EmojiPickerSelfTest
{
    internal static async Task Run(MainWindow owner, Action<bool, string> check)
    {
        var document = owner.ActiveDocument!;
        var settings = App.Current.Preferences;
        string originalEmoji = document.Emoji, originalText = document.Text, originalName = document.Name;
        var originalHistory = settings.RecentEmojis.ToArray();
        Window? dialog = null;
        T Named<T>(string name) where T : FrameworkElement => ScreenCaptureSelfTest.Controls(dialog!).OfType<T>().Single(control => control.Name == name);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Open() { dialog = EmojiPicker.Create(owner, document, settings, App.Current.MarkChanged, openSystemPanel: false); dialog.Show(); dialog.UpdateLayout(); }
        try
        {
            check(WindowsEmojiPanel.IsAvailable, "Windows exposes the native CoreInputView emoji-panel API to this desktop app");
            var colors = (ColorEmoji)owner.FindResource("ColorEmoji");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var image = (BitmapSource)colors.Convert("🦊", typeof(Image), null!, CultureInfo.CurrentCulture)!;
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            check(Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 128 && Math.Abs(pixels[i * 4] - pixels[i * 4 + 2]) > 50),
                "Windows emoji bitmap contains colored pixels rather than monochrome glyphs");
            check(image.IsFrozen && ReferenceEquals(image, colors.Convert("🦊", typeof(Image), null!, CultureInfo.CurrentCulture)) && pixels[3] == 0,
                "Color emoji images retain transparency and reuse a frozen cached bitmap");
            File.WriteAllBytes(Path.Combine(App.Current.Store.DirectoryPath, "color-emoji.png"), Convert.FromBase64String(ScreenCapture.Png(image).Split(',')[1]));
            check(watch.ElapsedMilliseconds < 1000, $"First native emoji rendering stays responsive ({watch.ElapsedMilliseconds} ms including capture)");
            settings.RecentEmojis.Clear(); Open();
            check(!Named<Button>("ApplyEmoji").IsEnabled, "Empty emoji input cannot be applied");
            Named<TextBox>("EmojiInput").Text = "👩🏽‍💻";
            check(Named<Button>("ApplyEmoji").IsEnabled && Named<Image>("EmojiPreview").Source != null,
                "Windows input accepts a complete skin-tone/ZWJ emoji outside the old limited catalog");
            Click(Named<Button>("ApplyEmoji"));
            check(document.Emoji == "👩🏽‍💻" && document.Text == originalText && document.Name == originalName && settings.RecentEmojis.SequenceEqual(["👩🏽‍💻"]),
                "Applying a Windows emoji changes only document metadata and recent history");
            string[] recent = Enumerable.Range(0x1F600, 20).Select(char.ConvertFromUtf32).ToArray();
            settings.RecentEmojis.Clear(); settings.RecentEmojis.AddRange(recent);
            Open(); Named<TextBox>("EmojiInput").Text = "🦊"; Click(Named<Button>("ApplyEmoji"));
            Open(); Click((Button)Named<UniformGrid>("RecentEmojis").Children[4]);
            check(settings.RecentEmojis.Count == 20 && settings.RecentEmojis.Distinct().Count() == 20 && settings.RecentEmojis[0] == recent[3] &&
                settings.RecentEmojis[1] == "🦊" && !settings.RecentEmojis.Contains(recent[19]),
                "Recent Windows emojis retain 20 unique choices in last-used order");
            var savedSettings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
            var savedSession = JsonSerializer.Deserialize<Session>(JsonSerializer.Serialize(new Session { Windows = [owner.Snapshot()] }))!;
            check(savedSettings.RecentEmojis.SequenceEqual(settings.RecentEmojis) && savedSession.Windows[0].Documents.Single(d => d.Id == document.Id).Emoji == document.Emoji,
                "Color emoji assignments and shared history survive session/settings serialization");
            var row = (FrameworkElement)((DataTemplate)owner.FindResource("DocumentTemplate")).LoadContent(); row.DataContext = document;
            row.Measure(new Size(300, 36)); row.Arrange(new Rect(0, 0, 300, 36)); row.UpdateLayout();
            var icon = ScreenCaptureSelfTest.Controls(row).OfType<Image>().Single();
            check(icon.Source != null && icon.Visibility == Visibility.Visible && document.AccessibleName.StartsWith(document.Emoji),
                "Tabs and Document List use the colored bitmap before the filename");
            Open(); Named<TextBox>("EmojiInput").Text = "😀😎";
            check(!Named<Button>("ApplyEmoji").IsEnabled, "Emoji input requires one whole text element rather than clipping a combined sequence");
            Click(ScreenCaptureSelfTest.Controls(dialog!).OfType<Button>().Single(button => Equals(button.Content, "Cancel")));
            check(document.Emoji == recent[3], "Cancel preserves the previous emoji assignment");
            Open(); Click(ScreenCaptureSelfTest.Controls(dialog!).OfType<Button>().Single(button => Equals(button.Content, "Remove Emoji")));
            row.UpdateLayout();
            check(document.Emoji == "" && icon.Visibility == Visibility.Collapsed && settings.RecentEmojis.Count == 20,
                "Remove Emoji hides its navigation slot while retaining recent choices");
        }
        finally
        {
            dialog?.Close(); document.Emoji = originalEmoji; document.Notify();
            settings.RecentEmojis.Clear(); settings.RecentEmojis.AddRange(originalHistory);
            owner.Activate(); await Task.Delay(120);
        }
    }
}
