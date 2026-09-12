using System.Windows;
using System.Windows.Controls;

namespace SinAIPrompt;

// Keep visited editors in the visual tree. Switching visibility preserves each
// native browser window, ribbon layout, selection and undo stack without reparenting.
public sealed class EditorSurface : Grid
{
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
        if (args.NewValue is FrameworkElement next)
        {
            if (!host.Children.Contains(next)) host.Children.Add(next);
            next.Visibility = Visibility.Visible;
        }
        if (args.OldValue is FrameworkElement previous)
        {
            // Null transfers the current editor to the full-content annotation host.
            if (args.NewValue == null || previous is not EditorView) host.Children.Remove(previous);
            else previous.Visibility = Visibility.Hidden;
        }
    }
    internal void Release(EditorView view) => Children.Remove(view);
}
