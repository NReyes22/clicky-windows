using System.Windows;
using System.Windows.Threading;

namespace ClickyWindows.Helpers;

/// <summary>
/// Animates the blue cursor along a quadratic bezier arc from its current position
/// to a target element location. Port of animateBezierFlightArc from OverlayWindow.swift.
///
/// - Control point offset upward by distance * 0.4
/// - Progress via smoothstep: 3t^2 - 2t^3
/// - Scale pulses: sin(progress * PI) * 0.3 + 1.0
/// - Duration: clamp(distance / 800, 0.6, 1.4) seconds
/// </summary>
public class BezierFlightAnimator
{
    public event Action<Point, double, double>? OnPositionUpdate; // position, scale, rotation
    public event Action? OnComplete;

    private readonly Point startPoint;
    private readonly Point endPoint;
    private readonly Point controlPoint;
    private readonly Dispatcher dispatcher;
    private readonly double durationSeconds;

    private DispatcherTimer? animationTimer;
    private DateTime animationStartTime;

    public BezierFlightAnimator(Point start, Point end, Dispatcher dispatcher)
    {
        startPoint = start;
        endPoint = end;
        this.dispatcher = dispatcher;

        // Compute distance and duration
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        durationSeconds = Math.Clamp(distance / 800.0, 0.6, 1.4);

        // Control point: midpoint elevated upward by distance * 0.4
        var midX = (start.X + end.X) / 2.0;
        var midY = (start.Y + end.Y) / 2.0;
        controlPoint = new Point(midX, midY - distance * 0.4);
    }

    public void Start()
    {
        animationStartTime = DateTime.Now;

        animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        animationTimer.Tick += AnimationTimer_Tick;
        animationTimer.Start();
    }

    public void Cancel()
    {
        animationTimer?.Stop();
        animationTimer = null;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.Now - animationStartTime).TotalSeconds;
        var linearProgress = Math.Clamp(elapsed / durationSeconds, 0.0, 1.0);

        // Smoothstep easing: 3t^2 - 2t^3
        var t = linearProgress;
        var smoothProgress = t * t * (3.0 - 2.0 * t);

        // Quadratic bezier position
        var oneMinusT = 1.0 - smoothProgress;
        var position = new Point(
            oneMinusT * oneMinusT * startPoint.X +
                2 * oneMinusT * smoothProgress * controlPoint.X +
                smoothProgress * smoothProgress * endPoint.X,
            oneMinusT * oneMinusT * startPoint.Y +
                2 * oneMinusT * smoothProgress * controlPoint.Y +
                smoothProgress * smoothProgress * endPoint.Y);

        // Scale pulse: peaks at midpoint of arc
        var scale = Math.Sin(smoothProgress * Math.PI) * 0.3 + 1.0;

        // Rotation follows the tangent to the curve
        var tangentX = 2 * (1 - smoothProgress) * (controlPoint.X - startPoint.X) +
                       2 * smoothProgress * (endPoint.X - controlPoint.X);
        var tangentY = 2 * (1 - smoothProgress) * (controlPoint.Y - startPoint.Y) +
                       2 * smoothProgress * (endPoint.Y - controlPoint.Y);
        var rotation = Math.Atan2(tangentY, tangentX) * 180.0 / Math.PI;

        OnPositionUpdate?.Invoke(position, scale, rotation);

        if (linearProgress >= 1.0)
        {
            animationTimer?.Stop();
            animationTimer = null;
            OnComplete?.Invoke();
        }
    }
}
