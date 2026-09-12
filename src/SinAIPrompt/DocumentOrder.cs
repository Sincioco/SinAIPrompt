using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Orders existing models only; never creates editors or reads document contents.
internal sealed class DocumentOrder : IDisposable
{
    readonly ObservableCollection<Document> documents;
    readonly Settings preferences;
    readonly Dispatcher dispatcher;
    readonly Action changed;
    readonly Action restoreSelection;
    readonly Dictionary<Document, (bool Pin, DateTime Created, DateTime Modified)> observed = [];
    bool queued, sorting, disposed;
    internal bool IsSorting => sorting;

    internal DocumentOrder(ObservableCollection<Document> documents, Settings preferences, Dispatcher dispatcher, Action changed, Action? restoreSelection = null)
    {
        this.documents = documents; this.preferences = preferences; this.dispatcher = dispatcher; this.changed = changed;
        this.restoreSelection = restoreSelection ?? (() => { });
        documents.CollectionChanged += CollectionChanged;
        foreach (var doc in documents) Watch(doc);
        Queue();
    }
    void Watch(Document doc)
    {
        if (observed.ContainsKey(doc)) return;
        observed[doc] = (doc.Pinned, doc.CreatedUtc, doc.ModifiedUtc); doc.PropertyChanged += DocumentChanged;
    }
    void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sorting) return;
        foreach (var doc in observed.Keys.Where(d => !documents.Contains(d)).ToArray()) { doc.PropertyChanged -= DocumentChanged; observed.Remove(doc); }
        foreach (var doc in documents) Watch(doc);
        Queue();
    }
    void DocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => DocumentChanged(sender, e)); return; }
        if (disposed || sender is not Document doc || !observed.ContainsKey(doc)) return;
        var stamp = (doc.Pinned, doc.CreatedUtc, doc.ModifiedUtc);
        if (observed.GetValueOrDefault(doc) == stamp) return;
        observed[doc] = stamp; Queue();
    }
    void Queue()
    {
        if (queued || disposed) return; queued = true;
        dispatcher.BeginInvoke(async () =>
        {
            queued = false; if (disposed) return;
            var missing = documents.Where(d => d.CreatedUtc == default).Select(d => (Doc: d, d.Path)).ToArray();
            var dates = await Task.Run(() => missing.Select(item =>
            {
                try { var info = item.Path == null ? null : new FileInfo(item.Path); return (item.Doc, Created: info?.Exists == true ? info.CreationTimeUtc : DateTime.UtcNow, Modified: info?.Exists == true ? info.LastWriteTimeUtc : DateTime.UtcNow); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (item.Doc, Created: DateTime.UtcNow, Modified: DateTime.UtcNow); }
            }).ToArray());
            if (disposed) return;
            foreach (var item in dates) if (documents.Contains(item.Doc) && item.Doc.CreatedUtc == default)
            { item.Doc.CreatedUtc = item.Created; item.Doc.ModifiedUtc = item.Modified; observed[item.Doc] = (item.Doc.Pinned, item.Created, item.Modified); }
            Apply();
        }, DispatcherPriority.Background);
    }
    internal void SetMode(string mode) { preferences.DocumentSort = mode; Apply(); changed(); }
    internal void TogglePin(Document doc) { doc.Pinned = !doc.Pinned; doc.Notify(); Apply(); changed(); }
    internal void Apply()
    {
        var sorted = documents.OrderByDescending(d => d.Pinned).ThenByDescending(d => preferences.DocumentSort == "created" ? d.CreatedUtc : preferences.DocumentSort == "manual" ? DateTime.MinValue : d.ModifiedUtc).ToArray();
        sorting = true;
        try { for (int i = 0; i < sorted.Length; i++) { int current = documents.IndexOf(sorted[i]); if (current != i) documents.Move(current, i); } }
        finally { sorting = false; }
        restoreSelection(); changed();
    }
    public void Dispose()
    {
        disposed = true; documents.CollectionChanged -= CollectionChanged;
        foreach (var doc in observed.Keys) doc.PropertyChanged -= DocumentChanged; observed.Clear();
    }
}
