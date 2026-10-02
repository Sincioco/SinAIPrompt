using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Capture on the dispatcher; serialize and publish recovery on one worker. A
// synchronous save is a barrier used before closing or moving the profile.
internal sealed class SessionPersistence(Func<Store> getStore, Func<Settings> getSettings, Func<Session> getSession)
{
    sealed record Snapshot(Store Store, byte[] Settings, Session Session, long Revision);
    static readonly JsonSerializerOptions recoveryJson = new() { Converters = { new RecoveryString() } };
    readonly object gate = new();
    long revision = 1, savedRevision, queuedRevision;
    Snapshot? pending;
    Task? writer;
    string? error;
    internal string? Error { get { lock (gate) return error; } }

    internal void MarkChanged() { lock (gate) revision++; }

    internal Task SaveInBackground()
    {
        lock (gate) if (revision == savedRevision || writer != null && revision == queuedRevision) return writer ?? Task.CompletedTask;
        var snapshot = Capture();
        lock (gate)
        {
            pending = snapshot; // A busy writer needs only the newest queued state.
            queuedRevision = snapshot.Revision;
            return writer ??= Task.Run(WritePending);
        }
    }

    internal bool SaveNow()
    {
        var snapshot = Capture();
        Task? running;
        lock (gate) { pending = null; running = writer; }
        // The worker never calls the dispatcher, so this cannot deadlock. No
        // queued older state may be published after this explicit save returns.
        running?.GetAwaiter().GetResult();
        return Publish(snapshot);
    }

    Snapshot Capture()
    {
        long current;
        lock (gate) current = revision;
        var settings = getSettings();
        var session = settings.RestoreSession ? getSession() : new Session();
        var copy = new Session
        {
            Version = session.Version,
            Windows = session.Windows.Select(window => new WindowSession
            {
                ShowTabs = window.ShowTabs, ActiveIndex = window.ActiveIndex,
                Width = window.Width, Height = window.Height, Maximized = window.Maximized,
                DocumentList = window.DocumentList, ListWidth = window.ListWidth,
                Documents = window.Documents.Select(document => new Document
                {
                    Id = document.Id, Path = document.Path, DraftName = document.DraftName,
                    Text = document.Text, SavedText = document.SavedText,
                    EncodingName = document.EncodingName, SavedEncoding = document.SavedEncoding,
                    NewLine = document.NewLine, SavedNewLine = document.SavedNewLine,
                    Fingerprint = document.Fingerprint, UntitledNumber = document.UntitledNumber,
                    AutoSave = document.AutoSave, Pinned = document.Pinned, IsPrivate = document.IsPrivate,
                    Emoji = document.Emoji, IsReadOnly = document.IsReadOnly,
                    CreatedUtc = document.CreatedUtc, ModifiedUtc = document.ModifiedUtc,
                    Caret = document.Caret, SelectionLength = document.SelectionLength,
                    Scroll = document.Scroll, HorizontalScroll = document.HorizontalScroll, Zoom = document.Zoom
                }).ToList()
            }).ToList()
        };
        // Settings are small; detach their mutable lists/dictionary without
        // serializing any document text on the UI thread.
        return new(getStore(), JsonSerializer.SerializeToUtf8Bytes(settings), copy, current);
    }

    void WritePending()
    {
        while (true)
        {
            Snapshot? snapshot;
            lock (gate)
            {
                snapshot = pending; pending = null;
                if (snapshot == null) { writer = null; return; }
            }
            Publish(snapshot);
        }
    }

    bool Publish(Snapshot snapshot)
    {
        string? failure = null;
        try
        {
            Directory.CreateDirectory(snapshot.Store.DirectoryPath);
            TextFiles.AtomicWrite(Path.Combine(snapshot.Store.DirectoryPath, "settings.json"), snapshot.Settings, true);
            WriteSession(Path.Combine(snapshot.Store.DirectoryPath, "session.json"), snapshot.Session);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failure = ex.Message; }
        lock (gate)
        {
            if (failure == null) savedRevision = snapshot.Revision;
            error = failure;
        }
        return failure == null;
    }

    static void WriteSession(string path, Session session)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                using var writer = new Utf8JsonWriter(stream);
                JsonSerializer.Serialize(writer, session, recoveryJson);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Embedded screenshots can make a single HTML string tens of megabytes.
    // .NET 10's segmented writer preserves its escaping/surrogate handling while
    // bounding temporary UTF-8 buffers instead of allocating for the whole value.
    sealed class RecoveryString : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetString();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            const int chunk = 65536;
            if (value.Length <= chunk)
            {
                writer.WriteStringValue(value);
                if (writer.BytesPending >= chunk) writer.Flush();
                return;
            }
            for (int start = 0; start < value.Length; start += chunk)
            {
                int length = Math.Min(chunk, value.Length - start);
                writer.WriteStringValueSegment(value.AsSpan(start, length), start + length == value.Length);
                writer.Flush();
            }
        }
    }
}
