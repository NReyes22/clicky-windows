using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ClickyWindows.Controls;

public partial class SpinnerControl : UserControl
{
    public SpinnerControl()
    {
        InitializeComponent();

        // Continuous 360-degree rotation in 0.8 seconds (matching macOS)
        var rotationAnimation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.8),
            RepeatBehavior = RepeatBehavior.Forever
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                SpinnerRotation.BeginAnimation(
                    System.Windows.Media.RotateTransform.AngleProperty,
                    rotationAnimation);
            }
            else
            {
                SpinnerRotation.BeginAnimation(
                    System.Windows.Media.RotateTransform.AngleProperty,
                    null);
            }
        };
    }
}
