using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// A navigation pane owns each ordered selection. Checkboxes are disposable views;
// selection survives filtering, sorting and Explorer row reconstruction.
public sealed class CombineSelection
{
    readonly List<object> items = [];
    internal event Action? Changed;
    internal IReadOnlyList<object> Items => items.ToArray();
    static string Key(object item) => item is Document doc ? doc.Id.ToString() : ((PromptEntry)item).Path.ToUpperInvariant();
    internal int Order(object item) => items.FindIndex(value => Key(value) == Key(item)) + 1;
    internal void Set(object item, bool selected)
    {
        int index = Order(item) - 1;
        if (selected && index < 0) items.Add(item);
        else if (!selected && index >= 0) items.RemoveAt(index);
        Changed?.Invoke();
    }
    internal void Clear() { items.Clear(); Changed?.Invoke(); }
}

public sealed class CombineSelectionBox : CheckBox
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(object), typeof(CombineSelectionBox), new PropertyMetadata(null, RefreshProperty));
    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(nameof(Selection), typeof(CombineSelection), typeof(CombineSelectionBox), new PropertyMetadata(null, ChangeSelection));
    public object? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public CombineSelection? Selection { get => (CombineSelection?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    bool refreshing;
    internal static bool ContainsTarget(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is CombineSelectionBox) return true;
            element = element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    public CombineSelectionBox()
    {
        Margin = new Thickness(0, 0, 6, 0); VerticalAlignment = VerticalAlignment.Center;
        FontSize = 10; ToolTip = "Select for File → Combine. Numbers show selection order.";
        Checked += Pick; Unchecked += Pick;
        Loaded += (_, _) => { if (Selection != null) { Selection.Changed -= Refresh; Selection.Changed += Refresh; } Refresh(); };
        Unloaded += (_, _) => { if (Selection != null) Selection.Changed -= Refresh; };
    }
    static void RefreshProperty(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((CombineSelectionBox)sender).Refresh();
    static void ChangeSelection(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var box = (CombineSelectionBox)sender;
        if (e.OldValue is CombineSelection old) old.Changed -= box.Refresh;
        if (box.IsLoaded && e.NewValue is CombineSelection current) current.Changed += box.Refresh;
        box.Refresh();
    }
    void Refresh()
    {
        bool eligible = Item is Document || Item is PromptEntry { IsHtml: true };
        Visibility = Selection != null && eligible ? Visibility.Visible : Visibility.Collapsed;
        refreshing = true;
        try
        {
            int order = eligible && Selection != null ? Selection.Order(Item!) : 0;
            IsChecked = order > 0; Content = order > 0 ? order.ToString() : "";
            string name = Item is Document doc ? doc.Name : Item is PromptEntry entry ? entry.Name : "";
            AutomationProperties.SetName(this, "Select for Combine: " + name + (order > 0 ? $", order {order}" : ""));
        }
        finally { refreshing = false; }
    }
    void Pick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!refreshing && Item != null) Selection?.Set(Item, IsChecked == true);
    }
}
