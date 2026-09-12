using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Shared local artwork for the startup splash and Help/About. Startup owns when
// to show/close it; this window owns only its presentation and dismissal gestures.
internal sealed class BrandingWindow : Window, IDisposable
{
    const string Artwork = "pack://application:,,,/Assets/Splash Screens/Sin-AI-Prompt-Splash-2400x1440.png";
    static BitmapImage? artwork;
    bool closed;
    internal static async void ShowAbout(Window owner)
    {
        // Let the menu finish restoring focus before opening a dismiss-on-blur window.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        new BrandingWindow(owner).Show();
    }
    internal BrandingWindow(Window? owner = null)
    {
        bool about = owner != null;
        Title = about ? "About Sin - AI Prompt" : "Sin - AI Prompt — Starting";
        Owner = owner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; WindowStartupLocation = about ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Width = Math.Min(about ? 1000 : 800, SystemParameters.WorkArea.Width * .85);
        Height = Width * .6; FontFamily = new FontFamily("Segoe UI"); Foreground = new SolidColorBrush(Color.FromRgb(35, 51, 72));
        if (artwork == null)
        {
            artwork = new BitmapImage(); artwork.BeginInit(); artwork.UriSource = new Uri(Artwork);
            artwork.DecodePixelWidth = 1200; artwork.CacheOption = BitmapCacheOption.OnLoad; artwork.EndInit(); artwork.Freeze();
        }
        var canvas = new Grid { Width = 1200, Height = 720 };
        canvas.Children.Add(new Image { Source = artwork, Stretch = Stretch.Fill });
        if (about) canvas.Children.Add(AboutDetails());
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        MouseLeftButtonUp += (_, _) => Dismiss();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } };
        Deactivated += (_, _) => Dismiss();
        Closed += (_, _) => closed = true;
    }
    static Border AboutDetails()
    {
        var panel = new StackPanel();
        var assembly = typeof(BrandingWindow).Assembly;
        string built = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(item => item.Key == "BuildDate")?.Value ?? "Unavailable";
        panel.Children.Add(new TextBlock { Text = "Programmed by: Louiery R. Sincioco", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"September 11, 2026 · Built {built}", FontSize = 14, Margin = new Thickness(0, 5, 0, 5) });
        panel.Children.Add(new TextBlock { Text = $"Version {assembly.GetName().Version?.ToString(3)}", FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var (label, address) in new[] { ("louiery@gmail.com", "mailto:louiery@gmail.com"), ("sincioco.com", "https://sincioco.com"), ("github.com/sincioco/sinaiprompt", "https://github.com/sincioco/sinaiprompt") })
        {
            var link = new Hyperlink(new Run(label)) { NavigateUri = new Uri(address), Foreground = new SolidColorBrush(Color.FromRgb(24, 90, 189)) };
            link.RequestNavigate += (_, e) =>
            {
                e.Handled = true;
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show(ex.Message, "Open Link"); }
            };
            var text = new TextBlock { FontSize = 16, Margin = new Thickness(0, 2, 0, 0) }; text.Inlines.Add(link); panel.Children.Add(text);
        }
        // Cover the artwork's startup-status caption while presenting About details.
        return new Border { Child = panel, Margin = new Thickness(66, 512, 0, 0), Padding = new Thickness(12, 10, 12, 12),
            Width = 585, Background = new SolidColorBrush(Color.FromRgb(247, 249, 252)), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    }
    void Dismiss() { if (!closed) { closed = true; Close(); } }
    public void Dispose() => Dismiss();
}
