using System.Windows;

namespace WhyDidIReboot;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }

    /// <summary>Set when the user asked to open another installation's logs; the owner shows the folder picker after this dialog closes.</summary>
    public bool BrowseRequested { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        BrowseRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
