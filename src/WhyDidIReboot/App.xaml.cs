using System.IO;
using System.Windows;
using WhyDidIReboot.Core;

namespace WhyDidIReboot;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Command line: WhyDidIReboot.exe --export <file.txt|file.csv> [--days N]
        var args = e.Args;
        var exportIndex = Array.FindIndex(args, a => a.Equals("--export", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0 && exportIndex + 1 < args.Length)
        {
            int? days = 365;
            var daysIndex = Array.FindIndex(args, a => a.Equals("--days", StringComparison.OrdinalIgnoreCase));
            if (daysIndex >= 0 && daysIndex + 1 < args.Length)
                days = args[daysIndex + 1].Equals("all", StringComparison.OrdinalIgnoreCase) ? null
                     : int.TryParse(args[daysIndex + 1], out var d) ? d : 365;

            var includeSleep = args.Any(a => a.Equals("--sleep", StringComparison.OrdinalIgnoreCase));
            var path = args[exportIndex + 1];
            try
            {
                var result = RebootAnalyzer.Analyze(days);
                var entries = result.Entries.Where(x => includeSleep || x.Category != RebootCategory.Sleep);
                var label = days is int dd ? $"last {dd} days" : "everything";
                var text = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? TextExporter.ToCsv(entries)
                    : TextExporter.ToText(result, entries, label);
                File.WriteAllText(path, text);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(path, "ERROR: " + ex);
                Environment.ExitCode = 1;
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Why Did I Reboot — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
