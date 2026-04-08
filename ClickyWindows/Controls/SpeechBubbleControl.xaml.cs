using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClickyWindows.Controls;

public partial class SpeechBubbleControl : UserControl
{
    private string fullText = "";
    private int currentCharacterIndex;
    private DispatcherTimer? characterStreamTimer;

    public SpeechBubbleControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the text to display, streaming it character-by-character
    /// at 30-60ms per character with a pop-in entrance animation.
    /// </summary>
    public void SetText(string text)
    {
        // If the new text starts with what we already have, just continue streaming
        if (text.StartsWith(fullText) && currentCharacterIndex > 0)
        {
            fullText = text;
            EnsureStreamingTimerRunning();
            return;
        }

        // New text — restart streaming from the beginning
        fullText = text;
        currentCharacterIndex = 0;
        BubbleText.Text = "";

        PlayPopInAnimation();
        EnsureStreamingTimerRunning();
    }

    private void EnsureStreamingTimerRunning()
    {
        if (characterStreamTimer != null) return;

        characterStreamTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        characterStreamTimer.Tick += (_, _) =>
        {
            if (currentCharacterIndex < fullText.Length)
            {
                currentCharacterIndex++;
                BubbleText.Text = fullText[..currentCharacterIndex];
            }
            else
            {
                // All characters displayed — stop the timer
                characterStreamTimer?.Stop();
                characterStreamTimer = null;
            }
        };
        characterStreamTimer.Start();
    }

    private void PlayPopInAnimation()
    {
        // Scale from 0.5 to 1.0 with a spring-like bounce
        var scaleXAnimation = new DoubleAnimation
        {
            From = 0.5,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 1,
                Springiness = 8
            }
        };
        var scaleYAnimation = new DoubleAnimation
        {
            From = 0.5,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new ElasticEase
            {
                EasingMode = EasingMode.EaseOut,
                Oscillations = 1,
                Springiness = 8
            }
        };

        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnimation);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnimation);
    }
}
