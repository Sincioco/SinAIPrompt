using System.Windows;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class DocumentLock
{
    internal static async Task ChangeAsync(Window owner, Document document, bool locked, Func<Task<bool>> save)
    {
        DocumentAccess.Refresh(document);
        if (locked == document.IsReadOnly) return;
        if (!locked && MessageBox.Show(owner, "Remove the read-only attribute and allow changes to this file?", "Unlock Document", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (locked && !await save()) return;
        DocumentAccess.Set(document, locked);
        foreach (var other in Application.Current.Windows.OfType<MainWindow>().SelectMany(w => w.Documents).Where(d => d != document && string.Equals(d.Path, document.Path, StringComparison.OrdinalIgnoreCase)))
        { other.IsReadOnly = locked; other.Notify(); }
    }
}
