using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using static SinAIPrompt.Dialogs;

namespace SinAIPrompt;

internal static class SettingsDialog
{
    public static void Show(Window owner)
    {
        var p = App.Current.Preferences;
        var w = Create(owner, "Settings - Sin - AI Prompt", 620);
        w.Height = Math.Min(570, SystemParameters.WorkArea.Height - 60); w.MinHeight = 350;
        w.SizeToContent = SizeToContent.Manual; w.ResizeMode = ResizeMode.CanResize;
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var tabs = new TabControl();
        StackPanel Page(string name)
        {
            var page = new StackPanel { Margin = new Thickness(14) };
            tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return page;
        }
        var panel = Page("Documents");
        void Label(string text, string? description = null)
        {
            panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
            if (description != null) { var label = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(label); }
        }
        Label("Auto-Save Folder", "New documents are created here as yyyy-MM-dd HHmm - Prompt N.html, using the current date and time, and saved as you edit. Leave this empty to save new documents manually.");
        var folderRow = new Grid(); folderRow.ColumnDefinitions.Add(new()); folderRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var folder = new TextBox { Text = p.AutoSaveDirectory, MinWidth = 240, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(folder, "Auto-Save Folder");
        folderRow.Children.Add(folder);
        var browse = Button("Browse…", () => { var dialog = new OpenFolderDialog { Title = "Choose A Folder For New HTML Files" }; if (Directory.Exists(folder.Text)) dialog.InitialDirectory = folder.Text; if (dialog.ShowDialog(w) == true) folder.Text = dialog.FolderName; });
        Grid.SetColumn(browse, 1); folderRow.Children.Add(browse); panel.Children.Add(folderRow);
        var clear = new Button { Content = "Use Manual Saving For New Documents", Style = (Style)Application.Current.FindResource("FlatButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) }; clear.Click += (_, _) => folder.Text = ""; panel.Children.Add(clear);
        Label("Document Numbering", "The sequence continues between launches. Existing files are always skipped when a number is already in use.");
        var sequenceRow = new DockPanel();
        int nextNumber = p.NextDocumentNumber;
        bool resetSequence = false;
        var next = new TextBlock { Text = $"Next Document: Prompt {nextNumber}", VerticalAlignment = VerticalAlignment.Center };
        var reset = Button("Reset To 1", () => { resetSequence = true; next.Text = "Next Document: Prompt 1 (existing files will be skipped)"; });
        DockPanel.SetDock(reset, Dock.Right); sequenceRow.Children.Add(reset); sequenceRow.Children.Add(next); panel.Children.Add(sequenceRow);
        var documentPanel = panel;
        panel = Page("Appearance");
        Label("Theme");
        var themes = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = p.Theme, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200 };
        System.Windows.Automation.AutomationProperties.SetName(themes, "App Theme"); panel.Children.Add(themes);
        Label("Editor");
        var wrap = new CheckBox { Content = "Word Wrap", IsChecked = p.WordWrap, Margin = new Thickness(0, 3, 0, 8) }; panel.Children.Add(wrap);
        var toolbar = new CheckBox { Content = "Show Formatting Toolbar", IsChecked = p.ShowToolbar, Margin = new Thickness(0, 3, 0, 8) }; panel.Children.Add(toolbar);
        panel.Children.Add(new TextBlock { Text = "You can also double-click an empty part of the menu bar to show or hide the toolbar.", TextWrapping = TextWrapping.Wrap });
        panel = documentPanel;
        Label("Saving And Startup");
        var saveAllOnClose = new CheckBox { Content = "Auto-Save All When Sin - AI Prompt Closes", IsChecked = p.AutoSaveAllOnClose, Margin = new Thickness(0, 3, 0, 8), ToolTip = "Saves changed documents that already have a file path. Untitled documents continue to use session recovery." }; panel.Children.Add(saveAllOnClose);
        System.Windows.Automation.AutomationProperties.SetName(saveAllOnClose, "Auto-Save All When Sin - AI Prompt Closes");
        var restore = new CheckBox { Content = "Restore Open Documents When The App Starts", IsChecked = p.RestoreSession, Margin = new Thickness(0, 3, 0, 8) }; panel.Children.Add(restore);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        panel = Page("Storage & Files");
        Label("Application Storage", "Settings, session recovery, and object templates are JSON files. Choose a folder on D: or another persistent drive to keep them through a C: reformat.");
        var storage = new TextBox { Text = App.Current.Store.DirectoryPath, Margin = new Thickness(0, 4, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(storage, "Application Storage Folder");
        panel.Children.Add(storage);
        panel.Children.Add(Button("Choose Storage Folder…", () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Application Settings And Templates Folder", InitialDirectory = App.Current.Store.DirectoryPath };
            if (picker.ShowDialog(w) == true) storage.Text = picker.FolderName;
        }));
        panel.Children.Add(new TextBlock { Text = "On Save, existing JSON is copied to the new folder. Original files remain as a backup. The location is remembered beside the executable. Keep that small data-location.json file when moving the app.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        Label("HTML File Association", "Windows protects default-app choices. Choose an app below, then confirm .html in Windows Settings.");
        var associationRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        associationRow.Children.Add(Button("Use Sin - AI Prompt For .html Files", () =>
        {
            try { FileAssociations.OpenSinAIPromptDefaults(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { error.Text = "Could not open Default apps: " + ex.Message; }
        }));
        panel.Children.Add(associationRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(Button("Save", () =>
        {
            string path = folder.Text.Trim();
            try
            {
                if (path.Length > 0)
                {
                    if (!Path.IsPathFullyQualified(path)) { error.Text = "Enter a full folder path, such as D:\\Notes."; return; }
                    path = Path.GetFullPath(path); Directory.CreateDirectory(path);
                    string probe = Path.Combine(path, ".sin-ai-prompt-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { file.WriteByte(0); }
                }
                p.AutoSaveDirectory = path; if (resetSequence) p.NextDocumentNumber = 1; p.Theme = themes.SelectedItem as string ?? "System"; p.WordWrap = wrap.IsChecked == true; p.ShowToolbar = toolbar.IsChecked == true; p.AutoSaveAllOnClose = saveAllOnClose.IsChecked == true; p.RestoreSession = restore.IsChecked == true;
                if (!Path.IsPathFullyQualified(storage.Text.Trim())) { error.Text = "Enter a full application storage folder path."; return; }
                App.Current.UseStorageFolder(storage.Text.Trim());
                App.Current.MarkChanged();
                if (!App.Current.SaveState()) { error.Text = "Could not save settings: " + App.Current.PersistenceError; return; }
                w.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { error.Text = "This folder cannot be used: " + ex.Message; }
        }, true));
        buttons.Children.Add(Button("Cancel", () => w.Close(), cancel: true));
        var footer = new StackPanel(); footer.Children.Add(error); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        w.Content = root;
        w.ShowDialog();
    }
}
