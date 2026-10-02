using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class SessionPersistenceSelfTest
{
    internal static async Task Run(string folder, Action<bool, string> check)
    {
        var store = new Store(Path.Combine(folder, "recovery-worker"));
        var settings = new Settings { Recent = ["before"], FileEmojis = new() { ["before"] = "a" } };
        string large = "<p>" + new string('R', 8_000_000) + "</p>";
        var document = new Document { Text = large, SavedText = large, DraftName = "Captured", Zoom = 125 };
        var window = new WindowSession { Documents = [document, new() { Text = large, SavedText = large }], Width = 1234 };
        var session = new Session { Windows = [window] };
        var persistence = new SessionPersistence(() => store, () => settings, () => session);

        var baseline = new Store(Path.Combine(folder, "recovery-baseline"));
        var oldTiming = await Measure(() => { baseline.Write("session.json", session); return Task.CompletedTask; });
        var newTiming = await Measure(async () =>
        {
            var saving = persistence.SaveInBackground();
            check(!saving.IsCompleted, "Large recovery snapshots return control before serialization and disk writing finish");
            bool dispatcherAvailable = await Dispatcher.CurrentDispatcher.InvokeAsync(() => !saving.IsCompleted, DispatcherPriority.Input);
            check(dispatcherAvailable, "The input dispatcher runs while large recovery files are being written");
            await saving;
        });
        check(newTiming.Ticks > 0, $"Recovery heartbeat: old UI gap {oldTiming.Gap:F1} ms; background UI gap {newTiming.Gap:F1} ms; background ticks {newTiming.Ticks}");

        // Capture must not hand live document objects, windows, or settings lists
        // to the worker, even if UI edits happen before it starts serializing.
        persistence.MarkChanged();
        var captured = persistence.SaveInBackground();
        document.Text = "new unsaved text"; document.DraftName = "Changed"; document.Zoom = 200;
        window.Width = 1400; window.Documents.RemoveAt(1);
        settings.Recent.Add("after"); settings.FileEmojis["before"] = "b";
        await captured;
        var recovered = store.Read<Session>("session.json");
        var recoveredSettings = store.Read<Settings>("settings.json");
        check(recovered.Windows[0].Width == 1234 && recovered.Windows[0].Documents.Count == 2 &&
            recovered.Windows[0].Documents[0].Text == large && recovered.Windows[0].Documents[0].DraftName == "Captured" && recovered.Windows[0].Documents[0].Zoom == 125 &&
            recoveredSettings.Recent.SequenceEqual(new[] { "before" }) && recoveredSettings.FileEmojis["before"] == "a",
            "Background recovery captures detached documents, windows and mutable settings collections");

        document.Text = large; document.SavedText = "saved text";
        persistence.MarkChanged(); var queued = persistence.SaveInBackground();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        document.Text = "intermediate"; persistence.MarkChanged(); _ = persistence.SaveInBackground();
        document.Text = "latest before close"; persistence.MarkChanged();
        check(!queued.IsCompleted, "An explicit close snapshot can be requested while background recovery is still active");
        check(persistence.SaveNow(), "Explicit save drains earlier background recovery before publishing current state");
        await queued;
        check(store.Read<Session>("session.json").Windows[0].Documents[0].Text == "latest before close" &&
            File.Exists(Path.Combine(store.DirectoryPath, "session.json.bak")),
            "Queued recovery cannot overwrite the close snapshot and retains the previous atomic backup");

        string recoveryPath = Path.Combine(store.DirectoryPath, "session.json");
        document.Text = "retry after sharing failure"; persistence.MarkChanged();
        using (var locked = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await persistence.SaveInBackground();
            check(persistence.Error != null && store.Read<Session>("session.json").Windows[0].Documents[0].Text == "latest before close",
                "Failed background replacement preserves the recoverable file and reports its error");
        }
        await persistence.SaveInBackground();
        check(persistence.Error == null && store.Read<Session>("session.json").Windows[0].Documents[0].Text == document.Text &&
            !Directory.EnumerateFiles(store.DirectoryPath, "*.tmp").Any(),
            "A failed recovery remains pending for retry and cleans temporary files");

        var oldStore = store;
        check(persistence.SaveNow(), "Storage relocation has a completed recovery barrier");
        store = new Store(Path.Combine(folder, "recovery-relocated"));
        document.Text = "new storage"; persistence.MarkChanged(); await persistence.SaveInBackground();
        check(store.Read<Session>("session.json").Windows[0].Documents[0].Text == "new storage" &&
            oldStore.Read<Session>("session.json").Windows[0].Documents[0].Text == "retry after sharing failure",
            "Recovery after profile relocation writes only to the new captured store");
        settings.RestoreSession = false; persistence.MarkChanged(); await persistence.SaveInBackground();
        check(store.Read<Session>("session.json").Windows.Count == 0 && !store.Read<Settings>("settings.json").RestoreSession,
            "Disabling session restoration preserves the empty-session JSON contract");

        settings.RestoreSession = true;
        document.Text = new string('x', 65535) + "\ud83d\ude80\\\"\r\n\t\0<>&+'é中文" + new string('y', 65535) + "\ud83d";
        document.SavedText = ""; document.DraftName = null;
        persistence.MarkChanged(); await persistence.SaveInBackground();
        check(File.ReadAllBytes(Path.Combine(store.DirectoryPath, "session.json")).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(session)),
            "Chunked recovery is byte-identical for split emoji, Unicode, controls, quotes, backslashes, HTML and incomplete surrogates");

        string shortText = new string('s', 60000);
        window.Documents = Enumerable.Range(0, 40).Select(_ => new Document { Text = shortText, SavedText = shortText }).ToList();
        long allocated = GC.GetTotalAllocatedBytes();
        persistence.MarkChanged(); await persistence.SaveInBackground();
        allocated = GC.GetTotalAllocatedBytes() - allocated;
        check(allocated < 4_000_000 && File.ReadAllBytes(Path.Combine(store.DirectoryPath, "session.json")).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(session)),
            $"Many shorter documents also keep recovery buffers bounded and retain exact JSON ({allocated:N0} allocated bytes)");
    }

    static async Task<(double Gap, int Ticks)> Measure(Func<Task> action)
    {
        var clock = Stopwatch.StartNew(); double previous = 0, gap = 0; int ticks = 0;
        var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        heartbeat.Tick += (_, _) => { double now = clock.Elapsed.TotalMilliseconds; gap = Math.Max(gap, now - previous); previous = now; ticks++; };
        heartbeat.Start();
        try { await Task.Delay(40); await action(); await Task.Delay(40); }
        finally { heartbeat.Stop(); }
        return (gap, ticks);
    }
}
