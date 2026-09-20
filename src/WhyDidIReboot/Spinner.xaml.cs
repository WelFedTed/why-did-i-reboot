using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace WhyDidIReboot;

/// <summary>The app's busy indicator: a ring that spins while visible and stops when hidden, so it costs nothing when idle.</summary>
public partial class Spinner : UserControl
{
    private readonly DoubleAnimation _spin = new(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever };

    public Spinner()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) Rotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _spin);
            else Rotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        };
        SizeChanged += (_, _) => Ring.StrokeThickness = Math.Max(2, ActualWidth / 9);
    }
}
