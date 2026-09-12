using System.Windows;

namespace SinAIPrompt;

public partial class MainWindow
{
    internal bool IsAnnotating => AnnotationHost.Content != null;

    internal void SetAnnotationMode(EditorView view, bool open)
    {
        if (open)
        {
            if (IsAnnotating || CurrentView != view) return;
            EditorHost.Content = null;
            AnnotationHost.Content = view;
            AnnotationHost.Visibility = Visibility.Visible;
            Shell.IsEnabled = false;
        }
        else
        {
            if (AnnotationHost.Content != view) return;
            AnnotationHost.Content = null;
            AnnotationHost.Visibility = Visibility.Collapsed;
            EditorHost.Content = view;
            Shell.IsEnabled = true;
        }
        UpdateLayout();
        view.FocusEditing();
    }
}
