using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SinAIPrompt.Core;

public sealed class Document : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Path { get; set; }
    public string? DraftName { get; set; }
    public string Text { get; set; } = "";
    public string SavedText { get; set; } = "";
    public string EncodingName { get; set; } = "UTF-8";
    public string SavedEncoding { get; set; } = "UTF-8";
    public string NewLine { get; set; } = "\r\n";
    public string SavedNewLine { get; set; } = "\r\n";
    public string? Fingerprint { get; set; }
    public int UntitledNumber { get; set; } = 1;
    public bool AutoSave { get; set; }
    public bool Pinned { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public int Caret { get; set; }
    public int SelectionLength { get; set; }
    public double Scroll { get; set; }
    public double HorizontalScroll { get; set; }
    public int Zoom { get; set; } = 100;
    public bool Dirty => Text != SavedText || EncodingName != SavedEncoding || NewLine != SavedNewLine;
    public string Name => Path != null ? System.IO.Path.GetFileName(Path) : DraftName ?? $"Prompt {UntitledNumber}";
    public string AccessibleName => $"{Name}. {(Pinned ? "Pinned. " : "")}{(Dirty ? "Modified" : "Unmodified")}.";
    public string Tooltip => (Path ?? Name) + (Dirty ? "\nUnsaved changes" : "");
    public string Marker => (Pinned ? "📌" : "") + (Dirty ? "•" : "");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() { foreach (var name in new[] { nameof(Name), nameof(Dirty), nameof(Marker), nameof(Tooltip), nameof(AccessibleName) }) PropertyChanged?.Invoke(this, new(name)); }
}

public static class TextFiles
{
    static TextFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    public static int ToNormalizedOffset(string raw, int offset)
    {
        offset = Math.Clamp(offset, 0, raw.Length);
        int result = offset;
        for (int i = 0; i + 1 < offset; i++) if (raw[i] == '\r' && raw[i + 1] == '\n') { result--; i++; }
        return result;
    }
    public static int FromNormalizedOffset(string raw, int offset)
    {
        int i = 0, normalized = 0;
        while (i < raw.Length && normalized < offset) { if (raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n') i++; i++; normalized++; }
        return i;
    }
    public static Encoding EncodingFor(string name, bool strict = true) => name switch
    {
        "UTF-8 with BOM" => new UTF8Encoding(true, strict),
        "UTF-16 LE" => new UnicodeEncoding(false, true, strict),
        "UTF-16 BE" => new UnicodeEncoding(true, true, strict),
        "UTF-32 LE" => new UTF32Encoding(false, true, strict),
        "UTF-32 BE" => new UTF32Encoding(true, true, strict),
        "ANSI" => Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage, strict ? EncoderFallback.ExceptionFallback : EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback),
        _ => new UTF8Encoding(false, strict)
    };
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static Document Open(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(path);
        string name = "UTF-8"; int skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { name = "UTF-32 LE"; skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { name = "UTF-32 BE"; skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { name = "UTF-8 with BOM"; skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { name = "UTF-16 LE"; skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { name = "UTF-16 BE"; skip = 2; }
        string raw;
        try { raw = EncodingFor(name).GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) when (skip == 0) { name = "ANSI"; raw = EncodingFor(name).GetString(bytes); }
        if (raw.Contains('\0')) throw new InvalidDataException("This file contains binary data or an unsupported encoding. It has not been opened as text.");
        string nl = DetectNewLine(raw);
        return new() { Path = path, Text = Normalize(raw), SavedText = Normalize(raw), NewLine = nl, SavedNewLine = nl, EncodingName = name, SavedEncoding = name, Fingerprint = Hash(bytes), CreatedUtc = File.GetCreationTimeUtc(path), ModifiedUtc = File.GetLastWriteTimeUtc(path) };
    }
    public static string DetectNewLine(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++) { if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; } else cr++; } else if (text[i] == '\n') lf++; }
        return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
    }
    public static bool ChangedOnDisk(Document doc) => doc.Path != null && doc.Fingerprint != null && (!File.Exists(doc.Path) || Hash(File.ReadAllBytes(doc.Path)) != doc.Fingerprint);
    public static string RenamePath(string path, string fileName)
    {
        ValidateFileName(fileName);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, fileName);
    }
    public static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName)) ||
            fileName.EndsWith('.') || fileName.EndsWith(' ') || fileName.Length > 255 ||
            Regex.IsMatch(fileName, @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Enter a valid file name, including its extension, without a folder path or reserved Windows name.");
    }
    public static void Rename(string path, string destination)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The file no longer exists at its original location.", path);
        if (string.Equals(path, destination, StringComparison.Ordinal)) return;
        // File.Move never replaces an existing destination; no save or re-encoding occurs.
        File.Move(path, destination);
    }
    public static void Save(Document doc, string path, bool overwriteConflict = false)
    {
        path = System.IO.Path.GetFullPath(path);
        bool same = string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase);
        if (same && !overwriteConflict && ChangedOnDisk(doc)) throw new IOException("The file changed outside Sin - AI Prompt.");
        // Preserve original bytes (including mixed endings) when an unchanged file is saved/copied.
        byte[] bytes;
        if (!doc.Dirty && doc.Path != null && File.Exists(doc.Path) && !ChangedOnDisk(doc)) bytes = File.ReadAllBytes(doc.Path);
        else { var enc = EncodingFor(doc.EncodingName); bytes = [.. enc.GetPreamble(), .. enc.GetBytes(doc.Text.Replace("\n", doc.NewLine))]; }
        AtomicWrite(path, bytes);
        doc.DraftName = null;
        if (doc.CreatedUtc == default) doc.CreatedUtc = File.GetCreationTimeUtc(path);
        doc.ModifiedUtc = File.GetLastWriteTimeUtc(path);
        doc.Path = path; doc.SavedText = doc.Text; doc.SavedEncoding = doc.EncodingName; doc.SavedNewLine = doc.NewLine; doc.Fingerprint = Hash(bytes); doc.Notify();
    }
    public static void AtomicWrite(string path, byte[] bytes, bool backup = false)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(tmp, path, backup ? path + ".bak" : null);
            else File.Move(tmp, path);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

public sealed class Settings
{
    public const int RecentFileLimit = 10;
    public string Theme { get; set; } = "System";
    public int NextDocumentNumber { get; set; } = 1;
    public string AutoSaveDirectory { get; set; } = "";
    public bool WordWrap { get; set; } = true;
    public bool StatusBar { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool LineNumbers { get; set; } = true;
    public bool RestoreSession { get; set; } = true;
    public bool AutoSaveAllOnClose { get; set; } = true;
    public bool OpenInNewWindow { get; set; }
    public bool RecentFiles { get; set; } = true;
    public bool DocumentList { get; set; }
    public string DocumentSort { get; set; } = "newest";
    public double ListWidth { get; set; } = 250;
    public int DateTimeFormat { get; set; } = DateTimeFormats.Default;
    public List<string> Recent { get; set; } = [];
}
public sealed class WindowSession
{
    public List<Document> Documents { get; set; } = [];
    public int ActiveIndex { get; set; }
    public double Width { get; set; } = 1016;
    public double Height { get; set; } = 625;
    public bool Maximized { get; set; }
    public bool DocumentList { get; set; }
    public double ListWidth { get; set; } = 250;
}
public sealed class Session { public int Version { get; set; } = 1; public List<WindowSession> Windows { get; set; } = []; }
public static class DocumentFactory
{
    public static Document CreateDraft(int number) => new()
    {
        UntitledNumber = number,
        CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
        DraftName = DateTime.Now.ToString("yyyy-MM-dd HHmm", System.Globalization.CultureInfo.InvariantCulture) + $" - Prompt {number}"
    };

    public static Document Create(Settings settings, string? excludedPath = null)
    {
        settings.NextDocumentNumber = Math.Max(1, settings.NextDocumentNumber);
        if (string.IsNullOrWhiteSpace(settings.AutoSaveDirectory)) return CreateDraft(settings.NextDocumentNumber++);
        string directory = System.IO.Path.GetFullPath(settings.AutoSaveDirectory);
        Directory.CreateDirectory(directory);
        while (true)
        {
            var doc = CreateDraft(settings.NextDocumentNumber++);
            doc.AutoSave = true;
            string path = System.IO.Path.Combine(directory, doc.Name + ".html");
            if (string.Equals(path, excludedPath, StringComparison.OrdinalIgnoreCase)) continue;
            // Reserve the filename atomically: resetting numbering can never overwrite an existing file.
            try { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
            catch (IOException) when (File.Exists(path)) { continue; }
            doc.Path = path; doc.Fingerprint = TextFiles.Hash([]); return doc;
        }
    }
}
public sealed class Store(string directory)
{
    public string DirectoryPath { get; } = directory;
    public T Read<T>(string name) where T : new()
    {
        foreach (string suffix in new[] { "", ".bak" })
        { try { using var input = File.OpenRead(System.IO.Path.Combine(DirectoryPath, name + suffix)); return JsonSerializer.Deserialize<T>(input) ?? new T(); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { } }
        return new T();
    }
    public void Write<T>(string name, T value) { Directory.CreateDirectory(DirectoryPath); TextFiles.AtomicWrite(System.IO.Path.Combine(DirectoryPath, name), JsonSerializer.SerializeToUtf8Bytes(value), true); }
}
