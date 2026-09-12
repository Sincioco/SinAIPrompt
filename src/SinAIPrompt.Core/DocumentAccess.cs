namespace SinAIPrompt.Core;

// The Windows file attribute is authoritative. The model caches it for UI binding.
public static class DocumentAccess
{
    public static bool IsReadOnly(string? path) => path != null && File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
    public static void Refresh(Document document)
    {
        bool value = IsReadOnly(document.Path);
        if (value != document.IsReadOnly) { document.IsReadOnly = value; document.Notify(); }
    }
    public static void Set(Document document, bool locked)
    {
        if (document.Path == null) throw new IOException("Save the document before locking it.");
        var attributes = File.GetAttributes(document.Path);
        File.SetAttributes(document.Path, locked ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
        document.IsReadOnly = locked; document.Notify();
    }
}
