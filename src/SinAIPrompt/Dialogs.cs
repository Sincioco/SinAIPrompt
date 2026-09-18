using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SinAIPrompt;

public enum SaveChoice { Cancel, Save, Discard }
public static class Dialogs
{
    internal static async Task WithProgress(Window owner, string title, Func<Task> action)
    {
        var dialog = Create(owner, title);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
        dialog.Content = panel;
        bool running = true, wasEnabled = owner.IsEnabled;
        dialog.Closing += (_, e) => e.Cancel = running;
        owner.IsEnabled = false;
        try { dialog.Show(); await action(); }
        finally { running = false; dialog.Close(); owner.IsEnabled = wasEnabled; owner.Activate(); }
    }
    internal static Window Create(Window owner, string title, double width = 450)
    {
        var w = new Window { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, ThemeMode = owner.ThemeMode };
        w.SetResourceReference(Window.BackgroundProperty, "ShellBrush"); w.SetResourceReference(Window.ForegroundProperty, "TextBrush"); return w;
    }
    internal static Button Button(string label, Action action, bool primary = false, bool cancel = false)
    {
        var b = new Button { Content = label, MinWidth = 90, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), IsDefault = primary, IsCancel = cancel };
        b.Click += (_, _) => action(); return b;
    }
    public static SaveChoice SaveChanges(Window owner, string name)
    {
        SaveChoice choice = SaveChoice.Cancel; var w = Create(owner, "Sin - AI Prompt");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Do you want to save changes to {name}?", TextWrapping = TextWrapping.Wrap, FontSize = 19, Margin = new Thickness(0, 0, 0, 24) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Save", () => { choice = SaveChoice.Save; w.Close(); }, true));
        buttons.Children.Add(Button("Don't Save", () => { choice = SaveChoice.Discard; w.Close(); }));
        buttons.Children.Add(Button("Cancel", () => w.Close(), cancel: true)); panel.Children.Add(buttons); w.Content = panel; w.ShowDialog(); return choice;
    }
    public static int? LineNumber(Window owner, int current, int maximum)
    {
        int? result = null; var w = Create(owner, "Go To Line", 360);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Line Number (1–{maximum:N0})", Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = current.ToString() }; panel.Children.Add(input);
        var error = new TextBlock { Foreground = Brushes.IndianRed, Margin = new Thickness(0, 6, 0, 10) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Go To", () => { if (int.TryParse(input.Text, out int line) && line >= 1 && line <= maximum) { result = line; w.Close(); } else error.Text = "Enter a valid line number."; }, true));
        buttons.Children.Add(Button("Cancel", () => w.Close(), cancel: true)); panel.Children.Add(buttons);
        w.Content = panel; w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; w.ShowDialog(); return result;
    }
    public static void RenameFile(Window owner, string currentName, Func<string, Task> rename, bool keepExtension = true)
    {
        var w = Create(owner, "Rename File - Sin - AI Prompt", 500);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = keepExtension ? "File Name (Without Extension)" : "Name", Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = keepExtension ? Path.GetFileNameWithoutExtension(currentName) : currentName };
        System.Windows.Automation.AutomationProperties.SetName(input, "File Name"); panel.Children.Add(input);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bool renaming = false;
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(progress);
        buttons.Children.Add(Button("Rename", async () =>
        {
            renaming = true; buttons.IsEnabled = false; input.IsEnabled = false; progress.Visibility = Visibility.Visible; error.Text = "";
            try { await rename(input.Text + (keepExtension ? Path.GetExtension(currentName) : "")); renaming = false; w.Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { error.Text = ex.Message; }
            finally { renaming = false; buttons.IsEnabled = true; input.IsEnabled = true; progress.Visibility = Visibility.Collapsed; if (error.Text.Length > 0) input.Focus(); }
        }, true));
        buttons.Children.Add(Button("Cancel", () => w.Close(), cancel: true)); panel.Children.Add(buttons);
        w.Content = panel;
        w.Closing += (_, e) => e.Cancel = renaming;
        w.Loaded += (_, _) =>
        {
            input.Focus();
            int end = input.Text.Length;
            var prefix = System.Text.RegularExpressions.Regex.Match(currentName, @"^\d{4}-\d{2}-\d{2}(?:[ -]\d{4})?\s*-\s*");
            int start = prefix.Success && prefix.Length < end ? prefix.Length : 0;
            input.Select(start, end - start);
        };
        w.ShowDialog();
    }
    internal static bool? MarkdownImagePaths(Window owner)
    {
        bool? absolute = null;
        var window = Create(owner, "Export as Markdown - Image References", 530);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Choose the image paths in the exported Markdown.", TextWrapping = TextWrapping.Wrap, FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "Relative paths keep the Markdown and its image folder portable. Absolute paths point to the exported images on this computer.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Relative Paths", () => { absolute = false; window.Close(); }, primary: true));
        buttons.Children.Add(Button("Absolute Paths", () => { absolute = true; window.Close(); }));
        buttons.Children.Add(Button("Cancel", () => window.Close(), cancel: true));
        panel.Children.Add(buttons); window.Content = panel; window.ShowDialog();
        return absolute;
    }
    public static bool DeleteFile(Window owner, string path, bool dirty, bool folder = false)
    {
        bool confirmed = false; var w = Create(owner, folder ? "Delete Folder - Sin - AI Prompt" : "Delete File - Sin - AI Prompt", 520);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = folder ? "Move this folder to the Recycle Bin?" : "Move this file to the Recycle Bin?", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(new TextBlock { Text = folder ? "The folder and its contents will be recycled only if no existing file in it is still referenced by the document." : "This closes every tab for this file." + (dirty ? " Unsaved changes in those tabs will be discarded." : ""), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Delete", () => { confirmed = true; w.Close(); }));
        buttons.Children.Add(Button("Cancel", () => w.Close(), primary: true, cancel: true)); panel.Children.Add(buttons);
        w.Content = panel; w.ShowDialog(); return confirmed;
    }

    internal static bool DeleteFiles(Window owner, IReadOnlyList<string> paths, IReadOnlyList<string> drafts, bool dirty)
    {
        bool confirmed = false;
        var dialog = Create(owner, "Delete Selected Files - Sin - AI Prompt", 580);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Delete the selected items?", FontSize = 20 });
        string details = string.Join("\n", paths.Select(path => "Recycle: " + path).Concat(drafts.Select(name => "Close unsaved: " + name)));
        panel.Children.Add(new ScrollViewer { MaxHeight = 260, Margin = new Thickness(0, 12, 0, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap } });
        panel.Children.Add(new TextBlock { Text = (paths.Count > 0 ? "Saved files will move to the Recycle Bin and their open tabs will close. " : "") +
            (dirty ? "Unsaved changes to those files will be discarded. " : "") +
            (drafts.Count > 0 ? "Unsaved documents will close without saving; their contents cannot be recovered from the Recycle Bin." : ""),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Delete", () => { confirmed = true; dialog.Close(); }));
        buttons.Children.Add(Button("Cancel", () => dialog.Close(), primary: true, cancel: true));
        panel.Children.Add(buttons); dialog.Content = panel; dialog.ShowDialog(); return confirmed;
    }
}
