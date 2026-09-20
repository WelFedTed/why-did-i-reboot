using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WhyDidIReboot.Core;

namespace WhyDidIReboot;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestCustomRange = current =>
        {
            var dialog = new CustomRangeWindow(current) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        };
        _vm.Confirm = text => MessageBox.Show(this, text, "Why Did I Reboot", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        _vm.Notify = text => MessageBox.Show(this, text, "Why Did I Reboot", MessageBoxButton.OK, MessageBoxImage.Information);

        // The caption bar can only be recoloured once the native window exists.
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) =>
        {
            await _vm.RefreshAsync();
            if (AppSettings.Current.CheckUpdatesOnStartup)
                await _vm.CheckForUpdatesAsync(silent: true);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { _ = _vm.RefreshAsync(); e.Handled = true; }
            if (e.Key == Key.F9) { BrowseOfflineLogs(); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && _vm.HasSearch) { _vm.SearchText = ""; e.Handled = true; }
        };
    }

    /// <summary>Lets the user pick another Windows installation (drive root, Windows folder or winevt\Logs) and loads its logs.</summary>
    public async void BrowseOfflineLogs()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a drive, a Windows folder, or a winevt\\Logs folder from another Windows installation",
            Multiselect = false,
        };
        var last = AppSettings.Current.LastOfflineFolder;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last)) dialog.InitialDirectory = last;

        if (dialog.ShowDialog(this) != true) return;
        await OpenPathAsync(dialog.FolderName);
    }

    /// <summary>Lets the user pick a CSV report this app exported and shows its entries.</summary>
    public async void BrowseCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a CSV report exported by Why Did I Reboot",
            Filter = "CSV report (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) != true) return;
        await OpenPathAsync(dialog.FileName);
    }

    /// <summary>Opens a dropped or chosen path: a .csv report, or a folder/.evtx of another installation.</summary>
    private async Task OpenPathAsync(string path)
    {
        try
        {
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                if (!await _vm.LoadCsvAsync(path))
                    MessageBox.Show(this, $"Could not open:\n{path}", "CSV not found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!await _vm.LoadOfflineAsync(path))
            {
                MessageBox.Show(this,
                    $"No System.evtx was found under:\n{path}\n\n" +
                    "Pick the drive root (for example D:\\), its Windows folder, or Windows\\System32\\winevt\\Logs.",
                    "Event logs not found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            await OpenPathAsync(paths[0]);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is null) return;
        var subject = _vm.IsOffline ? "offline" : Environment.MachineName;
        var dialog = new SaveFileDialog
        {
            Title = "Export reboot history",
            FileName = $"why-did-i-reboot-{subject}-{DateTime.Now:yyyyMMdd-HHmm}",
            Filter = "HTML report (*.html)|*.html|Text report (*.txt)|*.txt|CSV report, re-openable in this app (*.csv)|*.csv",
            DefaultExt = ".html",
        };
        if (dialog.ShowDialog(this) != true) return;

        var entries = _vm.VisibleEntries.ToList();
        var name = dialog.FileName;
        var content = name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? TextExporter.ToCsv(entries)
            : name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? TextExporter.ToText(_vm.LastResult, entries, _vm.RangeLabel)
            : TextExporter.ToHtml(_vm.LastResult, entries, _vm.RangeLabel);
        try
        {
            File.WriteAllText(name, content);
            if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_vm) { Owner = this };
        dialog.ShowDialog();
        if (dialog.BrowseRequested) BrowseOfflineLogs();
        else if (dialog.CsvRequested) BrowseCsv();
    }
}
