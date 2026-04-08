using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ClickyWindows.Services;

/// <summary>
/// Real-time speech-to-text via AssemblyAI v3 WebSocket streaming.
/// Port of AssemblyAIStreamingTranscriptionProvider.swift — handles token fetch,
/// WebSocket lifecycle, turn-based transcript assembly with grace period/fallback.
/// </summary>
public class AssemblyAiTranscriptionService : IDisposable
{
    public event EventHandler<string>? OnTranscriptUpdate;
    public event EventHandler<string>? OnFinalTranscriptReady;
    public event EventHandler<string>? OnError;

    private readonly string workerBaseUrl;
    private readonly HttpClient httpClient;
    private ClientWebSocket? webSocket;
    private CancellationTokenSource? sessionCancellation;

    // Turn-based transcript tracking (port of storedTurnTranscriptsByOrder)
    private readonly Dictionary<int, StoredTurnTranscript> storedTurnTranscriptsByOrder = new();
    private string activeTurnText = "";
    private int activeTurnOrder = -1;

    // Final transcript state
    private bool isFinalTranscriptRequested;
    private bool hasFinalBeenDelivered;
    private CancellationTokenSource? fallbackCancellation;

    private const double FallbackTimeoutSeconds = 3.0;

    public AssemblyAiTranscriptionService(string workerBaseUrl)
    {
        this.workerBaseUrl = workerBaseUrl;
        httpClient = new HttpClient();
    }

    public async Task StartSessionAsync(string[]? keyterms = null)
    {
        // Don't call StopSessionAsync here — it cancels the session token
        // Just clean up the previous websocket if any
        if (webSocket != null)
        {
            try { webSocket.Dispose(); } catch { }
            webSocket = null;
        }

        sessionCancellation = new CancellationTokenSource();
        storedTurnTranscriptsByOrder.Clear();
        activeTurnText = "";
        activeTurnOrder = -1;
        isFinalTranscriptRequested = false;
        hasFinalBeenDelivered = false;

        var token = await FetchTemporaryTokenAsync();
        if (token == null)
        {
            Debug.WriteLine("[Clicky] AssemblyAI: Failed to fetch token");
            OnError?.Invoke(this, "Failed to fetch AssemblyAI token");
            return;
        }
        Debug.WriteLine("[Clicky] AssemblyAI: Got token");

        var wsUrl = $"wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&encoding=pcm_s16le&format_turns=true&speech_model=u3-rt-pro&token={token}";

        if (keyterms != null && keyterms.Length > 0)
        {
            var keytermsJson = JsonSerializer.Serialize(keyterms);
            wsUrl += $"&keyterms_prompt={Uri.EscapeDataString(keytermsJson)}";
        }

        webSocket = new ClientWebSocket();

        try
        {
            await webSocket.ConnectAsync(new Uri(wsUrl), sessionCancellation.Token);
            Debug.WriteLine("[Clicky] AssemblyAI: WebSocket connected");

            _ = Task.Run(() => ReceiveLoopAsync(sessionCancellation.Token));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] AssemblyAI: WebSocket connection failed: {ex.Message}");
            OnError?.Invoke(this, $"WebSocket connection failed: {ex.Message}");
        }
    }

    public async Task SendAudioDataAsync(byte[] pcm16Data)
    {
        if (webSocket?.State != WebSocketState.Open) return;

        try
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(pcm16Data),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken: sessionCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception)
        {
            // Connection may have been closed
        }
    }

    public async Task RequestFinalTranscriptAsync()
    {
        Debug.WriteLine("[Clicky] AssemblyAI: RequestFinalTranscript called");
        isFinalTranscriptRequested = true;
        hasFinalBeenDelivered = false;

        // Send force_end_of_turn message
        if (webSocket?.State == WebSocketState.Open)
        {
            var message = "{\"type\":\"force_end_of_turn\"}";
            var bytes = Encoding.UTF8.GetBytes(message);
            try
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: sessionCancellation?.Token ?? CancellationToken.None);
                Debug.WriteLine("[Clicky] AssemblyAI: Sent force_end_of_turn");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Clicky] AssemblyAI: Failed to send force_end_of_turn: {ex.Message}");
            }
        }
        else
        {
            Debug.WriteLine($"[Clicky] AssemblyAI: WebSocket not open (state: {webSocket?.State})");
        }

        // Always start a fallback timer that will deliver whatever we have
        fallbackCancellation?.Cancel();
        fallbackCancellation = new CancellationTokenSource();
        var token = fallbackCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(FallbackTimeoutSeconds), token);

                // Fallback: deliver whatever transcript we have (even if empty)
                Debug.WriteLine("[Clicky] AssemblyAI: Fallback timeout reached");
                DeliverFinalTranscript(allowEmpty: true);
            }
            catch (TaskCanceledException)
            {
                // Cancelled because we already delivered
            }
        });
    }

    public async Task StopSessionAsync()
    {
        fallbackCancellation?.Cancel();
        sessionCancellation?.Cancel();

        if (webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
        }

        webSocket?.Dispose();
        webSocket = null;
    }

    private async Task<string?> FetchTemporaryTokenAsync()
    {
        try
        {
            var response = await httpClient.PostAsync($"{workerBaseUrl}/transcribe-token", null);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[Clicky] AssemblyAI: Token fetch failed with status {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] AssemblyAI: Token fetch exception: {ex.Message}");
            return null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        Debug.WriteLine("[Clicky] AssemblyAI: Receive loop started");

        try
        {
            while (webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.WriteLine("[Clicky] AssemblyAI: WebSocket closed by server");
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Text) continue;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Debug.WriteLine($"[Clicky] AssemblyAI: Received: {message[..Math.Min(200, message.Length)]}");
                HandleWebSocketMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Clicky] AssemblyAI: Receive loop cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] AssemblyAI: Receive error: {ex.Message}");
            OnError?.Invoke(this, $"WebSocket receive error: {ex.Message}");
        }

        // If we were waiting for a final transcript and the connection closed, deliver what we have
        if (isFinalTranscriptRequested && !hasFinalBeenDelivered)
        {
            Debug.WriteLine("[Clicky] AssemblyAI: Connection closed while waiting for final, delivering now");
            DeliverFinalTranscript(allowEmpty: true);
        }
    }

    private void HandleWebSocketMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            // AssemblyAI v3 uses PascalCase message types (Begin, Turn, Termination, etc.)
            var messageType = typeElement.GetString()?.ToLowerInvariant();

            switch (messageType)
            {
                case "begin":
                    Debug.WriteLine("[Clicky] AssemblyAI: Session began");
                    break;

                case "turn":
                    HandleTurnMessage(root);
                    break;

                case "speechstarted":
                    Debug.WriteLine("[Clicky] AssemblyAI: Speech started");
                    break;

                case "speechended":
                    Debug.WriteLine("[Clicky] AssemblyAI: Speech ended");
                    break;

                case "termination":
                    Debug.WriteLine("[Clicky] AssemblyAI: Termination received");
                    if (isFinalTranscriptRequested)
                    {
                        DeliverFinalTranscript(allowEmpty: true);
                    }
                    break;

                case "error":
                    var errorMessage = root.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString() ?? "Unknown error"
                        : "Unknown error";
                    Debug.WriteLine($"[Clicky] AssemblyAI: Error: {errorMessage}");
                    OnError?.Invoke(this, errorMessage);
                    break;

                default:
                    Debug.WriteLine($"[Clicky] AssemblyAI: Unknown message type: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Clicky] AssemblyAI: Parse error: {ex.Message}");
        }
    }

    private void HandleTurnMessage(JsonElement root)
    {
        var transcript = root.TryGetProperty("transcript", out var transcriptElement)
            ? transcriptElement.GetString() ?? ""
            : "";
        var turnOrder = root.TryGetProperty("turn_order", out var orderElement)
            ? orderElement.GetInt32()
            : 0;
        var endOfTurn = root.TryGetProperty("end_of_turn", out var eotElement) && eotElement.GetBoolean();
        var turnIsFormatted = root.TryGetProperty("turn_is_formatted", out var formatElement) && formatElement.GetBoolean();

        Debug.WriteLine($"[Clicky] AssemblyAI: Turn order={turnOrder}, endOfTurn={endOfTurn}, formatted={turnIsFormatted}, text=\"{transcript}\"");

        if (endOfTurn)
        {
            storedTurnTranscriptsByOrder[turnOrder] = new StoredTurnTranscript(transcript, turnIsFormatted);
            activeTurnText = "";
            activeTurnOrder = -1;
        }
        else
        {
            activeTurnText = transcript;
            activeTurnOrder = turnOrder;
        }

        var fullTranscript = BuildFullTranscript();
        OnTranscriptUpdate?.Invoke(this, fullTranscript);

        // If we got a completed turn after requesting final, deliver it
        if (isFinalTranscriptRequested && endOfTurn)
        {
            Debug.WriteLine("[Clicky] AssemblyAI: Got end_of_turn after final requested, delivering");
            fallbackCancellation?.Cancel();
            DeliverFinalTranscript(allowEmpty: false);
        }
    }

    private string BuildFullTranscript()
    {
        var parts = new List<string>();

        foreach (var kvp in storedTurnTranscriptsByOrder.OrderBy(x => x.Key))
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value.Transcript))
            {
                parts.Add(kvp.Value.Transcript);
            }
        }

        if (!string.IsNullOrWhiteSpace(activeTurnText))
        {
            parts.Add(activeTurnText);
        }

        return string.Join(" ", parts);
    }

    private void DeliverFinalTranscript(bool allowEmpty)
    {
        if (hasFinalBeenDelivered) return;
        hasFinalBeenDelivered = true;
        isFinalTranscriptRequested = false;
        fallbackCancellation?.Cancel();

        var finalTranscript = BuildFullTranscript().Trim();
        Debug.WriteLine($"[Clicky] AssemblyAI: Delivering final transcript: \"{finalTranscript}\"");

        if (!string.IsNullOrEmpty(finalTranscript))
        {
            OnFinalTranscriptReady?.Invoke(this, finalTranscript);
        }
        else if (allowEmpty)
        {
            Debug.WriteLine("[Clicky] AssemblyAI: Transcript is empty, not delivering");
            // Fire with empty so the state machine returns to idle
            OnFinalTranscriptReady?.Invoke(this, "");
        }
    }

    public void Dispose()
    {
        fallbackCancellation?.Cancel();
        sessionCancellation?.Cancel();
        webSocket?.Dispose();
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private record StoredTurnTranscript(string Transcript, bool IsFormatted);
}
