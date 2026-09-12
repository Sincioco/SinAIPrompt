using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinAIPrompt;

// Owns only capture choices and their previews. The dialog owns the capture
// operation, countdown, cursor/region options, and restoration of its owner.
internal sealed class CaptureGallery : ScrollViewer, IDisposable
{
    readonly List<Button> tiles = [];
    readonly List<WindowThumbnail> thumbnails = [];
    readonly Dictionary<Int32Rect, BitmapSource?> monitorPreviews = [];
    internal ScreenCapture.Target? Selected { get; private set; }
    internal event Action<ScreenCapture.Target>? SelectionChanged;

    internal CaptureGallery()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Padding = new Thickness(8, 16, 8, 16);
        SizeChanged += (_, _) => { if (Content is Grid grid) FitColumns(grid); };
    }

    internal async Task RefreshAsync(nint exclude)
    {
        var monitors = ScreenCapture.Monitors();
        var windows = ScreenCapture.Windows(exclude);
        var previews = await Task.Run(() => monitors.Select(m =>
        {
            if (monitorPreviews.TryGetValue(m.Bounds, out var existing)) return existing;
            try
            {
                var pixels = ScreenCapture.Capture(m.Bounds);
                var small = new TransformedBitmap(pixels, new ScaleTransform(Math.Min(1, 420d / pixels.PixelWidth), Math.Min(1, 420d / pixels.PixelWidth)));
                small.Freeze(); return (BitmapSource?)small;
            }
            catch { return null; }
        }).ToArray());
        for (int i = 0; i < monitors.Count; i++) monitorPreviews[monitors[i].Bounds] = previews[i];
        var previous = Selected;
        Dispose(); tiles.Clear();
        var columns = new Grid { MinWidth = monitors.Count * 300 };
        FitColumns(columns);
        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            var section = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
            Grid.SetColumn(section, i); columns.Children.Add(section);
            section.Children.Add(new TextBlock { Text = $"Monitor {i + 1}", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            var screen = new Border { BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(6),
                Background = Brushes.White, Child = new Image { Source = previews[i], Height = 165, Stretch = Stretch.Uniform }, Margin = new Thickness(4) };
            var monitorTile = Tile(monitor, screen); monitorTile.MaxWidth = 430; monitorTile.HorizontalAlignment = HorizontalAlignment.Left;
            section.Children.Add(monitorTile);
            section.Children.Add(new TextBlock { Text = "Applications on this monitor", FontSize = 13, Margin = new Thickness(0, 20, 0, 12) });
            var applications = new WrapPanel(); section.Children.Add(applications);
            foreach (var window in windows.Where(w => ScreenCapture.MonitorBounds(w) == monitor.Bounds))
            {
                var thumbnail = new WindowThumbnail(window.Window); thumbnails.Add(thumbnail);
                var tile = Tile(window, thumbnail); tile.Width = 206; tile.Margin = new Thickness(0, 0, 12, 12); applications.Children.Add(tile);
            }
            if (applications.Children.Count == 0) applications.Children.Add(new TextBlock { Text = "No open application windows", Foreground = Brushes.SlateGray });
        }
        Content = columns;
        var selected = tiles.FirstOrDefault(b => b.Tag is ScreenCapture.Target t && t.Window == previous?.Window && (t.Window != 0 || t.Bounds == previous.Bounds)) ?? tiles.FirstOrDefault();
        if (selected != null) Select(selected);
    }

    void FitColumns(Grid grid) => grid.Width = Math.Max(grid.MinWidth, ActualWidth - Padding.Left - Padding.Right - SystemParameters.VerticalScrollBarWidth);

    Button Tile(ScreenCapture.Target target, UIElement preview)
    {
        var content = new StackPanel(); content.Children.Add(preview);
        content.Children.Add(new TextBlock { Text = target.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 9, 5, 4), FontSize = 12 });
        var tile = new Button { Tag = target, Content = content, ToolTip = target.Name, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, BorderThickness = new Thickness(2), Padding = new Thickness(6) };
        System.Windows.Automation.AutomationProperties.SetName(tile, target.Name);
        tile.Click += (_, _) => Select(tile); tiles.Add(tile); return tile;
    }
    void Select(Button button)
    {
        Selected = (ScreenCapture.Target)button.Tag;
        foreach (var tile in tiles) tile.BorderBrush = tile == button ? new SolidColorBrush(Color.FromRgb(8, 106, 183)) : Brushes.Transparent;
        SelectionChanged?.Invoke(Selected);
    }
    public void Dispose() { foreach (var thumbnail in thumbnails) thumbnail.Dispose(); thumbnails.Clear(); }
}
