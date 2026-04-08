using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace ClickyWindows.Services;

/// <summary>
/// Text-to-speech client that tries ElevenLabs first (via Cloudflare Worker proxy),
/// and falls back to Windows modern natural voices if ElevenLabs fails.
/// The WinRT SpeechSynthesizer provides much more natural-sounding voices
/// than the old System.Speech SAPI engine.
/// </summary>
public class ElevenLabsTtsClient : IDisposable
{
    private readonly string proxyUrl;
    private readonly HttpClient httpClient;
    private WaveOutEvent? waveOut;
    private Mp3FileReader? mp3Reader;
    private RawSourceWaveStream? rawStream;
    private bool isWindowsTtsPlaying;

    public bool IsPlaying => waveOut?.PlaybackState == PlaybackState.Playing || isWindowsTtsPlaying;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackStopped;

    public ElevenLabsTtsClient(string proxyUrl)
    {
        this.proxyUrl = proxyUrl;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task SpeakTextAsync(string text, CancellationToken cancellationToken = default)
    {
        StopPlayback();

        try
        {
            await SpeakWithElevenLabsAsync(text, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] ElevenLabs TTS failed ({ex.Message}), falling back to Windows natural TTS");
            await SpeakWithWindowsNaturalTtsAsync(text);
        }
    }

    private async Task SpeakWithElevenLabsAsync(string text, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            text,
            model_id = "eleven_flash_v2_5",
            voice_settings = new
            {
                stability = 0.5,
                similarity_boost = 0.75
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyUrl}/tts")
        {
            Content = jsonContent
        };
        request.Headers.Accept.ParseAdd("audio/mpeg");

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        var audioStream = new MemoryStream(audioBytes);
        mp3Reader = new Mp3FileReader(audioStream);
        waveOut = new WaveOutEvent();
        waveOut.Init(mp3Reader);

        waveOut.PlaybackStopped += (_, _) =>
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        };

        waveOut.Play();
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Uses the modern WinRT SpeechSynthesizer which has access to the natural
    /// Microsoft OneCore/Neural voices (much better than old SAPI robot voices).
    /// </summary>
    private async Task SpeakWithWindowsNaturalTtsAsync(string text)
    {
        var synthesizer = new SpeechSynthesizer();

        // Try to find a natural-sounding voice (prefer female voices like "Zira" or any neural voice)
        var allVoices = SpeechSynthesizer.AllVoices;
        Debug.WriteLine($"[Clicky] Available voices: {allVoices.Count}");
        foreach (var voice in allVoices)
        {
            Debug.WriteLine($"[Clicky]   Voice: {voice.DisplayName} ({voice.Language}, {voice.Gender})");
        }

        // Prefer natural/neural English voices, then David (clearer enunciation than Zira)
        var preferredVoice = allVoices
            .Where(v => v.Language.StartsWith("en"))
            .OrderByDescending(v => v.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.DisplayName.Contains("David", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (preferredVoice != null)
        {
            synthesizer.Voice = preferredVoice;
            Debug.WriteLine($"[Clicky] Selected voice: {preferredVoice.DisplayName}");
        }

        // Use SSML for natural pacing: default rate, with slight pauses between sentences
        var escapedText = System.Security.SecurityElement.Escape(text);
        var ssml = $"""
            <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
                <prosody rate='-5%' pitch='+0%'>{escapedText}</prosody>
            </speak>
            """;
        var speechStream = await synthesizer.SynthesizeSsmlToStreamAsync(ssml);

        // Convert the WinRT stream to a .NET stream for NAudio playback
        var netStream = speechStream.AsStreamForRead();

        // WinRT SpeechSynthesizer outputs WAV audio — play it via NAudio
        var waveReader = new WaveFileReader(netStream);
        waveOut = new WaveOutEvent();
        waveOut.Init(waveReader);

        isWindowsTtsPlaying = true;

        waveOut.PlaybackStopped += (_, _) =>
        {
            isWindowsTtsPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        };

        waveOut.Play();
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopPlayback()
    {
        isWindowsTtsPlaying = false;

        if (waveOut != null)
        {
            waveOut.Stop();
            waveOut.Dispose();
            waveOut = null;
        }

        if (mp3Reader != null)
        {
            mp3Reader.Dispose();
            mp3Reader = null;
        }

        if (rawStream != null)
        {
            rawStream.Dispose();
            rawStream = null;
        }
    }

    public void Dispose()
    {
        StopPlayback();
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
