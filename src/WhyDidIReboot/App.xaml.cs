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

            // --source <folder|System.evtx>: analyse another Windows installation's logs instead of this PC's.
            var location = LogLocation.Local;
            var sourceIndex = Array.FindIndex(args, a => a.Equals("--source", StringComparison.OrdinalIgnoreCase));
            if (sourceIndex >= 0 && sourceIndex + 1 < args.Length)
            {
                var resolved = LogLocation.Resolve(args[sourceIndex + 1]);
                if (resolved is null)
                {
                    File.WriteAllText(path, $"ERROR: no System.evtx found under '{args[sourceIndex + 1]}'.");
                    Environment.ExitCode = 2;
                    Shutdown();
                    return;
                }
                location = resolved;
            }

            try
            {
                var result = RebootAnalyzer.Analyze(days, location);
                var entries = result.Entries.Where(x => includeSleep || x.Category != RebootCategory.Sleep);
                var label = days is int dd ? $"last {dd} days" : "everything";
                var text = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? TextExporter.ToCsv(entries)
                    : path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
                        ? TextExporter.ToHtml(result, entries, label)
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

        ThemeManager.Initialize();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
