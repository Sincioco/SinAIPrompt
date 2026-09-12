using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

internal static class BrandingSelfTest
{
    internal static async Task Run(MainWindow owner, Action<bool, string> check)
    {
        owner.AboutMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(80);
        using var about = Application.Current.Windows.OfType<BrandingWindow>().Single();
        await Task.Delay(60); about.UpdateLayout();
        var canvas = (Grid)((Viewbox)about.Content).Child;
        check(((BitmapImage)canvas.Children.OfType<Image>().Single().Source).UriSource.ToString().Contains("Sin-AI-Prompt-Splash-2400x1440.png") && about.Owner == owner,
            "Help/About opens with the permanently embedded splash artwork");
        var details = ((StackPanel)canvas.Children.OfType<Border>().Single().Child).Children.OfType<TextBlock>().ToArray();
        check(details.Any(text => text.Text.Contains("Louiery R. Sincioco")) && details.Any(text => text.Text.Contains("Built ") && !text.Text.Contains("Unavailable")),
            "About displays author and actual compiler-generated build date");
        check(details.SelectMany(text => text.Inlines.OfType<Hyperlink>()).Select(link => link.NavigateUri.Scheme).SequenceEqual(new[] { "mailto", "https", "https" }),
            "About uses native clickable email, website and repository hyperlinks");
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(canvas), null, new Rect(0, 0, 1200, 720));
        var preview = new RenderTargetBitmap(1200, 720, 96, 96, PixelFormats.Pbgra32); preview.Render(drawing);
        File.WriteAllBytes(Path.Combine(App.Current.Store.DirectoryPath, "about.png"), Convert.FromBase64String(ScreenCapture.Png(preview).Split(',')[1]));
        about.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(about), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        check(!about.IsVisible, "Escape dismisses About");
        using var splash = new BrandingWindow(); splash.Show(); await Task.Delay(40);
        splash.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        check(!splash.IsVisible, "Clicking the splash artwork dismisses it");
        using var outside = new BrandingWindow(owner); outside.Show(); await Task.Delay(40);
        owner.Activate(); await Task.Delay(40);
        check(!outside.IsVisible, "Moving focus outside the branding window dismisses it");
    }
}
