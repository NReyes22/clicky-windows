using System.Diagnostics;
using NAudio.Wave;

namespace ClickyWindows.Services;

/// <summary>
/// Captures microphone audio as PCM16 mono 16kHz, matching the format required by
/// AssemblyAI streaming.
///
/// The mic runs continuously once initialized. StartCapture/StopCapture toggle
/// whether audio data is forwarded to listeners via AudioDataAvailable.
/// Also maintains a ring buffer so that audio captured BEFORE the AssemblyAI
/// WebSocket connects can be retrieved and sent retroactively.
/// </summary>
public class AudioCaptureService : IDisposable
{
    /// <summary>
    /// Fires for each audio chunk while capturing is active. Used for real-time
    /// streaming to AssemblyAI.
    /// </summary>
    public event EventHandler<byte[]>? AudioDataAvailable;

    /// <summary>
    /// Fires continuously while capturing, for waveform visualization.
    /// </summary>
    public event EventHandler<double>? AudioPowerLevelChanged;

    private WaveInEvent? waveIn;
    private bool isInitialized;

    // Ring buffer: stores recent audio so we can retrieve data from before
    // the WebSocket connected. 30 seconds at 16kHz 16-bit mono = ~960KB
    private const int RingBufferDurationSeconds = 30;
    private const int BytesPerSecond = 16000 * 2;
    private readonly byte[] ringBuffer = new byte[RingBufferDurationSeconds * BytesPerSecond];
    private long ringWritePosition;
    private readonly object ringLock = new();

    // Capture state
    private Stopwatch? captureStopwatch;
    private volatile bool isCapturing;
    private int totalDataAvailableCount;

    public void Initialize()
    {
        if (isInitialized) return;

        Debug.WriteLine("[Clicky] AudioCapture: Initializing persistent mic capture");

        try
        {
            var deviceCount = WaveInEvent.DeviceCount;
            Debug.WriteLine($"[Clicky] AudioCapture: Found {deviceCount} recording device(s)");
            for (int i = 0; i < deviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                Debug.WriteLine($"[Clicky] AudioCapture:   Device {i}: {caps.ProductName}, channels={caps.Channels}");
            }

            waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 64
            };

            waveIn.DataAvailable += WaveIn_DataAvailable;
            waveIn.RecordingStopped += (_, args) =>
            {
                Debug.WriteLine($"[Clicky] AudioCapture: Recording stopped. Exception: {args.Exception?.Message ?? "none"}");
            };

            waveIn.StartRecording();
            isInitialized = true;
            Debug.WriteLine("[Clicky] AudioCapture: Persistent mic recording started successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] AudioCapture: Failed to initialize: {ex.Message}");
        }
    }

    /// <summary>
    /// Start forwarding audio to listeners. Also records the start time so we can
    /// retroactively retrieve audio from the ring buffer. Safe to call from any thread.
    /// </summary>
    public void StartCapture()
    {
        captureStopwatch = Stopwatch.StartNew();
        isCapturing = true;
        Debug.WriteLine($"[Clicky] AudioCapture: Forwarding ON, totalCallbacks={totalDataAvailableCount}");
    }

    /// <summary>
    /// Stop forwarding audio. Safe to call from any thread.
    /// </summary>
    public void StopCapture()
    {
        if (!isCapturing) return;
        isCapturing = false;
        var elapsed = captureStopwatch?.Elapsed ?? TimeSpan.Zero;
        captureStopwatch?.Stop();
        Debug.WriteLine($"[Clicky] AudioCapture: Forwarding OFF, held for {elapsed.TotalMilliseconds:F0}ms, totalCallbacks={totalDataAvailableCount}");
    }

    /// <summary>
    /// Retrieves audio from the ring buffer covering the time since StartCapture was called.
    /// Use this to get audio that was captured before the AssemblyAI WebSocket connected.
    /// </summary>
    public byte[] GetBufferedAudioSinceStart()
    {
        var elapsed = captureStopwatch?.Elapsed ?? TimeSpan.Zero;
        long byteCount = (long)(elapsed.TotalSeconds * BytesPerSecond);

        if (byteCount <= 0) return Array.Empty<byte>();

        lock (ringLock)
        {
            if (byteCount > ringBuffer.Length)
                byteCount = ringBuffer.Length;

            long endPos = ringWritePosition;
            long startPos = endPos - byteCount;
            if (startPos < 0) startPos = 0;
            byteCount = endPos - startPos;

            if (byteCount <= 0) return Array.Empty<byte>();

            var result = new byte[byteCount];
            int ringStart = (int)(startPos % ringBuffer.Length);
            int bytesToCopy = (int)byteCount;

            if (ringStart + bytesToCopy <= ringBuffer.Length)
            {
                Array.Copy(ringBuffer, ringStart, result, 0, bytesToCopy);
            }
            else
            {
                int firstPart = ringBuffer.Length - ringStart;
                int secondPart = bytesToCopy - firstPart;
                Array.Copy(ringBuffer, ringStart, result, 0, firstPart);
                Array.Copy(ringBuffer, 0, result, firstPart, secondPart);
            }

            Debug.WriteLine($"[Clicky] AudioCapture: Retrieved {byteCount} bytes from ring buffer");
            return result;
        }
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        totalDataAvailableCount++;

        if (totalDataAvailableCount <= 5 || totalDataAvailableCount % 500 == 0)
        {
            Debug.WriteLine($"[Clicky] AudioCapture: DataAvailable #{totalDataAvailableCount}, bytes={e.BytesRecorded}, capturing={isCapturing}");
        }

        if (e.BytesRecorded == 0) return;

        // Always write to ring buffer
        lock (ringLock)
        {
            int ringPos = (int)(ringWritePosition % ringBuffer.Length);
            int bytesToWrite = e.BytesRecorded;

            if (ringPos + bytesToWrite <= ringBuffer.Length)
            {
                Array.Copy(e.Buffer, 0, ringBuffer, ringPos, bytesToWrite);
            }
            else
            {
                int firstPart = ringBuffer.Length - ringPos;
                int secondPart = bytesToWrite - firstPart;
                Array.Copy(e.Buffer, 0, ringBuffer, ringPos, firstPart);
                Array.Copy(e.Buffer, firstPart, ringBuffer, 0, secondPart);
            }

            ringWritePosition += bytesToWrite;
        }

        // Forward to listeners when capturing
        if (isCapturing)
        {
            var audioData = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, audioData, e.BytesRecorded);
            AudioDataAvailable?.Invoke(this, audioData);

            var rawPowerLevel = ComputeRmsPowerLevel(e.Buffer, e.BytesRecorded);
            var boostedLevel = Math.Min(Math.Max(rawPowerLevel * 10.2, 0.0), 1.0);
            AudioPowerLevelChanged?.Invoke(this, boostedLevel);
        }
    }

    private static double ComputeRmsPowerLevel(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2;
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
        isCapturing = false;
        if (waveIn != null)
        {
            waveIn.StopRecording();
            waveIn.Dispose();
            waveIn = null;
        }
        GC.SuppressFinalize(this);
    }
}
