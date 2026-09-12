using System.Windows;
using System.Windows.Controls;

namespace SinAIPrompt;

// Native options are collected before the existing cancellable capture workflow.
internal sealed record RegionCaptureOptions(int Delay, bool IncludeCursor, string Storage)
{
    internal static RegionCaptureOptions? Choose(Window owner)
    {
        var dialog = Dialogs.Create(owner, "Region Capture", 430);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var separate = new RadioButton { Content = "Copied in document’s folder", IsChecked = true, Margin = new Thickness(0, 0, 0, 12) };
        var embedded = new RadioButton { Content = "Embedded into HTML", Margin = new Thickness(0, 0, 0, 20) };
        var cursor = new CheckBox { Content = "Include cursor", Margin = new Thickness(0, 0, 0, 16) };
        var delay = new ComboBox { ItemsSource = new[] { 0, 3, 5, 10 }, SelectedItem = 3, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(separate); panel.Children.Add(embedded); panel.Children.Add(cursor);
        panel.Children.Add(new TextBlock { Text = "Delay (seconds)", Margin = new Thickness(0, 0, 0, 6) }); panel.Children.Add(delay);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(Dialogs.Button("Capture", () => dialog.DialogResult = true, true));
        buttons.Children.Add(Dialogs.Button("Cancel", () => dialog.Close(), cancel: true));
        panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? new((int)delay.SelectedItem, cursor.IsChecked == true, separate.IsChecked == true ? "separate" : "inline") : null;
    }
}
