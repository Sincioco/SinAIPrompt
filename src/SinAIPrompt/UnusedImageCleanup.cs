using System.IO;
using System.Windows;
using System.Windows.Controls;
using SinAIPrompt.Core;

namespace SinAIPrompt;

// Owns one reviewable cleanup run; file/reference algorithms stay in Core.
internal static class UnusedImageCleanup
{
    internal static void Show(Window owner)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Scan Documents For Unused Images", InitialDirectory = App.Current.Preferences.ExplorerDirectory };
        if (picker.ShowDialog(owner) != true) return;
        var dialog = Dialogs.Create(owner, "Unused Images", 760);
        var panel = new DockPanel { Margin = new Thickness(20) };
        var status = new TextBlock { Text = "Scanning documents and their image folders…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var progress = new ProgressBar { Height = 5, Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(status, Dock.Top); panel.Children.Add(status); DockPanel.SetDock(progress, Dock.Top); panel.Children.Add(progress);
        var list = new ListBox { Height = 330, SelectionMode = SelectionMode.Extended };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        bool running = false;
        var recycle = new Button { Content = "Recycle Selected Images", IsEnabled = false, Padding = new Thickness(12, 6, 12, 6) };
        footer.Children.Add(recycle); footer.Children.Add(Dialogs.Button("Close", () => dialog.Close(), cancel: true));
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(list); dialog.Content = panel;
        dialog.Closing += (_, e) => e.Cancel = running;
        async Task<IReadOnlyList<string>> Scan()
        {
            var snapshots = new List<(string Path, string Html)>();
            foreach (var window in Application.Current.Windows.OfType<MainWindow>()) snapshots.AddRange(await window.CurrentHtmlSnapshots());
            var gauge = new Progress<int>(value => { progress.IsIndeterminate = false; progress.Value = value; });
            return await Task.Run(() => UnusedImages.Scan(picker.FolderName, snapshots, value => ((IProgress<int>)gauge).Report(value)));
        }
        dialog.Loaded += async (_, _) =>
        {
            running = true;
            try
            {
                var files = await Scan();
                list.ItemsSource = files; list.SelectAll(); recycle.IsEnabled = files.Count > 0;
                status.Text = $"{files.Count:N0} unused images. Review the list and select the files to move to the Recycle Bin. Open unsaved document edits are included.";
            }
            catch (Exception ex) { status.Text = "Scan could not finish: " + ex.Message; }
            finally { running = false; progress.IsIndeterminate = false; progress.Value = 100; }
        };
        recycle.Click += async (_, _) =>
        {
            var selected = list.SelectedItems.Cast<string>().ToArray();
            if (selected.Length == 0 || MessageBox.Show(dialog, $"Move {selected.Length:N0} selected images to the Recycle Bin?", "Recycle Unused Images", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            running = true; recycle.IsEnabled = false;
            try
            {
                status.Text = "Rechecking references before recycling…";
                var unused = (await Scan()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var safe = selected.Where(unused.Contains).ToArray();
                await Task.Run(() => { foreach (string path in safe) if (File.Exists(path)) ExplorerFileOperations.Recycle(path, false); });
                list.ItemsSource = (await Scan());
                status.Text = $"Recycled {safe.Length:N0} images. {selected.Length - safe.Length:N0} skipped because their references or files changed.";
            }
            catch (Exception ex) { status.Text = "Cleanup stopped: " + ex.Message; }
            finally { running = false; recycle.IsEnabled = list.Items.Count > 0; progress.Value = 100; }
        };
        dialog.ShowDialog();
    }
}
