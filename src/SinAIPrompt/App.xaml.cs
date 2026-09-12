using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;
    public Store Store { get; private set; } = null!;
    public Settings Preferences { get; private set; } = new();
    public bool TestMode { get; private set; }
    public bool Exiting { get; private set; }
    public string? PersistenceError { get; private set; }
    Mutex? instanceMutex;
    string pipeName = "";
    readonly CancellationTokenSource stop = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    bool sessionChanged;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.ToList();
        string dataPath = StorageLocation.Read();
        int dataIndex = args.IndexOf("--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Count) { dataPath = Path.GetFullPath(args[dataIndex + 1]); args.RemoveRange(dataIndex, 2); }
        TestMode = args.Remove("--self-test");
        if (TestMode && dataIndex < 0) dataPath = Path.Combine(Path.GetTempPath(), "SinAIPrompt-ui-tests", Guid.NewGuid().ToString("N"));
        Store = new(dataPath);
        Directory.CreateDirectory(dataPath);
        pipeName = "SinAIPrompt-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataPath.ToUpperInvariant())))[..24];
        instanceMutex = new Mutex(true, "Local\\" + pipeName, out bool isFirst);
        if (!isFirst && !TestMode)
        {
            try { using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly); await client.ConnectAsync(5000); using var writer = new StreamWriter(client); await writer.WriteLineAsync(JsonSerializer.Serialize(args)); }
            catch (Exception ex) { MessageBox.Show("Could not contact the running editor. Please try again.\n\n" + ex.Message, "Sin - AI Prompt"); }
            Shutdown(); return;
        }
        using var splash = TestMode ? null : new BrandingWindow();
        if (splash != null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            splash.Show();
            await Dispatcher.Yield(DispatcherPriority.Background); // Paint before restoring documents; no minimum display time.
        }
        Preferences = Store.Read<Settings>("settings.json");
        Preferences.NextDocumentNumber = Math.Max(1, Preferences.NextDocumentNumber);
        if (Preferences.Recent.Count > Settings.RecentFileLimit) Preferences.Recent.RemoveRange(Settings.RecentFileLimit, Preferences.Recent.Count - Settings.RecentFileLimit);
        DispatcherUnhandledException += (_, ev) =>
        {
            ev.Handled = true;
            if (Exiting || Dispatcher.HasShutdownStarted) return;
            string message = string.IsNullOrWhiteSpace(ev.Exception.Message) ? "An unexpected error occurred." : ev.Exception.Message;
            MessageBox.Show(message, "Sin - AI Prompt", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        if (TestMode) { await UiSelfTest.Run(this); return; }
        _ = ListenForFiles();
        var session = Preferences.RestoreSession ? Store.Read<Session>("session.json") : new Session();
        foreach (var saved in session.Windows.Where(w => w.Documents.Count > 0)) { var window = new MainWindow(saved); window.Show(); }
        if (Windows.OfType<MainWindow>().FirstOrDefault() is not { } main) { main = new MainWindow(); main.Show(); }
        MainWindow = main; ShutdownMode = ShutdownMode.OnLastWindowClose;
        _ = Task.Run(() =>
        {
            try { FileAssociations.Register(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
        if (args.Count > 0) main.OpenPaths(args);
        timer.Tick += (_, _) => { if (sessionChanged) SaveState(); };
        timer.Start();
        sessionChanged = true;
        SessionEnding += (_, _) => { foreach (var window in Windows.OfType<MainWindow>()) window.FlushAutoSaves(true); SaveState(); };
    }
    async Task ListenForFiles()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(server); string? line = await reader.ReadLineAsync(stop.Token);
                var paths = JsonSerializer.Deserialize<List<string>>(line ?? "[]") ?? [];
                await Dispatcher.InvokeAsync(() =>
                {
                    var main = Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive) ?? Windows.OfType<MainWindow>().FirstOrDefault();
                    if (main == null) { main = new MainWindow(); main.Show(); }
                    if (paths.Count > 0) main.OpenPaths(paths);
                    if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                    main.Activate();
                });
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                try { await Task.Delay(250, stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
    public int NextNumber() { int result = Preferences.NextDocumentNumber++; MarkChanged(); return result; }
    public void UseStorageFolder(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (string.Equals(folder, Store.DirectoryPath, StringComparison.OrdinalIgnoreCase)) return;
        if (!SaveState()) throw new IOException(PersistenceError);
        Directory.CreateDirectory(folder);
        string[] names = ["settings.json", "session.json", "templates.json"];
        foreach (string name in names)
            if (File.Exists(Path.Combine(folder, name))) throw new IOException("The selected folder already contains " + name + ". Choose an empty folder to preserve both sets of settings.");
        foreach (string name in names)
        {
            string source = Path.Combine(Store.DirectoryPath, name), destination = Path.Combine(folder, name);
            if (File.Exists(source)) TextFiles.AtomicWrite(destination, File.ReadAllBytes(source));
        }
        StorageLocation.Write(folder);
        Store = new Store(folder);
        MarkChanged();
    }
    public void MarkChanged() => sessionChanged = true;
    public bool SaveState()
    {
        try
        {
            Store.Write("settings.json", Preferences);
            if (Preferences.RestoreSession) Store.Write("session.json", new Session { Windows = Windows.OfType<MainWindow>().Select(w => w.Snapshot()).ToList() });
            else Store.Write("session.json", new Session());
            sessionChanged = false; PersistenceError = null; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { PersistenceError = ex.Message; return false; }
    }
    public async void ExitAll()
    {
        var windows = Windows.OfType<MainWindow>().ToArray();
        foreach (var window in windows) if (!await window.PrepareClose()) return;
        if (!SaveState()) { MessageBox.Show("Could not save your session. Save your files before closing.\n\n" + PersistenceError, "Sin - AI Prompt"); return; }
        BeginExit();
        foreach (var window in windows) window.Close();
    }
    internal void BeginExit()
    {
        if (Exiting) return;
        Exiting = true;
        timer.Stop();
        stop.Cancel();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        BeginExit(); instanceMutex?.Dispose(); base.OnExit(e);
    }
}
