using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClickyWindows.Helpers;
using ClickyWindows.Models;
using ClickyWindows.ViewModels;

namespace ClickyWindows.Views;

public partial class OverlayWindow : Window
{
    private readonly CompanionManagerViewModel companionManager;
    private readonly System.Windows.Forms.Screen targetScreen;
    private readonly DispatcherTimer cursorFollowTimer;

    // Spring animation state for smooth cursor following
    private double currentCursorX;
    private double currentCursorY;
    private double velocityX;
    private double velocityY;

    // Spring physics constants (matching macOS: response=0.2, dampingFraction=0.6)
    private const double SpringStiffness = 300.0;
    private const double SpringDamping = 22.0;

    // Whether the cursor is currently on this overlay's screen
    private bool isCursorOnThisScreen;

    // When true, the cursor stays at the pointed element instead of following the mouse
    private bool isHoldingAtPointedElement;

    // DPI scale factor — physical pixels to WPF DIPs
    private double dpiScaleX = 1.0;
    private double dpiScaleY = 1.0;

    public OverlayWindow(CompanionManagerViewModel companionManager, System.Windows.Forms.Screen screen)
    {
        InitializeComponent();

        this.companionManager = companionManager;
        this.targetScreen = screen;

        // 60fps cursor follow timer
        cursorFollowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        cursorFollowTimer.Tick += CursorFollowTimer_Tick;

        // Listen for voice state changes
        companionManager.PropertyChanged += CompanionManager_PropertyChanged;

        // Listen for pointing animation requests
        companionManager.PointingAnimationRequested += OnPointingAnimationRequested;

        Loaded += OverlayWindow_Loaded;
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Get the DPI scale for this window so we can convert physical pixels to WPF DIPs
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        Debug.WriteLine($"[Clicky] Overlay DPI scale: {dpiScaleX}x{dpiScaleY}");
        Debug.WriteLine($"[Clicky] Screen bounds (physical): {targetScreen.Bounds}");

        // Position and size the window using DIPs (physical pixels / DPI scale)
        Left = targetScreen.Bounds.Left / dpiScaleX;
        Top = targetScreen.Bounds.Top / dpiScaleY;
        Width = targetScreen.Bounds.Width / dpiScaleX;
        Height = targetScreen.Bounds.Height / dpiScaleY;

        Debug.WriteLine($"[Clicky] Overlay window (DIPs): Left={Left}, Top={Top}, Width={Width}, Height={Height}");

        MakeClickThrough();
        cursorFollowTimer.Start();

        // Initialize cursor position to current mouse location
        var cursorPos = System.Windows.Forms.Cursor.Position;
        currentCursorX = (cursorPos.X - targetScreen.Bounds.Left) / dpiScaleX;
        currentCursorY = (cursorPos.Y - targetScreen.Bounds.Top) / dpiScaleY;
    }

    /// <summary>
    /// Sets WS_EX_TRANSPARENT so all mouse events pass through this window.
    /// </summary>
    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
    }

    private void CursorFollowTimer_Tick(object? sender, EventArgs e)
    {
        var cursorPosition = System.Windows.Forms.Cursor.Position;

        // Check if cursor is on this screen (using physical pixel coordinates)
        isCursorOnThisScreen = targetScreen.Bounds.Contains(cursorPosition);

        // Only show controls on the screen where the cursor currently is
        if (!isCursorOnThisScreen)
        {
            BlueCursor.Visibility = Visibility.Collapsed;
            Waveform.Visibility = Visibility.Collapsed;
            Spinner.Visibility = Visibility.Collapsed;
            SpeechBubble.Visibility = Visibility.Collapsed;
            return;
        }

        // When holding at a pointed element, keep the cursor pinned there
        if (isHoldingAtPointedElement)
        {
            UpdateControlVisibility();
            UpdateControlPositions(currentCursorX, currentCursorY);
            return;
        }

        // Convert physical screen coordinates to window-local DIPs
        var targetX = (cursorPosition.X - targetScreen.Bounds.Left) / dpiScaleX;
        var targetY = (cursorPosition.Y - targetScreen.Bounds.Top) / dpiScaleY;

        // Spring physics for smooth cursor following
        var deltaTime = 1.0 / 60.0;
        var forceX = -SpringStiffness * (currentCursorX - targetX) - SpringDamping * velocityX;
        var forceY = -SpringStiffness * (currentCursorY - targetY) - SpringDamping * velocityY;

        velocityX += forceX * deltaTime;
        velocityY += forceY * deltaTime;
        currentCursorX += velocityX * deltaTime;
        currentCursorY += velocityY * deltaTime;

        // Update control positions based on voice state
        UpdateControlVisibility();
        UpdateControlPositions(currentCursorX, currentCursorY);
    }

    private void UpdateControlVisibility()
    {
        var state = companionManager.VoiceState;

        // Blue cursor is ALWAYS visible when on this screen (idle, responding, or any state)
        BlueCursor.Visibility = isCursorOnThisScreen ? Visibility.Visible : Visibility.Collapsed;

        Waveform.Visibility = state == CompanionVoiceState.Listening
            ? Visibility.Visible : Visibility.Collapsed;

        Spinner.Visibility = state == CompanionVoiceState.Processing
            ? Visibility.Visible : Visibility.Collapsed;

        // Speech bubble is visible during responding (with text streaming)
        SpeechBubble.Visibility = state == CompanionVoiceState.Responding && companionManager.CurrentResponseText != null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateControlPositions(double x, double y)
    {
        // Position the active control at the cursor location
        Canvas.SetLeft(BlueCursor, x - 8);
        Canvas.SetTop(BlueCursor, y - 8);

        Canvas.SetLeft(Waveform, x - 20);
        Canvas.SetTop(Waveform, y - 12);

        Canvas.SetLeft(Spinner, x - 7);
        Canvas.SetTop(Spinner, y - 7);

        // Speech bubble offset to the right and slightly above the cursor
        Canvas.SetLeft(SpeechBubble, x + 18);
        Canvas.SetTop(SpeechBubble, y - 30);
    }

    private void CompanionManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(CompanionManagerViewModel.VoiceState):
                    UpdateControlVisibility();
                    break;

                case nameof(CompanionManagerViewModel.CurrentAudioPowerLevel):
                    if (isCursorOnThisScreen)
                    {
                        Waveform.AudioPowerLevel = companionManager.CurrentAudioPowerLevel;
                    }
                    break;

                case nameof(CompanionManagerViewModel.CurrentResponseText):
                    if (isCursorOnThisScreen && companionManager.CurrentResponseText != null)
                    {
                        SpeechBubble.SetText(companionManager.CurrentResponseText);
                    }
                    break;
            }
        });
    }

    private void OnPointingAnimationRequested(object? sender, PointingAnimationEventArgs args)
    {
        // Check if the target point is on this screen
        var targetScreenPoint = new System.Drawing.Point((int)args.ScreenLocation.X, (int)args.ScreenLocation.Y);
        if (!targetScreen.Bounds.Contains(targetScreenPoint)) return;

        Dispatcher.Invoke(() =>
        {
            // Convert physical pixel target to window-local DIPs
            var localTargetX = (args.ScreenLocation.X - targetScreen.Bounds.Left) / dpiScaleX;
            var localTargetY = (args.ScreenLocation.Y - targetScreen.Bounds.Top) / dpiScaleY;

            var animator = new BezierFlightAnimator(
                new Point(currentCursorX, currentCursorY),
                new Point(localTargetX, localTargetY),
                Dispatcher);

            animator.OnPositionUpdate += (position, scale, rotation) =>
            {
                currentCursorX = position.X;
                currentCursorY = position.Y;
                velocityX = 0;
                velocityY = 0;

                BlueCursor.RenderTransform = new ScaleTransform(scale, scale);
                UpdateControlPositions(position.X, position.Y);
            };

            animator.OnComplete += () =>
            {
                // Pin the cursor at the pointed element
                isHoldingAtPointedElement = true;

                if (args.ElementLabel != null)
                {
                    SpeechBubble.SetText(args.ElementLabel);
                    SpeechBubble.Visibility = Visibility.Visible;
                }

                // Hold at the pointed element for 8 seconds before returning to mouse
                var holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                holdTimer.Tick += (_, _) =>
                {
                    holdTimer.Stop();
                    isHoldingAtPointedElement = false;
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    BlueCursor.RenderTransform = new ScaleTransform(1, 1);
                };
                holdTimer.Start();
            };

            animator.Start();
        });
    }
}
