using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Read-only non-HTML preview. It never enters the document/save/recovery collection.
public sealed class ExplorerPreview : DockPanel, IDisposable
{
    public TextBlock PathStatus { get; } = new();
    internal string FilePath { get; }
    readonly ContentControl content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly string profileFolder;
    WebView2? browser;
    bool disposed;
    public ExplorerPreview(string path, string profileFolder, Action returnToDocument)
    {
        FilePath = path; PathStatus.Text = path; this.profileFolder = profileFolder;
        var back = new Button { Content = "← Return to document", Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => returnToDocument(); SetDock(back, Dock.Top); Children.Add(back);
        var caption = new TextBlock { Text = Path.GetFileName(path) + "  ·  Read-only preview", Margin = new Thickness(12, 0, 12, 8) };
        SetDock(caption, Dock.Top); Children.Add(caption);
        content.Content = new ProgressBar { IsIndeterminate = true, Height = 4, VerticalAlignment = VerticalAlignment.Top };
        Children.Add(content);
    }
    internal async Task LoadAsync(CancellationToken token)
    {
        string path = FilePath;
        if (PromptDirectory.IsImage(path))
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var value = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                value.Freeze(); return value;
            }, token);
            if (disposed || token.IsCancellationRequested) return;
            content.Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(12) };
        }
        else if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            string text = await File.ReadAllTextAsync(path, token);
            if (disposed || token.IsCancellationRequested) return;
            content.Content = new TextBox { Text = text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
                BorderThickness = new Thickness(0), Padding = new Thickness(12), TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        else
        {
            browser = new WebView2(); content.Content = browser;
            string cache = Path.Combine(Path.GetTempPath(), "Sin-AI-Prompt-PDF", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profileFolder)))[..16]);
            var environment = await CoreWebView2Environment.CreateAsync(null, cache, new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-component-update --disable-sync --no-first-run"));
            if (disposed || token.IsCancellationRequested) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (disposed || token.IsCancellationRequested) return;
            var core = browser.CoreWebView2;
            core.Settings.IsScriptEnabled = false; core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
            string address = new Uri(path).AbsoluteUri;
            core.NavigationStarting += (_, e) => e.Cancel = !e.Uri.Split('#')[0].Equals(address, StringComparison.OrdinalIgnoreCase);
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    e.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Offline preview", "");
            };
            core.Navigate(address);
        }
    }
    internal void ShowError(string message) => content.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    public void Dispose() { disposed = true; browser?.Dispose(); }
}
