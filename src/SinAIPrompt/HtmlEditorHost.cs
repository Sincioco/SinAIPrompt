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
    public RibbonWebView Browser { get; private set; } = null!;
    internal event EventHandler? ChromeChanged;
    internal double RibbonHeight { get; private set; }
    bool ribbonMeasured;
    internal void ReserveRibbonHeight(double height)
    {
        if (!ribbonMeasured) RibbonHeight = App.Current.Preferences.ShowToolbar ? height : 0;
    }
    double navigationInset;
    double? appliedInset;
    bool? readOnlyApplied;
    void ApplyReadOnly()
    {
        Editor.IsReadOnly = Document.IsReadOnly;
        if (ready && !disposed && readOnlyApplied != Document.IsReadOnly)
        { readOnlyApplied = Document.IsReadOnly; _ = Browser.ExecuteScriptAsync($"window.editor.setReadOnly({Json(Document.IsReadOnly)})"); }
    }
    internal void SetNavigationInset(double width)
    {
        navigationInset = width;
        Browser.SetChrome(width, RibbonHeight);
        double pixels = width / Browser.ZoomFactor;
        if (ready && !disposed && appliedInset != pixels)
        { appliedInset = pixels; _ = Browser.ExecuteScriptAsync($"window.editor.setNavigationInset({Json(pixels)})"); }
    }
    public bool IsVisual { get; private set; } = true;
    public event EventHandler? HtmlChanged;
    public EditorPathStatus PathStatus { get; private set; } = null!;
    bool ready, receiving, disposed;
    string? saveAsPath;
    string? mappedFolder;
    readonly TaskCompletionSource initialized = new();
    internal Task Initialization => initialized.Task;
    readonly TaskCompletionSource painted = new();
    internal Task FirstPaint => painted.Task;
    Button sourceBack = null!;
    static string Json(object? value) => JsonSerializer.Serialize(value);
    internal MainWindow? HostWindow { get; set; }
    readonly OriginalImages originalImages = new(App.Current.Store.DirectoryPath);
    MainWindow Owner => HostWindow ?? (MainWindow)Window.GetWindow(this);

    void InitializeHtmlEditor()
    {
        PathStatus = new(Document, App.Current.Store.DirectoryPath);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        SetRow(Editor, 1); SetRow(Gutter, 1);
        sourceBack = new Button { Content = "← Visual editor    ·    HTML source", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        sourceBack.Click += (_, _) => ToggleSource(); SetColumnSpan(sourceBack, 2); Children.Add(sourceBack);
        Browser = new RibbonWebView(); SetColumnSpan(Browser, 2); SetRowSpan(Browser, 2); Children.Add(Browser);
        Browser.IsVisibleChanged += async (_, _) =>
        {
            // Fullscreen temporarily reparents the host. Check the settled layout
            // before stopping media when switching documents or opening source.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (!Browser.IsVisible) StopMedia();
        };
        UpdateMode();
        Editor.TextChanged += (_, _) => { if (!receiving && ready && IsVisual) LoadHtml(); };
        Loaded += OnHtmlLoaded;
    }

    internal void StopMedia()
    {
        if (ready && !disposed) _ = Browser.ExecuteScriptAsync("window.editor.stopMedia()");
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
            if (disposed) return;
            // Bundled CSS/modules must reflect the installed build on every launch.
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
            if (disposed) return;
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("sin-editor.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            originalImages.Attach(Browser.CoreWebView2);
            await RefreshBase();
            if (disposed) return;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = App.Current.TestMode;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.NavigationStarting += (_, args) => { if (!args.Uri.StartsWith("https://sin-editor.local/", StringComparison.OrdinalIgnoreCase)) args.Cancel = true; };
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.PermissionRequested += (_, args) => args.State = args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            Browser.CoreWebView2.WebMessageReceived += ReceiveMessage;
            _ = new EditorFullscreen(Browser.CoreWebView2, () => Owner, open => Owner.SetAnnotationMode(this, open));
            Browser.Source = new Uri($"https://sin-editor.local/index.html?v=1.0.0&toolbar={Json(App.Current.Preferences.ShowToolbar)}&wrap={Json(App.Current.Preferences.WrapToolbar)}");
        }
        catch (Exception ex)
        {
            // Closing an editor aborts its pending WebView creation. Disposal has
            // already canceled waiters; it is not a missing-runtime/startup failure.
            if (disposed) return;
            initialized.TrySetException(ex); await SetSourceAsync(true); painted.TrySetResult();
            MessageBox.Show("The visual editor could not start. HTML source remains available.\n\n" + ex.Message, "Sin - AI Prompt");
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
        if (Document.IsReadOnly) return;
        if (IsVisual)
        {
            string normalized = TextFiles.Normalize(html);
            if (Document.Text == normalized) return;
            // The hidden WPF TextBox is populated only when source view is opened.
            Document.Edit(normalized); App.Current.MarkChanged();
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
                case "ribbon-height": ribbonMeasured = true; RibbonHeight = message.GetProperty("height").GetDouble() * Browser.ZoomFactor; ChromeChanged?.Invoke(this, EventArgs.Empty); break;
                case "chrome-overlay":
                    Browser.SetPopups(message.GetProperty("rects").EnumerateArray().Select(rect => new Rect(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(), rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble())).ToArray(), message.GetProperty("modal").GetBoolean());
                    ChromeChanged?.Invoke(this, EventArgs.Empty); break;
                case "open-files": await Owner.OpenDroppedPathsAsync(e.AdditionalObjects.OfType<CoreWebView2File>().Select(file => file.Path).ToArray()); break;
                case "insert-images":
                    foreach (var file in e.AdditionalObjects.OfType<CoreWebView2File>())
                        await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", Json(new { expression = $"window.editor.insertImage({Json(new Uri(file.Path).AbsoluteUri)})", awaitPromise = true }));
                    break;
                case "test-path-status" when App.Current.TestMode: result = PathStatus.Text; break;
                case "ready": ready = true; LoadHtml(); ApplyHtmlPreferences(); ApplyReadOnly(); initialized.TrySetResult(); break;
                case "painted": painted.TrySetResult(); break;
                case "change": AcceptHtml(message.GetProperty("html").GetString()!); break;
                case "source": await SetSourceAsync(true); break;
                case "annotation-mode": Owner.SetAnnotationMode(this, message.GetProperty("open").GetBoolean()); break;
                case "annotation-copy": AnnotationClipboard.Copy(message.GetProperty("format").GetString()!, message.GetProperty("content").GetString()!, message.GetProperty("objects").GetString()!); break;
                case "annotation-paste": result = AnnotationClipboard.Read(); break;
                case "editor-copy": EditorClipboard.Copy(message.GetProperty("html").GetString()!, message.GetProperty("text").GetString()!, message.TryGetProperty("internalHtml", out var internalHtml) ? internalHtml.GetString() : null, Document.Id.ToString()); break;
                case "editor-paste": result = await EditorClipboard.ReadAsync(Document.Id.ToString()); break;
                case "editor-fonts": result = await EditorFonts.StyleSheetAsync(Browser.CoreWebView2); break;
                case "link-preview": result = await LinkPreview.FetchAsync(message.GetProperty("url").GetString()!); break;
                case "youtube-preview": result = await LinkPreview.FetchVideoAsync(message.GetProperty("url").GetString()!, message.GetProperty("saveThumbnail").GetBoolean()); break;
                case "open-video": LinkPreview.OpenVideo(message.GetProperty("video").GetString()!); break;
                case "screen-capture": result = await ScreenCaptureDialog.CaptureAsync(Owner); break;
                case "region-capture": result = await ScreenCaptureDialog.CaptureRegionAsync(Owner); break;
                case "test-clipboard-formats" when App.Current.TestMode: result = AnnotationClipboard.TestData?.GetFormats(false); break;
                case "command":
                    if (message.TryGetProperty("html", out var html)) AcceptHtml(html.GetString()!);
                    await Owner.HandleHtmlCommand(message.GetProperty("command").GetString()!); break;
                case "save-image":
                    if (Document.IsReadOnly) throw new IOException("Unlock the document before adding images.");
                    if (Document.Path == null && !await Owner.SaveDocument(Document)) throw new OperationCanceledException("Save the HTML file before storing a separate image.");
                    result = await HtmlAssets.SavePngAsync(Document.Path!, message.GetProperty("data").GetString()!); break;
                case "save-image-as" when saveAsPath != null:
                    result = await HtmlAssets.SavePngAsync(saveAsPath, message.GetProperty("data").GetString()!); break;
                case "read-image": result = await HtmlAssets.ReadImageAsync(Document.Path, originalImages.Source(message.GetProperty("source").GetString()!)); break;
                case "map-original-image":
                    result = originalImages.Map(Document.Path, message.GetProperty("source").GetString()!, message.TryGetProperty("base", out var mapBase) ? mapBase.GetString() : null); break;
                case "reference-image":
                    if (Document.IsReadOnly) throw new IOException("Unlock the document before adding images.");
                    string? originalSource = OriginalImages.ChooseSource(Owner, message.GetProperty("source").GetString()!);
                    if (originalSource == null) break;
                    bool absoluteReference = message.GetProperty("absolute").GetBoolean();
                    if (!absoluteReference && Document.Path == null && !await Owner.SaveDocument(Document)) break;
                    string? referenceBase = message.TryGetProperty("base", out var imageBase) ? imageBase.GetString() : null;
                    var mappedImage = originalImages.Map(Document.Path, originalSource, referenceBase);
                    result = new { reference = originalImages.Reference(Document.Path, originalSource, absoluteReference, referenceBase), mappedImage.display }; break;
                case "relocate-original-image" when saveAsPath != null:
                    string originalReference = message.GetProperty("source").GetString()!;
                    result = originalImages.Reference(Document.Path, originalReference, Uri.TryCreate(originalReference, UriKind.Absolute, out _),
                        message.TryGetProperty("base", out var relocationBase) ? relocationBase.GetString() : null, saveAsPath); break;
                case "open-image": await ImageExternalViewer.OpenAsync(App.Current.Store.DirectoryPath, message.GetProperty("data").GetString()!); break;
                case "rename-image":
                    if (Document.Path == null) throw new IOException("Save the document before renaming a linked image.");
                    var paths = ImageReferences.LocalPaths("<img src=\"" + System.Net.WebUtility.HtmlEncode(message.GetProperty("source").GetString()) + "\">", Document.Path);
                    string path = paths.SingleOrDefault() ?? throw new IOException("Only local image files can be renamed.");
                    Dialogs.RenameFile(Owner, Path.GetFileName(path), name => Owner.RenameExplorerImage(new(path, false, Document.Path), name), keepExtension: true); break;
                case "reuse-image": result = await HtmlAssets.ReusePngAsync(Document.Path, message.GetProperty("data").GetString()!); break;
                case "templates-load": result = App.Current.Store.Read<List<JsonElement>>("templates.json"); break;
                case "recent-colors": result = RecentColors.Read(App.Current.Preferences); break;
                case "color-used": RecentColors.Use(App.Current.Preferences, message.GetProperty("value").GetString()!); App.Current.MarkChanged(); break;
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
                    string screenshot = message.TryGetProperty("name", out var name) ? Path.GetFileName(name.GetString()) + ".png" : "annotation.png";
                    using (var capture = File.Create(Path.Combine(App.Current.Store.DirectoryPath, screenshot)))
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
        ChromeChanged?.Invoke(this, EventArgs.Empty);
    }
    public async void FocusEditing()
    {
        if (!IsVisual) { Editor.Focus(); return; }
        Browser.Focus();
        // The native browser can receive focus before its editable iframe exists.
        // Honor that request once ready, unless the user has moved elsewhere.
        try { await initialized.Task; } catch { return; }
        if (disposed || !IsVisual || !IsVisible || Owner.CurrentView != this) return;
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
        SetNavigationInset(navigationInset);
        if (ready) _ = Browser.ExecuteScriptAsync($"document.documentElement.dataset.theme={Json(App.Current.Preferences.Theme.ToLowerInvariant())};document.querySelector('#toolbar').hidden={Json(!App.Current.Preferences.ShowToolbar)};window.editor.setImageStorage({Json(App.Current.Preferences.ImageStorage)});window.editor.setToolbarWrap({Json(App.Current.Preferences.WrapToolbar)})");
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
    internal async Task<string> RenameImageFileAsync(string html, string parent, string oldPath, string destination, bool live = false)
    {
        await initialized.Task;
        string args = $"{Json(new Uri(parent).AbsoluteUri)},{Json(new Uri(oldPath).AbsoluteUri)},{Json(new Uri(destination).AbsoluteUri)}";
        string expression = live ? $"window.editor.renameOpenImageFile({args})" : $"window.editor.renameImageFile({Json(html)},{args})";
        string response = await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", Json(new { expression, awaitPromise = true, returnByValue = true }));
        using var result = JsonDocument.Parse(response);
        if (!result.RootElement.GetProperty("result").TryGetProperty("value", out var value)) throw new IOException("Could not update the image references.");
        return value.GetString() ?? throw new IOException("Could not update the image references.");
    }
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
    public void Dispose() { disposed = true; initialized.TrySetCanceled(); painted.TrySetCanceled(); (Parent as EditorSurface)?.Release(this); originalImages.Dispose(); Browser.Dispose(); }
}
