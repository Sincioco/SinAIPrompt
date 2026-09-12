using System.Windows;
using System.Windows.Controls;

namespace SinAIPrompt;

// Keep visited browser surfaces painted. Switching their native clip avoids the
// hide/show transition in WebView2 while preserving each document's editing state.
public sealed class EditorSurface : Grid
{
    EditorView? ribbonFallback;
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(nameof(Content),
        typeof(FrameworkElement), typeof(EditorSurface), new PropertyMetadata(null, ContentChanged));
    public FrameworkElement? Content
    {
        get => (FrameworkElement?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
    static void ContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (EditorSurface)sender;
        var fallback = host.ribbonFallback ?? args.OldValue as EditorView;
        host.ClearFallback();
        if (args.OldValue is FrameworkElement previous)
        {
            // Null transfers the current editor to the full-content annotation host.
            if (args.NewValue == null || previous is not EditorView)
            {
                if (previous is EditorView transfer) transfer.Browser.SetPresentation(EditorPresentation.Complete);
                host.Children.Remove(previous);
            }
            else
            {
                previous.IsHitTestVisible = false;
                previous.IsEnabled = false;
                SetZIndex(previous, 0);
                var editor = (EditorView)previous;
                editor.Browser.SetPresentation(EditorPresentation.Hidden);
                editor.StopMedia();
            }
        }
        if (args.NewValue is FrameworkElement next)
        {
            if (!host.Children.Contains(next)) host.Children.Add(next);
            next.Visibility = Visibility.Visible;
            next.IsHitTestVisible = true;
            next.IsEnabled = true;
            SetZIndex(next, 1);
            if (next is EditorView editor)
            {
                if (!editor.FirstPaint.IsCompleted && fallback != editor && fallback?.FirstPaint.IsCompletedSuccessfully == true &&
                    fallback.IsVisual && host.Children.Contains(fallback))
                {
                    // On a first visit only, keep the previous ribbon until the new
                    // document has applied its formatting and painted a complete frame.
                    host.ribbonFallback = fallback;
                    fallback.Browser.SetPresentation(EditorPresentation.Ribbon);
                    editor.Browser.SetPresentation(EditorPresentation.Document);
                    host.FinishFirstPaint(editor);
                }
                else editor.Browser.SetPresentation(EditorPresentation.Complete);
            }
        }
    }
    async void FinishFirstPaint(EditorView editor)
    {
        try { await editor.FirstPaint; } catch (OperationCanceledException) { return; }
        if (Content != editor) return; // Rapid clicks must not resurrect an old selection.
        ClearFallback();
        editor.Browser.SetPresentation(EditorPresentation.Complete);
    }
    void ClearFallback()
    {
        ribbonFallback?.Browser.SetPresentation(EditorPresentation.Hidden);
        ribbonFallback = null;
    }
    internal void Release(EditorView view)
    {
        if (ribbonFallback == view) ribbonFallback = null;
        Children.Remove(view);
    }
}
