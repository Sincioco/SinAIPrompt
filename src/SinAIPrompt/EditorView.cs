using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class TextBoxExtensions
{
    public static void ClearUndo(this TextBox editor) { editor.IsUndoEnabled = false; editor.IsUndoEnabled = true; }
}

public sealed partial class EditorView : Grid, IDisposable
{
    public TextBox Editor { get; }
    public LineNumberGutter Gutter { get; }
    public Document Document { get; }
    public EditorView(Document doc)
    {
        Document = doc;
        SetResourceReference(BackgroundProperty, "EditorBrush");
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            IsUndoEnabled = true,
            UndoLimit = 1000,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.0 * 96 / 72 * doc.Zoom / 100,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 12, 12, 12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = doc.Text,
            TextWrapping = App.Current.Preferences.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            SelectionOpacity = 0.45,
            Cursor = Cursors.IBeam,
        };
        Style scrollBarCursorStyle = Application.Current.TryFindResource(typeof(ScrollBar)) is Style baseScrollBarStyle
            ? new Style(typeof(ScrollBar), baseScrollBarStyle)
            : new Style(typeof(ScrollBar));
        scrollBarCursorStyle.Setters.Add(new Setter(CursorProperty, Cursors.Arrow));
        Editor.Resources[typeof(ScrollBar)] = scrollBarCursorStyle;
        // Use the standard native WPF text surface, with no Fluent focus border or formatting features.
        var template = new ControlTemplate(typeof(TextBox));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.Name = "PART_ContentHost";
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, false);
        template.VisualTree = scroll;
        Editor.Template = template;
        Editor.SetResourceReference(Control.BackgroundProperty, "EditorBrush");
        Editor.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        Editor.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        Editor.SetResourceReference(TextBox.SelectionBrushProperty, "AccentBrush");
        AutomationProperties.SetName(Editor, "Text editor");
        Editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        SpellCheck.SetIsEnabled(Editor, false);
        SetColumn(Editor, 1); Children.Add(Editor);
        Gutter = new LineNumberGutter(Editor) { Visibility = App.Current.Preferences.LineNumbers ? Visibility.Visible : Visibility.Collapsed };
        Children.Add(Gutter);
        Editor.TextChanged += (_, _) => { doc.Text = TextFiles.Normalize(Editor.Text); doc.Notify(); Gutter.Rebuild(); App.Current.MarkChanged(); };
        Editor.SelectionChanged += (_, _) => { doc.Caret = TextFiles.ToNormalizedOffset(Editor.Text, Editor.SelectionStart); doc.SelectionLength = TextFiles.ToNormalizedOffset(Editor.Text, Editor.SelectionStart + Editor.SelectionLength) - doc.Caret; };
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => { Gutter.RequestRefresh(); doc.Scroll = Editor.VerticalOffset; doc.HorizontalScroll = Editor.HorizontalOffset; }));
        Editor.SizeChanged += (_, _) => Gutter.RequestRefresh();
        Loaded += (_, _) =>
        {
            int start = TextFiles.FromNormalizedOffset(Editor.Text, doc.Caret);
            int end = TextFiles.FromNormalizedOffset(Editor.Text, doc.Caret + doc.SelectionLength);
            Editor.Select(start, end - start);
            Editor.ScrollToVerticalOffset(doc.Scroll); Editor.ScrollToHorizontalOffset(doc.HorizontalScroll); Gutter.Rebuild();
        };
        Editor.ClearUndo();
        InitializeHtmlEditor();
    }
    public void ApplyPreferences()
    {
        Gutter.Visibility = App.Current.Preferences.LineNumbers ? Visibility.Visible : Visibility.Collapsed;
        Editor.TextWrapping = App.Current.Preferences.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.HorizontalScrollBarVisibility = App.Current.Preferences.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Editor.FontSize = 11.0 * 96 / 72 * Document.Zoom / 100;
        Gutter.Rebuild();
        ApplyHtmlPreferences();
    }
}

public sealed class LineNumberGutter : FrameworkElement
{
    readonly TextBox editor;
    bool refreshQueued;
    bool refreshPending;
    readonly record struct VisibleLine(int Number, double Top);
    readonly record struct DrawingStyle(FontFamily Font, double FontSize, double Dpi, double Width, Brush Foreground, Brush Background, Brush Border);
    List<VisibleLine> visibleLines = [];
    DrawingStyle? drawingStyle;
    DrawingGroup? numberDrawing;
    public List<int> LineStarts { get; private set; } = [0];
    public int LineCount => LineStarts.Count;
    public LineNumberGutter(TextBox editor)
    {
        this.editor = editor; ClipToBounds = true; Focusable = false;
        AutomationProperties.SetName(this, "Line numbers");
        IsHitTestVisible = false;
        Loaded += (_, _) => RequestRefresh();
        SizeChanged += (_, _) => RequestRefresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) RequestRefresh(); };
        editor.LayoutUpdated += (_, _) => { if (refreshPending) QueueRefresh(); };
        Rebuild();
    }
    public void Rebuild()
    {
        var starts = new List<int> { 0 }; string text = editor.Text;
        for (int i = 0; i < text.Length; i++) { if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; starts.Add(i + 1); } else if (text[i] == '\n') starts.Add(i + 1); }
        LineStarts = starts;
        Width = Math.Max(42, Math.Ceiling(editor.FontSize * 0.62 * Math.Max(2, starts.Count.ToString().Length) + 22));
        RequestRefresh();
    }
    public void RequestRefresh()
    {
        refreshPending = true;
        QueueRefresh();
    }
    void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            refreshQueued = false;
            if (!IsVisible || !editor.IsLoaded) { refreshPending = false; return; }
            // Never query text geometry from OnRender: the text view can be invalid during
            // that layout pass. Keep the previous complete drawing until new geometry is ready.
            if (TryRefreshDrawing()) refreshPending = false;
        });
    }
    bool TryRefreshDrawing()
    {
        if (!editor.IsMeasureValid || !editor.IsArrangeValid || ActualWidth <= 0 || ActualHeight <= 0) return false;
        int index = editor.GetCharacterIndexFromPoint(new Point(editor.Padding.Left + 1, 1), true);
        if (index < 0) return false;
        int first = LineStarts.BinarySearch(index); if (first < 0) first = ~first - 1;
        var next = new List<VisibleLine>();
        for (int line = Math.Max(0, first); line < LineStarts.Count; line++)
        {
            Rect rect = editor.GetRectFromCharacterIndex(LineStarts[line], true);
            if (rect.IsEmpty) return false;
            if (rect.Y > ActualHeight) break;
            if (rect.Bottom >= 0) next.Add(new(line + 1, rect.Top));
        }
        var style = new DrawingStyle(editor.FontFamily, editor.FontSize, VisualTreeHelper.GetDpi(this).PixelsPerDip,
            ActualWidth, (Brush)FindResource("MutedBrush"), (Brush)FindResource("EditorBrush"), (Brush)FindResource("LineBrush"));
        // Ordinary character edits usually leave the numbers and their positions unchanged.
        // Reuse the existing drawing instead of invalidating the gutter on every keystroke.
        if (drawingStyle == style && visibleLines.SequenceEqual(next)) return true;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            foreach (var line in next)
            {
                var label = new FormattedText(line.Number.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(style.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), style.FontSize, style.Foreground, style.Dpi);
                dc.DrawText(label, new Point(style.Width - label.Width - 10, line.Top));
            }
        }
        drawing.Freeze();
        numberDrawing = drawing; drawingStyle = style; visibleLines = next;
        InvalidateVisual();
        return true;
    }
    public (int Line, int Column) Position(int index)
    {
        int line = LineStarts.BinarySearch(index); if (line < 0) line = ~line - 1;
        return (line + 1, index - LineStarts[Math.Max(0, line)] + 1);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var background = drawingStyle?.Background ?? (Brush)FindResource("EditorBrush");
        dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawLine(new Pen(drawingStyle?.Border ?? (Brush)FindResource("LineBrush"), 1), new Point(ActualWidth - 0.5, 0), new Point(ActualWidth - 0.5, ActualHeight));
        if (numberDrawing != null) dc.DrawDrawing(numberDrawing);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); RequestRefresh(); }
}
