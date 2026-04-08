using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using ClickyWindows.Helpers;
using ClickyWindows.Models;
using ClickyWindows.Services;

namespace ClickyWindows.ViewModels;

/// <summary>
/// Event args for requesting a pointing animation to a screen location.
/// </summary>
public class PointingAnimationEventArgs : EventArgs
{
    public required Point ScreenLocation { get; init; }
    public string? ElementLabel { get; init; }
}

/// <summary>
/// Central state machine orchestrating the full push-to-talk pipeline:
/// hotkey press → mic capture → AssemblyAI transcription → screenshot →
/// Claude API (SSE streaming) → ElevenLabs TTS → element pointing animation.
///
/// Port of CompanionManager.swift — the heart of the application.
/// </summary>
public partial class CompanionManagerViewModel : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PointingAnimationEventArgs>? PointingAnimationRequested;

    // ── Services ────────────────────────────────────────────────────
    private readonly GlobalHotkeyService globalHotkeyService;
    private readonly AudioCaptureService audioCaptureService;
    private readonly AssemblyAiTranscriptionService assemblyAiService;
    private readonly ScreenCaptureService screenCaptureService;
    private readonly ClaudeApiClient claudeApiClient;
    private readonly ElevenLabsTtsClient elevenLabsTtsClient;

    // ── State ───────────────────────────────────────────────────────
    private CompanionVoiceState voiceState = CompanionVoiceState.Idle;
    private double currentAudioPowerLevel;
    private string? currentResponseText;
    private string selectedModel;
    private CancellationTokenSource? currentResponseCancellation;

    // ── Conversation History ────────────────────────────────────────
    private readonly List<ConversationEntry> conversationHistory = new();
    private const int MaxConversationHistoryEntries = 10;

    // ── Last captured screenshots (for coordinate mapping) ─────────
    private List<CompanionScreenCapture>? lastCapturedScreenshots;

    public CompanionVoiceState VoiceState
    {
        get => voiceState;
        private set { voiceState = value; OnPropertyChanged(nameof(VoiceState)); }
    }

    public double CurrentAudioPowerLevel
    {
        get => currentAudioPowerLevel;
        private set { currentAudioPowerLevel = value; OnPropertyChanged(nameof(CurrentAudioPowerLevel)); }
    }

    public string? CurrentResponseText
    {
        get => currentResponseText;
        private set { currentResponseText = value; OnPropertyChanged(nameof(CurrentResponseText)); }
    }

    public string SelectedModel
    {
        get => selectedModel;
        set
        {
            selectedModel = value;
            claudeApiClient.SetModel(value);
            OnPropertyChanged(nameof(SelectedModel));
        }
    }

    public CompanionManagerViewModel(string workerBaseUrl, string defaultModel)
    {
        selectedModel = defaultModel;

        globalHotkeyService = new GlobalHotkeyService();
        audioCaptureService = new AudioCaptureService();
        assemblyAiService = new AssemblyAiTranscriptionService(workerBaseUrl);
        screenCaptureService = new ScreenCaptureService();
        claudeApiClient = new ClaudeApiClient(workerBaseUrl, defaultModel);
        elevenLabsTtsClient = new ElevenLabsTtsClient(workerBaseUrl);

        // Wire up events
        globalHotkeyService.ShortcutTransitionChanged += OnShortcutTransitionChanged;
        audioCaptureService.AudioPowerLevelChanged += (_, level) =>
        {
            CurrentAudioPowerLevel = level;
        };
        audioCaptureService.AudioDataAvailable += async (_, data) =>
        {
            try
            {
                await assemblyAiService.SendAudioDataAsync(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Clicky] Audio send error: {ex.Message}");
            }
        };
        assemblyAiService.OnTranscriptUpdate += (_, transcript) =>
        {
            Debug.WriteLine($"[Clicky] Transcript update: {transcript}");
        };
        assemblyAiService.OnError += (_, error) =>
        {
            Debug.WriteLine($"[Clicky] AssemblyAI error: {error}");
        };
        assemblyAiService.OnFinalTranscriptReady += (_, transcript) =>
        {
            Debug.WriteLine($"[Clicky] Final transcript: {transcript}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                _ = SendTranscriptToClaudeWithScreenshot(transcript);
            });
        };
        elevenLabsTtsClient.PlaybackStarted += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                VoiceState = CompanionVoiceState.Responding;
            });
        };
        elevenLabsTtsClient.PlaybackStopped += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                VoiceState = CompanionVoiceState.Idle;
                CurrentResponseText = null;
            });
        };
    }

    public void Start()
    {
        globalHotkeyService.Start();
    }

    public void Stop()
    {
        globalHotkeyService.Stop();
        audioCaptureService.StopCapture();
        elevenLabsTtsClient.StopPlayback();
    }

    // ── Hotkey Transition Handling ──────────────────────────────────

    private void OnShortcutTransitionChanged(object? sender, ShortcutTransition transition)
    {
        Debug.WriteLine($"[Clicky] Shortcut transition: {transition}");
        Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                switch (transition)
                {
                    case ShortcutTransition.Pressed:
                        await HandleShortcutPressed();
                        break;

                    case ShortcutTransition.Released:
                        await HandleShortcutReleased();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Clicky] Shortcut handler error: {ex}");
            }
        });
    }

    private async Task HandleShortcutPressed()
    {
        Debug.WriteLine("[Clicky] HandleShortcutPressed - starting mic and AssemblyAI");

        // Cancel any in-progress response
        currentResponseCancellation?.Cancel();
        elevenLabsTtsClient.StopPlayback();

        VoiceState = CompanionVoiceState.Listening;
        CurrentResponseText = null;

        // Start mic capture and AssemblyAI streaming session
        audioCaptureService.StartCapture();
        Debug.WriteLine("[Clicky] Mic capture started");

        var keyterms = new[] { "Claude", "Clicky", "Windows", "Visual Studio", "browser" };
        await assemblyAiService.StartSessionAsync(keyterms);
        Debug.WriteLine("[Clicky] AssemblyAI session started");
    }

    private async Task HandleShortcutReleased()
    {
        Debug.WriteLine("[Clicky] HandleShortcutReleased - stopping mic, requesting transcript");

        VoiceState = CompanionVoiceState.Processing;

        // Stop mic capture
        audioCaptureService.StopCapture();

        // Request final transcript (AssemblyAI will fire OnFinalTranscriptReady)
        await assemblyAiService.RequestFinalTranscriptAsync();
        Debug.WriteLine("[Clicky] Final transcript requested");
    }

    // ── AI Response Pipeline ───────────────────────────────────────

    private async Task SendTranscriptToClaudeWithScreenshot(string transcript)
    {
        Debug.WriteLine($"[Clicky] SendTranscriptToClaudeWithScreenshot: \"{transcript}\"");

        if (string.IsNullOrWhiteSpace(transcript))
        {
            Debug.WriteLine("[Clicky] Empty transcript, returning to idle");
            VoiceState = CompanionVoiceState.Idle;
            return;
        }

        VoiceState = CompanionVoiceState.Processing;
        currentResponseCancellation = new CancellationTokenSource();

        try
        {
            // Capture all screens
            lastCapturedScreenshots = screenCaptureService.CaptureAllScreens();
            Debug.WriteLine($"[Clicky] Captured {lastCapturedScreenshots.Count} screen(s)");

            // Send to Claude with streaming
            var responseText = await claudeApiClient.AnalyzeImageStreamingAsync(
                lastCapturedScreenshots,
                transcript,
                conversationHistory,
                onTextChunk: (accumulatedText) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentResponseText = accumulatedText;
                    });
                },
                cancellationToken: currentResponseCancellation.Token);

            // Parse pointing coordinates from the response
            var pointingResult = ParsePointingCoordinates(responseText);

            // Strip the [POINT:...] tag from spoken text for TTS
            var spokenText = pointingResult.SpokenText.Trim();

            // Add to conversation history
            conversationHistory.Add(new ConversationEntry(transcript, spokenText));
            if (conversationHistory.Count > MaxConversationHistoryEntries)
            {
                conversationHistory.RemoveAt(0);
            }

            // Play TTS audio (state transitions to Responding when playback starts)
            if (!string.IsNullOrEmpty(spokenText))
            {
                await elevenLabsTtsClient.SpeakTextAsync(
                    spokenText,
                    currentResponseCancellation.Token);
            }

            // If Claude pointed at an element, trigger the flight animation
            if (pointingResult.Coordinate.HasValue && lastCapturedScreenshots != null)
            {
                var targetScreenIndex = (pointingResult.ScreenNumber ?? 1) - 1;
                targetScreenIndex = Math.Clamp(targetScreenIndex, 0, lastCapturedScreenshots.Count - 1);

                var targetCapture = lastCapturedScreenshots[targetScreenIndex];
                var screenLocation = CoordinateMapper.MapScreenshotToDisplay(
                    pointingResult.Coordinate.Value,
                    targetCapture);

                PointingAnimationRequested?.Invoke(this, new PointingAnimationEventArgs
                {
                    ScreenLocation = screenLocation,
                    ElementLabel = pointingResult.ElementLabel
                });
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Clicky] Response cancelled by user");
            VoiceState = CompanionVoiceState.Idle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] Pipeline error: {ex}");
            VoiceState = CompanionVoiceState.Idle;
            CurrentResponseText = null;
        }
    }

    // ── POINT Tag Parsing ──────────────────────────────────────────

    /// <summary>
    /// Parses [POINT:x,y:label] or [POINT:x,y:label:screenN] tags from Claude's response.
    /// Port of the regex parsing from CompanionManager.swift line 784.
    /// </summary>
    public static PointingParseResult ParsePointingCoordinates(string responseText)
    {
        // Match [POINT:none] — no pointing
        if (responseText.Contains("[POINT:none]"))
        {
            var spokenText = responseText.Replace("[POINT:none]", "").Trim();
            return new PointingParseResult(spokenText, null, null, null);
        }

        // Match [POINT:x,y:label] or [POINT:x,y:label:screenN]
        var pattern = @"\[POINT:(\d+),(\d+):([^:\]]+)(?::screen(\d+))?\]";
        var match = Regex.Match(responseText, pattern);

        if (match.Success)
        {
            var x = int.Parse(match.Groups[1].Value);
            var y = int.Parse(match.Groups[2].Value);
            var label = match.Groups[3].Value;
            int? screenNumber = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value)
                : null;

            var spokenText = responseText[..match.Index].Trim();
            return new PointingParseResult(
                spokenText,
                new Point(x, y),
                label,
                screenNumber);
        }

        // No POINT tag found
        return new PointingParseResult(responseText, null, null, null);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        Stop();
        globalHotkeyService.Dispose();
        audioCaptureService.Dispose();
        assemblyAiService.Dispose();
        claudeApiClient.Dispose();
        elevenLabsTtsClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
