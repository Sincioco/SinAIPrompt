using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SinAIPrompt;

// Native MessageBox windows are outside Application.Windows and browser exceptions.
// Watch this test process so an unexpected startup dialog cannot vanish at shutdown
// while the test log incorrectly claims success.
internal sealed class EditorStartupSelfTest : IDisposable
{
    readonly WinEvent callback;
    readonly nint hook;
    readonly List<string> errors = [];
    internal IReadOnlyList<string> Errors => errors;
    internal EditorStartupSelfTest()
    {
        callback = Shown;
        hook = SetWinEventHook(0x8002, 0x8002, 0, callback, (uint)Environment.ProcessId, 0, 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception();
    }
    void Shown(nint handle, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != 0 || window == 0) return;
        var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
        if (name.ToString() != "#32770") return;
        var text = new StringBuilder();
        EnumChildWindows(window, (child, _) =>
        {
            var value = new StringBuilder(4096); GetWindowText(child, value, value.Capacity);
            text.AppendLine(value.ToString()); return true;
        }, 0);
        string message = text.ToString();
        if (message.Contains("The visual editor could not start", StringComparison.Ordinal)) errors.Add(message.Trim());
    }
    internal static async Task ClosingDuringStartup(MainWindow window, Action<bool, string> check)
    {
        var original = window.ActiveDocument!;
        foreach (int delay in new[] { 0, 2, 10, 25 })
        {
            var draft = window.NewDocument(); var view = window.CurrentView!;
            window.UpdateLayout(); await Task.Delay(delay);
            window.ActiveDocument = original; window.RemoveDocument(draft);
            await Task.Delay(80);
            check(view.Initialization.IsCompleted && !view.Initialization.IsFaulted,
                $"Closing an editor during startup settles initialization without reporting a failure ({delay} ms)");
        }
    }
    public void Dispose() => UnhookWinEvent(hook);
    delegate void WinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    delegate bool ChildWindow(nint window, nint parameter);
    [DllImport("user32.dll")] static extern nint SetWinEventHook(uint first, uint last, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(nint parent, ChildWindow callback, nint parameter);
}
