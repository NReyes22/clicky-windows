using NAudio.Wave;

namespace ClickyWindows.Services;

/// <summary>
/// Captures microphone audio as PCM16 mono 16kHz, matching the format required by
/// AssemblyAI streaming. Computes audio power level (RMS) for waveform visualization.
/// Port of the audio capture portion of BuddyDictationManager.swift.
/// </summary>
public class AudioCaptureService : IDisposable
{
    public event EventHandler<byte[]>? AudioDataAvailable;
    public event EventHandler<double>? AudioPowerLevelChanged;

    private WaveInEvent? waveIn;
    private double currentSmoothedPowerLevel;

    /// <summary>
    /// Starts capturing audio from the default microphone.
    /// Format: PCM16, 16kHz, mono — matching AssemblyAI's expected input.
    /// </summary>
    public void StartCapture()
    {
        StopCapture();

        waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 64  // ~1024 bytes per buffer at 16kHz/16bit/mono
        };

        waveIn.DataAvailable += WaveIn_DataAvailable;
        waveIn.StartRecording();
    }

    public void StopCapture()
    {
        if (waveIn != null)
        {
            waveIn.StopRecording();
            waveIn.DataAvailable -= WaveIn_DataAvailable;
            waveIn.Dispose();
            waveIn = null;
        }
        currentSmoothedPowerLevel = 0;
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        // Forward raw PCM16 audio data to listeners (AssemblyAI)
        var audioData = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, audioData, e.BytesRecorded);
        AudioDataAvailable?.Invoke(this, audioData);

        // Compute audio power level for waveform visualization
        // Port of BuddyDictationManager.updateAudioPowerLevel()
        var rawPowerLevel = ComputeRmsPowerLevel(e.Buffer, e.BytesRecorded);

        // Boost and clamp: RMS * 10.2, clamped to [0, 1]
        var boostedLevel = Math.Min(Math.Max(rawPowerLevel * 10.2, 0.0), 1.0);

        // Smooth with 0.72 decay factor to prevent flicker
        currentSmoothedPowerLevel = Math.Max(boostedLevel, currentSmoothedPowerLevel * 0.72);

        AudioPowerLevelChanged?.Invoke(this, currentSmoothedPowerLevel);
    }

    /// <summary>
    /// Computes the RMS (root mean square) of PCM16 audio samples,
    /// normalized to the range [0, 1].
    /// </summary>
    private static double ComputeRmsPowerLevel(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2; // 16-bit = 2 bytes per sample
        if (sampleCount == 0) return 0;

        double sumOfSquares = 0;
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            double normalizedSample = sample / 32768.0;
            sumOfSquares += normalizedSample * normalizedSample;
        }

        return Math.Sqrt(sumOfSquares / sampleCount);
    }

    public void Dispose()
    {
        StopCapture();
        GC.SuppressFinalize(this);
    }
}
