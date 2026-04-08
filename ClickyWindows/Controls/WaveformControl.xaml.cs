using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClickyWindows.Controls;

public partial class WaveformControl : UserControl
{
    private readonly Rectangle[] bars;
    private readonly double[] barProfile = [0.4, 0.7, 1.0, 0.7, 0.4];
    private readonly DispatcherTimer animationTimer;
    private double phase;

    public double AudioPowerLevel { get; set; }

    public WaveformControl()
    {
        InitializeComponent();

        bars = [Bar0, Bar1, Bar2, Bar3, Bar4];

        // ~36fps animation for idle pulse
        animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(28)
        };
        animationTimer.Tick += AnimationTimer_Tick;

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                animationTimer.Start();
            else
                animationTimer.Stop();
        };
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        phase += 0.07;

        // Ease the power level with a quadratic curve for smoother response
        var easedPowerLevel = AudioPowerLevel * AudioPowerLevel;

        // Idle pulse: gentle sine wave when not speaking
        var idlePulse = (Math.Sin(phase) + 1.0) / 2.0 * 1.5;

        for (int i = 0; i < bars.Length; i++)
        {
            // Each bar has a slightly offset phase for a wave effect
            var barIdlePulse = (Math.Sin(phase + i * 0.5) + 1.0) / 2.0 * 1.5;
            var barHeight = 3.0 + easedPowerLevel * 10.0 * barProfile[i] + barIdlePulse;
            barHeight = Math.Max(3.0, Math.Min(barHeight, 20.0));

            bars[i].Height = barHeight;

            // Center the bars vertically
            Canvas.SetTop(bars[i], 12.0 - barHeight / 2.0);
        }
    }
}
