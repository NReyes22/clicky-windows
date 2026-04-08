using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClickyWindows.Models;

namespace ClickyWindows.Services;

/// <summary>
/// Claude vision API client with SSE streaming via the Cloudflare Worker proxy.
/// Port of ClaudeAPI.swift — handles TLS warmup, base64 image encoding,
/// conversation history, and streaming response parsing.
/// </summary>
public class ClaudeApiClient : IDisposable
{
    private readonly string proxyUrl;
    private readonly HttpClient httpClient;
    private string model;

    public ClaudeApiClient(string proxyUrl, string model)
    {
        this.proxyUrl = proxyUrl;
        this.model = model;

        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        // TLS warmup: pre-establish connection (port of warmupTLSConnection)
        _ = Task.Run(async () =>
        {
            try
            {
                using var warmupRequest = new HttpRequestMessage(HttpMethod.Head, proxyUrl);
                await httpClient.SendAsync(warmupRequest);
            }
            catch { /* Warmup failure is non-fatal */ }
        });
    }

    public void SetModel(string newModel) => model = newModel;

    /// <summary>
    /// Sends screenshots and a transcript to Claude with SSE streaming.
    /// Calls onTextChunk for each text delta received.
    /// Returns the complete response text.
    /// </summary>
    public async Task<string> AnalyzeImageStreamingAsync(
        List<CompanionScreenCapture> screenshots,
        string transcript,
        List<ConversationEntry> conversationHistory,
        Action<string> onTextChunk,
        CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(screenshots, transcript, conversationHistory);
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyUrl}/chat")
        {
            Content = jsonContent
        };

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulatedText = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // Look for content_block_delta with text_delta type
                if (root.TryGetProperty("type", out var typeElement) &&
                    typeElement.GetString() == "content_block_delta" &&
                    root.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("type", out var deltaType) &&
                    deltaType.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var textElement))
                {
                    var textChunk = textElement.GetString() ?? "";
                    accumulatedText.Append(textChunk);
                    onTextChunk(accumulatedText.ToString());
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE data lines
            }
        }

        return accumulatedText.ToString();
    }

    private object BuildRequestBody(
        List<CompanionScreenCapture> screenshots,
        string transcript,
        List<ConversationEntry> conversationHistory)
    {
        var messages = new List<object>();

        // Add conversation history as previous user/assistant pairs
        foreach (var entry in conversationHistory)
        {
            messages.Add(new { role = "user", content = entry.UserTranscript });
            messages.Add(new { role = "assistant", content = entry.AssistantResponse });
        }

        // Build current message content blocks (images + text)
        var contentBlocks = new List<object>();

        foreach (var screenshot in screenshots)
        {
            var mediaType = DetectImageMediaType(screenshot.ImageData);
            var base64Data = Convert.ToBase64String(screenshot.ImageData);

            contentBlocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = mediaType,
                    data = base64Data
                }
            });

            contentBlocks.Add(new
            {
                type = "text",
                text = screenshot.Label
            });
        }

        // Add the user's transcript as the final text block
        contentBlocks.Add(new
        {
            type = "text",
            text = transcript
        });

        messages.Add(new { role = "user", content = contentBlocks });

        return new
        {
            model,
            max_tokens = 1024,
            stream = true,
            system = SystemPrompt,
            messages
        };
    }

    /// <summary>
    /// Detects image MIME type by checking magic bytes.
    /// PNG: 0x89 0x50 0x4E 0x47, else assume JPEG.
    /// </summary>
    private static string DetectImageMediaType(byte[] imageData)
    {
        if (imageData.Length >= 4 &&
            imageData[0] == 0x89 &&
            imageData[1] == 0x50 &&
            imageData[2] == 0x4E &&
            imageData[3] == 0x47)
        {
            return "image/png";
        }
        return "image/jpeg";
    }

    // Exact system prompt from CompanionManager.swift lines 544-577
    private const string SystemPrompt = """
        you're clicky, a friendly always-on companion that lives in the user's system tray. the user just spoke to you via push-to-talk and you can see their screen(s). your reply will be spoken aloud via text-to-speech, so write the way you'd actually talk. this is an ongoing conversation — you remember everything they've said before.

        rules:
        - default to one or two sentences. be direct and dense. BUT if the user asks you to explain more, go deeper, or elaborate, then go all out — give a thorough, detailed explanation with no length limit.
        - all lowercase, casual, warm. no emojis.
        - write for the ear, not the eye. short sentences. no lists, bullet points, markdown, or formatting — just natural speech.
        - don't use abbreviations or symbols that sound weird read aloud. write "for example" not "e.g.", spell out small numbers.
        - if the user's question relates to what's on their screen, reference specific things you see.
        - if the screenshot doesn't seem relevant to their question, just answer the question directly.
        - you can help with anything — coding, writing, general knowledge, brainstorming.
        - never say "simply" or "just".
        - don't read out code verbatim. describe what the code does or what needs to change conversationally.
        - focus on giving a thorough, useful explanation. don't end with simple yes/no questions like "want me to explain more?" or "should i show you?" — those are dead ends that force the user to just say yes.
        - instead, when it fits naturally, end by planting a seed — mention something bigger or more ambitious they could try, a related concept that goes deeper, or a next-level technique that builds on what you just explained. make it something worth coming back for, not a question they'd just nod to. it's okay to not end with anything extra if the answer is complete on its own.
        - if you receive multiple screen images, the one labeled "primary focus" is where the cursor is — prioritize that one but reference others if relevant.

        element pointing:
        you have a small blue triangle cursor that can fly to and point at things on screen. use it whenever pointing would genuinely help the user — if they're asking how to do something, looking for a menu, trying to find a button, or need help navigating an app, point at the relevant element. err on the side of pointing rather than not pointing, because it makes your help way more useful and concrete.

        don't point at things when it would be pointless — like if the user asks a general knowledge question, or the conversation has nothing to do with what's on screen, or you'd just be pointing at something obvious they're already looking at. but if there's a specific UI element, menu, button, or area on screen that's relevant to what you're helping with, point at it.

        when you point, append a coordinate tag at the very end of your response, AFTER your spoken text. the screenshot images are labeled with their pixel dimensions. use those dimensions as the coordinate space. the origin (0,0) is the top-left corner of the image. x increases rightward, y increases downward.

        format: [POINT:x,y:label] where x,y are integer pixel coordinates in the screenshot's coordinate space, and label is a short 1-3 word description of the element (like "search bar" or "save button"). if the element is on the cursor's screen you can omit the screen number. if the element is on a DIFFERENT screen, append :screenN where N is the screen number from the image label (e.g. :screen2). this is important — without the screen number, the cursor will point at the wrong place.

        if pointing wouldn't help, append [POINT:none].

        examples:
        - user asks how to color grade in final cut: "you'll want to open the color inspector — it's right up in the top right area of the toolbar. click that and you'll get all the color wheels and curves. [POINT:1100,42:color inspector]"
        - user asks what html is: "html stands for hypertext markup language, it's basically the skeleton of every web page. curious how it connects to the css you're looking at? [POINT:none]"
        - user asks how to commit in xcode: "see that source control menu up top? click that and hit commit, or you can use command option c as a shortcut. [POINT:285,11:source control]"
        - element is on screen 2 (not where cursor is): "that's over on your other monitor — see the terminal window? [POINT:400,300:terminal:screen2]"
        """;

    public void Dispose()
    {
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
