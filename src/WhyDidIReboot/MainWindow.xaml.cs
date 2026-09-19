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
        Loaded += async (_, _) => await _vm.RefreshAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { _ = _vm.RefreshAsync(); e.Handled = true; }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        };
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LastResult is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export reboot history",
            FileName = $"why-did-i-reboot-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}",
            Filter = "Text report (*.txt)|*.txt|CSV spreadsheet (*.csv)|*.csv",
            DefaultExt = ".txt",
        };
        if (dialog.ShowDialog(this) != true) return;

        var entries = _vm.VisibleEntries.ToList();
        var content = dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? TextExporter.ToCsv(entries)
            : TextExporter.ToText(_vm.LastResult, entries, _vm.RangeLabel);
        try
        {
            File.WriteAllText(dialog.FileName, content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EventViewer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open Event Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
