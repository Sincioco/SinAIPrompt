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
    public event EventHandler? HtmlChanged;
    public EditorPathStatus PathStatus { get; private set; } = null!;
    bool ready, receiving, disposed;
    string? saveAsPath;
    string? mappedFolder;
    readonly TaskCompletionSource initialized = new();
    Button sourceBack = null!;
    static string Json(object? value) => JsonSerializer.Serialize(value);
    internal MainWindow? HostWindow { get; set; }
    MainWindow Owner => HostWindow ?? (MainWindow)Window.GetWindow(this);

    void InitializeHtmlEditor()
    {
        PathStatus = new(Document, App.Current.Store.DirectoryPath);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(Editor, 1); SetRow(Gutter, 1);
        sourceBack = new Button { Content = "← Visual editor    ·    HTML source", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        sourceBack.Click += (_, _) => ToggleSource(); SetColumnSpan(sourceBack, 2); Children.Add(sourceBack);
        Browser = new WebView2(); SetColumnSpan(Browser, 2); SetRowSpan(Browser, 2); Children.Add(Browser);
        UpdateMode();
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
            // Bundled CSS/modules must reflect the installed build on every launch.
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("sin-editor.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            await RefreshBase();
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
            initialized.TrySetException(ex); await SetSourceAsync(true);
            MessageBox.Show("The visual editor could not start. HTML source remains available. Install Microsoft Edge WebView2 Runtime and restart.\n\n" + ex.Message, "Sin - AI Prompt");
        }
    }

    public async Task RefreshBase(bool reloadDocument = true)
    {
        if (Browser.CoreWebView2 == null) return;
        string folder = Document.Path == null ? App.Current.Store.DirectoryPath : Path.GetDirectoryName(Document.Path)!;
        if (string.Equals(folder, mappedFolder, StringComparison.OrdinalIgnoreCase)) return;
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("sin-document.local", folder, CoreWebView2HostResourceAccessKind.Allow);
        mappedFolder = folder;
        PathStatus.SetFolder(folder);
        // ExecuteScriptAsync returns before a JavaScript Promise settles. Wait for
        // the iframe reload so a command immediately after Save reaches the new DOM.
        if (ready && reloadDocument) await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            Json(new { expression = "window.editor.setBase('https://sin-document.local/')", awaitPromise = true, returnByValue = true }));
    }
    void LoadHtml() => _ = Browser.ExecuteScriptAsync($"window.editor.load({Json(Document.Text)},'https://sin-document.local/')");
    public void AcceptHtml(string html)
    {
        if (IsVisual)
        {
            string normalized = TextFiles.Normalize(html);
            if (Document.Text == normalized) return;
            // The hidden WPF TextBox is populated only when source view is opened.
            Document.Text = normalized; Document.Notify(); App.Current.MarkChanged();
            HtmlChanged?.Invoke(this, EventArgs.Empty); return;
        }
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
        // Finish the WebView callback before any native modal UI can start a
        // nested message loop. Otherwise Rename waits forever for script results.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Normal);
        if (disposed) return;
        try
        {
            object? result = null;
            switch (type)
            {
                case "image-status": PathStatus.SetImage(message.GetProperty("source").GetString() ?? ""); break;
                case "test-path-status" when App.Current.TestMode: result = PathStatus.Text; break;
                case "ready": ready = true; LoadHtml(); ApplyHtmlPreferences(); initialized.TrySetResult(); break;
                case "change": AcceptHtml(message.GetProperty("html").GetString()!); break;
                case "source": await SetSourceAsync(true); break;
                case "annotation-mode": Owner.SetAnnotationMode(this, message.GetProperty("open").GetBoolean()); break;
                case "annotation-copy": AnnotationClipboard.Copy(message.GetProperty("format").GetString()!, message.GetProperty("content").GetString()!, message.GetProperty("objects").GetString()!); break;
                case "annotation-paste": result = AnnotationClipboard.Read(); break;
                case "editor-copy": EditorClipboard.Copy(message.GetProperty("html").GetString()!, message.GetProperty("text").GetString()!); break;
                case "editor-paste": result = await EditorClipboard.ReadAsync(); break;
                case "editor-fonts": result = await EditorFonts.StyleSheetAsync(Browser.CoreWebView2); break;
                case "screen-capture": result = await ScreenCaptureDialog.CaptureAsync(Owner); break;
                case "test-clipboard-formats" when App.Current.TestMode: result = AnnotationClipboard.TestData?.GetFormats(false); break;
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
                case "test-key" when App.Current.TestMode:
                    await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", message.GetProperty("parameters").GetRawText()); break;
                case "test-annotation-layout" when App.Current.TestMode:
                    var origin = Browser.TranslatePoint(new Point(), Owner.ClientArea);
                    result = new { expanded = Owner.IsAnnotating, backgroundEnabled = Owner.Shell.IsEnabled, x = origin.X, y = origin.Y, width = Browser.ActualWidth, height = Browser.ActualHeight, clientWidth = Owner.ClientArea.ActualWidth, clientHeight = Owner.ClientArea.ActualHeight };
                    break;
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
        string result = await Browser.ExecuteScriptAsync("window.editor.html(true)");
        AcceptHtml(JsonSerializer.Deserialize<string>(result) ?? Document.Text);
    }
    public async void ToggleSource() => await SetSourceAsync(IsVisual);
    public async void ShowSource() { if (IsVisual) await SetSourceAsync(true); }
    public async Task SetSourceAsync(bool source)
    {
        if (source)
        {
            await FlushAsync();
            receiving = true;
            try { Editor.Text = Document.Text; }
            finally { receiving = false; }
        }
        IsVisual = !source;
        if (IsVisual && ready) LoadHtml();
        UpdateMode(); FocusEditing();
    }
    void UpdateMode()
    {
        Browser.Visibility = IsVisual ? Visibility.Visible : Visibility.Collapsed;
        if (!IsVisual) PathStatus.SetImage("");
        sourceBack.Visibility = IsVisual ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = IsVisual ? Visibility.Collapsed : Visibility.Visible;
        Gutter.Visibility = !IsVisual && App.Current.Preferences.LineNumbers ? Visibility.Visible : Visibility.Collapsed;
    }
    public async void FocusEditing()
    {
        if (!IsVisual) { Editor.Focus(); return; }
        Browser.Focus();
        // The native browser can receive focus before its editable iframe exists.
        // Honor that request once ready, unless the user has moved elsewhere.
        try { await initialized.Task; } catch { return; }
        if (disposed || !IsVisual || !IsVisible || Owner.ActiveDocument != Document) return;
        await Browser.ExecuteScriptAsync("window.editor.focus()");
    }
    public async void Command(string name, string? value = null)
    {
        if (!ready) return;
        await Browser.ExecuteScriptAsync($"window.editor.command({Json(name)},{Json(value)})");
    }
    void ApplyHtmlPreferences()
    {
        if (Browser == null) return;
        UpdateMode(); Browser.ZoomFactor = Document.Zoom / 100.0;
        if (ready) _ = Browser.ExecuteScriptAsync($"document.documentElement.dataset.theme={Json(App.Current.Preferences.Theme.ToLowerInvariant())};document.querySelector('#toolbar').hidden={Json(!App.Current.Preferences.ShowToolbar)}");
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
    internal async Task<string> RenameImageFolderAsync(string html, string oldName, string newName)
    {
        await initialized.Task;
        return JsonSerializer.Deserialize<string>(await Browser.ExecuteScriptAsync($"window.editor.renameImageFolder({Json(html)},{Json(oldName)},{Json(newName)})"))
            ?? throw new IOException("Could not update the image references.");
    }
    internal async Task RenameOpenImageFolderAsync(string oldName, string newName)
    {
        if (IsVisual)
        {
            string result = await Browser.ExecuteScriptAsync($"window.editor.renameOpenImageFolder({Json(oldName)},{Json(newName)})");
            AcceptHtml(JsonSerializer.Deserialize<string>(result) ?? throw new IOException("Could not refresh the renamed image references."));
        }
        else
        {
            // Keep source edits made while the disk operation ran.
            string original, updated;
            do { original = Editor.Text; updated = await RenameImageFolderAsync(original, oldName, newName); } while (Editor.Text != original);
            AcceptHtml(updated);
        }
    }
    public async Task<string> ExportAsync(bool duplicate = false)
    {
        await initialized.Task;
        if (!IsVisual) { LoadHtml(); await Browser.ExecuteScriptAsync("window.editor.whenLoaded=true"); }
        string key = JsonSerializer.Deserialize<string>(await Browser.ExecuteScriptAsync($"window.editor.beginPortable({Json(duplicate)})"))!;
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
