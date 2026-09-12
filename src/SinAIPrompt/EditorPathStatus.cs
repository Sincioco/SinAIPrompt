using System.IO;
using System.Windows;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// One editor owns its hovered/selected image source and displayed local path.
public sealed class EditorPathStatus : DependencyObject
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditorPathStatus));
    public string Text { get => (string)GetValue(TextProperty); private set => SetValue(TextProperty, value); }
    readonly Document document;
    string folder, source = "";

    internal EditorPathStatus(Document document, string folder)
    {
        this.document = document; this.folder = folder;
        document.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }
    internal void SetFolder(string value) { folder = value; Refresh(); }
    internal void SetImage(string value) { source = value; Refresh(); }
    void Refresh()
    {
        string value = document.Path ?? "Unsaved document";
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) value = "Embedded image (inline)";
        else if (source.Length > 0)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                value = uri.Host == "sin-document.local" ? Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')))) : uri.IsFile ? uri.LocalPath : source;
            else value = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(source.Split('?', '#')[0])));
        }
        if (Text != value) Text = value;
    }
}
