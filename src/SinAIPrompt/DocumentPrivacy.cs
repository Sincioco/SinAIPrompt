using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Visibility only. Documents remain in their session, autosave and recovery owners.
internal sealed class DocumentPrivacy : IDisposable
{
    readonly ObservableCollection<Document> documents;
    readonly Settings settings;
    readonly ListBox tabs;
    readonly ListCollectionView view;

    internal DocumentPrivacy(ObservableCollection<Document> documents, ListBox tabs, Settings settings)
    {
        this.documents = documents; this.tabs = tabs; this.settings = settings;
        view = new ListCollectionView(documents) { Filter = item => IsVisible((Document)item) };
        tabs.IsSynchronizedWithCurrentItem = false;
        tabs.ItemsSource = view;
    }
    internal bool IsVisible(Document document) => settings.ShowPrivateDocuments || !document.IsPrivate;
    internal Document? VisibleDocument(Document? preferred = null) => preferred != null && IsVisible(preferred) ? preferred : documents.FirstOrDefault(IsVisible);
    internal Document? Adjacent(Document? current, int direction)
    {
        var visible = documents.Where(IsVisible).ToList();
        return visible.Count == 0 ? null : visible[(visible.IndexOf(current!) + direction + visible.Count) % visible.Count];
    }
    internal bool IsVisible(PromptEntry entry) => IsPathVisible(entry.ParentHtml ?? entry.Path);
    internal bool IsPathVisible(string path)
    {
        if (settings.ShowPrivateDocuments) return true;
        foreach (var document in documents.Where(doc => doc.IsPrivate && doc.Path != null))
        {
            if (string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase)) return false;
            string assets = Path.Combine(Path.GetDirectoryName(document.Path!)!, Path.GetFileNameWithoutExtension(document.Path!));
            if (string.Equals(assets, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
    internal void Refresh()
    {
        view.Refresh();
        tabs.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
    }
    public void Dispose() => view.Filter = null;
}
