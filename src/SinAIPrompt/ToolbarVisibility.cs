using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class ToolbarVisibility
{
    internal static void Toggle(MouseButtonEventArgs e, Settings preferences, Action apply)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        for (var node = e.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is MenuItem or ButtonBase) return;
        preferences.ShowToolbar = !preferences.ShowToolbar;
        e.Handled = true;
        apply();
    }
}
