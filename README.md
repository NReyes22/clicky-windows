# Clicky for Windows

AI companion that lives in your system tray. Hold **Ctrl+Alt** to talk, and Clicky sees your screen, responds with voice, and points a blue cursor at UI elements it references.

![Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)

## What it does

1. **Push-to-talk** (Ctrl+Alt) captures your voice via AssemblyAI real-time transcription
2. **Screenshots** all your monitors and sends them to Claude along with your transcript
3. **Claude responds** with streamed text and voice (ElevenLabs or Windows TTS fallback)
4. **Blue cursor overlay** follows your mouse and flies to UI elements Claude references

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A deployed [Cloudflare Worker](../worker/) proxy with API keys for:
  - Anthropic (Claude)
  - AssemblyAI (speech-to-text)
  - ElevenLabs (text-to-speech, optional — falls back to Windows TTS)

## Setup

1. Clone this repo
2. Update `ClickyWindows/appsettings.json` with your Cloudflare Worker URL:
   ```json
   {
     "WorkerBaseUrl": "https://your-worker.workers.dev",
     "DefaultModel": "claude-sonnet-4-6"
   }
   ```
3. Build and run:
   ```bash
   dotnet restore
   dotnet run --project ClickyWindows
   ```

## Deploy the Cloudflare Worker

The app needs a Cloudflare Worker proxy that holds your API keys. See the [worker/](https://github.com/farzaa/clicky/tree/main/worker) directory in the original macOS repo for the worker code.

```bash
cd worker
npm install
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put ASSEMBLYAI_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
npx wrangler deploy
```

## Architecture

- **C# / WPF / .NET 8** native Windows app
- **System tray only** — no taskbar icon, no main window
- **Transparent overlay** with click-through for the blue cursor
- **NAudio** for microphone capture and audio playback
- **Spring physics** for smooth cursor following
- **Bezier arc animation** for cursor pointing at UI elements

## Credits

Windows port of [Clicky](https://github.com/farzaa/clicky) by [@farzaa](https://github.com/farzaa).
