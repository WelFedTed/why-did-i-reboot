using System.Windows;

namespace WhyDidIReboot;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        PreviewKeyDown += (_, e) =>
        {
            // F9 opens Settings from the main window; pressing it again here closes them.
            if (e.Key == System.Windows.Input.Key.F9) { Close(); e.Handled = true; }
            // F5 here presses the update button (Check for updates, or Update now once one is known).
            if (e.Key == System.Windows.Input.Key.F5 && vm.UpdateActionCommand.CanExecute(null)) { vm.UpdateActionCommand.Execute(null); e.Handled = true; }
        };
    }

    /// <summary>Set when the user asked to open another installation's logs; the owner shows the folder picker after this dialog closes.</summary>
    public bool BrowseRequested { get; private set; }

    /// <summary>Set when the user asked to open a saved CSV report; the owner shows the file picker after this dialog closes.</summary>
    public bool CsvRequested { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        BrowseRequested = true;
        Close();
    }

    private void Csv_Click(object sender, RoutedEventArgs e)
    {
        CsvRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
