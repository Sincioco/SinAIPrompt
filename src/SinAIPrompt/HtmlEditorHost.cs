using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public sealed partial class EditorView
{
    public WebView2 Browser { get; private set; } = null!;
    public bool IsVisual { get; private set; } = true;
    bool ready, receiving, disposed;
    string? saveAsPath;
    readonly TaskCompletionSource initialized = new();
    Button sourceBack = null!;
    static string Json(object? value) => JsonSerializer.Serialize(value);
    internal MainWindow? HostWindow { get; set; }
    MainWindow Owner => HostWindow ?? (MainWindow)Window.GetWindow(this);

    void InitializeHtmlEditor()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(Editor, 1); SetRow(Gutter, 1);
        sourceBack = new Button { Content = "← Visual editor    ·    HTML source", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        sourceBack.Click += (_, _) => ToggleSource(); SetColumnSpan(sourceBack, 2); Children.Add(sourceBack);
        Browser = new WebView2(); SetColumnSpan(Browser, 2); SetRowSpan(Browser, 2); Children.Add(Browser);
        Editor.TextChanged += (_, _) => { if (!receiving && ready && IsVisual) LoadHtml(); };
        Loaded += OnHtmlLoaded;
    }

    async void OnHtmlLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnHtmlLoaded;
        try
        {
            string profileId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(App.Current.Store.DirectoryPath)))[..16];
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sin - AI Prompt Cache", profileId);
            var environment = await CoreWebView2Environment.CreateAsync(null, cache, new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-component-update --disable-sync --no-first-run"));
            if (disposed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("sin-editor.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            RefreshBase();
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = App.Current.TestMode;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.NavigationStarting += (_, args) => { if (!args.Uri.StartsWith("https://sin-editor.local/", StringComparison.OrdinalIgnoreCase)) args.Cancel = true; };
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.PermissionRequested += (_, args) => args.State = args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            Browser.CoreWebView2.WebMessageReceived += ReceiveMessage;
            Browser.Source = new Uri("https://sin-editor.local/index.html?v=1.0.0");
        }
        catch (Exception ex)
        {
            initialized.TrySetException(ex); IsVisual = false; UpdateMode();
            MessageBox.Show("The visual editor could not start. HTML source remains available. Install Microsoft Edge WebView2 Runtime and restart.\n\n" + ex.Message, "Sin - AI Prompt");
        }
    }

    public void RefreshBase()
    {
        if (Browser.CoreWebView2 == null) return;
        string folder = Document.Path == null ? App.Current.Store.DirectoryPath : Path.GetDirectoryName(Document.Path)!;
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("sin-document.local", folder, CoreWebView2HostResourceAccessKind.Allow);
        if (ready) _ = Browser.ExecuteScriptAsync("window.editor.setBase('https://sin-document.local/')");
    }
    void LoadHtml() => _ = Browser.ExecuteScriptAsync($"window.editor.load({Json(Document.Text)},'https://sin-document.local/')");
    public void AcceptHtml(string html)
    {
        receiving = true;
        try { if (TextFiles.Normalize(Editor.Text) != TextFiles.Normalize(html)) Editor.Text = html; }
        finally { receiving = false; }
    }
    async void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith("https://sin-editor.local/", StringComparison.OrdinalIgnoreCase)) return;
        using var json = JsonDocument.Parse(e.WebMessageAsJson);
        var message = json.RootElement;
        string type = message.GetProperty("type").GetString()!;
        string? id = message.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        try
        {
            object? result = null;
            switch (type)
            {
                case "ready": ready = true; LoadHtml(); ApplyHtmlPreferences(); initialized.TrySetResult(); break;
                case "change": AcceptHtml(message.GetProperty("html").GetString()!); break;
                case "source": await SetSourceAsync(true); break;
                case "command":
                    if (message.TryGetProperty("html", out var html)) AcceptHtml(html.GetString()!);
                    await Owner.HandleHtmlCommand(message.GetProperty("command").GetString()!); break;
                case "save-image":
                    if (Document.Path == null && !await Owner.SaveDocument(Document)) throw new OperationCanceledException("Save the HTML file before storing a separate image.");
                    result = HtmlAssets.SavePng(Document.Path!, message.GetProperty("data").GetString()!); break;
                case "save-image-as" when saveAsPath != null:
                    result = HtmlAssets.SavePng(saveAsPath, message.GetProperty("data").GetString()!); break;
                case "read-image": result = await HtmlAssets.ReadImageAsync(Document.Path, message.GetProperty("source").GetString()!); break;
                case "templates-load": result = App.Current.Store.Read<List<JsonElement>>("templates.json"); break;
                case "templates-save": App.Current.Store.Write("templates.json", message.GetProperty("templates")); break;
                case "test-mouse" when App.Current.TestMode:
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", message.GetProperty("parameters").GetRawText()); break;
                case "test-capture" when App.Current.TestMode:
                    using (var capture = File.Create(Path.Combine(App.Current.Store.DirectoryPath, "annotation.png")))
                        await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                    break;
                case "export-template":
                    var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Object.pmt-template.json", Filter = "PMT object template|*.pmt-template.json", DefaultExt = ".json" };
                    if (dialog.ShowDialog(Owner) == true) TextFiles.AtomicWrite(dialog.FileName, System.Text.Encoding.UTF8.GetBytes(message.GetProperty("contents").GetString()!)); break;
            }
            if (id != null) Browser.CoreWebView2.PostWebMessageAsJson(Json(new { id, result }));
        }
        catch (Exception ex)
        {
            if (id != null) Browser.CoreWebView2.PostWebMessageAsJson(Json(new { id, error = ex.Message }));
            else MessageBox.Show(Owner, ex.Message, "Sin - AI Prompt");
        }
    }
    public async Task FlushAsync()
    {
        if (!ready || !IsVisual || disposed) return;
        string result = await Browser.ExecuteScriptAsync("window.editor.html()");
        AcceptHtml(JsonSerializer.Deserialize<string>(result) ?? Document.Text);
    }
    public async void ToggleSource() => await SetSourceAsync(IsVisual);
    public async void ShowSource() { if (IsVisual) await SetSourceAsync(true); }
    public async Task SetSourceAsync(bool source)
    {
        if (source) await FlushAsync();
        IsVisual = !source;
        if (IsVisual && ready) LoadHtml();
        UpdateMode(); FocusEditing();
    }
    void UpdateMode()
    {
        Browser.Visibility = IsVisual ? Visibility.Visible : Visibility.Collapsed;
        sourceBack.Visibility = IsVisual ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = IsVisual ? Visibility.Collapsed : Visibility.Visible;
        Gutter.Visibility = !IsVisual && App.Current.Preferences.LineNumbers ? Visibility.Visible : Visibility.Collapsed;
    }
    public void FocusEditing() { if (IsVisual) Browser.Focus(); else Editor.Focus(); }
    public async void Command(string name, string? value = null)
    {
        if (!ready) return;
        if (name == "paste" && Clipboard.ContainsImage()) { await Browser.ExecuteScriptAsync($"window.editor.insertImage({Json(HtmlAssets.ClipboardPng())})"); return; }
        await Browser.ExecuteScriptAsync($"window.editor.command({Json(name)},{Json(value)})");
    }
    void ApplyHtmlPreferences()
    {
        if (Browser == null) return;
        UpdateMode(); Browser.ZoomFactor = Document.Zoom / 100.0;
        if (ready) _ = Browser.ExecuteScriptAsync($"document.documentElement.dataset.theme={Json(App.Current.Preferences.Theme.ToLowerInvariant())}");
    }
    public async Task<string?> PrepareSaveAsAsync(string path)
    {
        if (Document.Path == null || string.Equals(path, Document.Path, StringComparison.OrdinalIgnoreCase)) return null;
        await initialized.Task;
        if (!IsVisual) LoadHtml();
        saveAsPath = path;
        try
        {
            string key = JsonSerializer.Deserialize<string>(await Browser.ExecuteScriptAsync("window.editor.beginRelocate()"))!;
            return await AwaitExportAsync(key);
        }
        finally { saveAsPath = null; }
    }
    public void ReloadSavedHtml() { if (ready) LoadHtml(); }
    public async Task<string> ExportAsync()
    {
        await initialized.Task;
        if (!IsVisual) { LoadHtml(); await Browser.ExecuteScriptAsync("window.editor.whenLoaded=true"); }
        string key = JsonSerializer.Deserialize<string>(await Browser.ExecuteScriptAsync("window.editor.beginPortable()"))!;
        return await AwaitExportAsync(key);
    }
    async Task<string> AwaitExportAsync(string key)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            string raw = await Browser.ExecuteScriptAsync($"window.editor.exportResult({Json(key)})");
            using var value = JsonDocument.Parse(raw);
            if (value.RootElement.ValueKind != JsonValueKind.Null)
            {
                if (value.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.GetString());
                return value.RootElement.GetProperty("html").GetString()!;
            }
            await Task.Delay(100);
        }
        throw new IOException("Export timed out. Check that all referenced images are available.");
    }
    public void Dispose() { disposed = true; Browser.Dispose(); }
}
