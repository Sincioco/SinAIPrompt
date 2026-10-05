using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// A short naming dialog and project choices; the caller owns document moves.
internal static class PromptProjectUi
{
    sealed class ProjectMenuItem : MenuItem
    {
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // Fluent's submenu header omits the shared checkmark column used by
            // ordinary items. Reuse that column so menus without checks stay compact.
            const string checkColumn = "MenuItemCheckBoxIconColumnGroup";
            if (Role != MenuItemRole.SubmenuHeader || Template.FindName("MenuItemContent", this) is not Grid grid ||
                grid.ColumnDefinitions.FirstOrDefault()?.SharedSizeGroup == checkColumn) return;
            grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = checkColumn });
            foreach (UIElement child in grid.Children) Grid.SetColumn(child, Grid.GetColumn(child) + 1);
        }
    }

    internal static MenuItem Menu(Window owner, Settings settings, IReadOnlyList<object> selection, string defaultFolder,
        Func<IReadOnlyList<object>, string, Task> move, Action changed)
    {
        var menu = new ProjectMenuItem { Header = "Move to Project", IsEnabled = selection.Count > 0 };
        var selected = selection.ToArray();
        foreach (string folder in PromptProjects.Known(settings))
        {
            var item = new MenuItem { Header = Path.GetFileName(folder), ToolTip = folder, Tag = folder };
            item.Click += async (_, _) =>
            {
                item.IsEnabled = false;
                try { await move(selected, folder); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Move to Project", MessageBoxButton.OK, MessageBoxImage.Warning); }
                finally { item.IsEnabled = true; }
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New Project…" };
        create.Click += async (_, _) =>
        {
            create.IsEnabled = false;
            try
            {
                string? folder = await CreateAsync(owner, settings, defaultFolder);
                if (folder == null) return;
                changed();
                await move(selected, folder);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Move to Project", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { create.IsEnabled = true; }
        };
        menu.Items.Add(create);
        return menu;
    }

    internal static async Task<string?> CreateAsync(Window owner, Settings settings, string defaultFolder)
    {
        var request = Ask(owner, defaultFolder);
        if (request == null) return null;
        try
        {
            string folder = await Task.Run(() => PromptProjects.Create(request.Value.Parent, request.Value.Name));
            PromptProjects.Remember(settings, folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(owner, ex.Message, "Create Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    static (string Parent, string Name)? Ask(Window owner, string defaultFolder)
    {
        (string Parent, string Name)? result = null;
        var dialog = Dialogs.Create(owner, "Create Project", 560);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Project Name", Margin = new Thickness(0, 0, 0, 6) });
        var name = new TextBox();
        System.Windows.Automation.AutomationProperties.SetName(name, "Project Name");
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "Folder name: Project - <Name>", Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Parent Folder", Margin = new Thickness(0, 0, 0, 6) });
        var parent = new TextBox { Text = string.IsNullOrWhiteSpace(defaultFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : defaultFolder, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(parent, "Project Parent Folder");
        var row = new DockPanel();
        var browse = Dialogs.Button("Browse…", () =>
        {
            var picker = new OpenFolderDialog { Title = "Choose Project Parent Folder", InitialDirectory = parent.Text };
            if (picker.ShowDialog(dialog) == true) parent.Text = picker.FolderName;
        });
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(parent); panel.Children.Add(row);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Dialogs.Button("Create", () =>
        {
            try
            {
                string value = name.Text.Trim();
                if (value.Length == 0) throw new ArgumentException("Enter a project name.");
                TextFiles.ValidateFileName(PromptProjects.Prefix + value);
                if (!Path.IsPathFullyQualified(parent.Text.Trim())) throw new ArgumentException("Enter a full parent folder path.");
                result = (Path.GetFullPath(parent.Text.Trim()), value);
                dialog.Close();
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { error.Text = ex.Message; }
        }, primary: true));
        buttons.Children.Add(Dialogs.Button("Cancel", () => dialog.Close(), cancel: true));
        panel.Children.Add(buttons); dialog.Content = panel;
        dialog.Loaded += (_, _) => name.Focus();
        dialog.ShowDialog();
        return result;
    }
}
