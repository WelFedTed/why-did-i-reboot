using System.Windows;
using WhyDidIReboot.Core;

namespace WhyDidIReboot;

public partial class CustomRangeWindow : Window
{
    public CustomRangeWindow(TimeRange? current)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        var today = DateTime.Today;
        FromPicker.SelectedDate = current?.From?.Date ?? today.AddDays(-30);
        ToPicker.SelectedDate = current?.To?.Date ?? today;
        FromPicker.DisplayDateEnd = today;
        ToPicker.DisplayDateEnd = today;
    }

    /// <summary>The chosen window (whole days), set when the dialog closes with Apply.</summary>
    public TimeRange? Result { get; private set; }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (FromPicker.SelectedDate is not DateTime from || ToPicker.SelectedDate is not DateTime to)
        {
            MessageBox.Show(this, "Pick both a start and an end date.", "Custom range", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = TimeRange.Days(from, to);
        DialogResult = true;
    }

    private void ThisMonth_Click(object sender, RoutedEventArgs e)
    {
        var t = DateTime.Today;
        FromPicker.SelectedDate = new DateTime(t.Year, t.Month, 1);
        ToPicker.SelectedDate = t;
    }

    private void LastMonth_Click(object sender, RoutedEventArgs e)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        FromPicker.SelectedDate = first;
        ToPicker.SelectedDate = first.AddMonths(1).AddDays(-1);
    }

    private void ThisYear_Click(object sender, RoutedEventArgs e)
    {
        FromPicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        ToPicker.SelectedDate = DateTime.Today;
    }
}
