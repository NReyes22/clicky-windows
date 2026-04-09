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
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if you don't have it
3. Update `ClickyWindows/appsettings.json` with your Cloudflare Worker URL:
   ```json
   {
     "WorkerBaseUrl": "https://your-worker.workers.dev",
     "DefaultModel": "claude-sonnet-4-6"
   }
   ```
4. Build and run:
   ```bash
   cd ClickyWindows
   dotnet build -r win-x64 --no-self-contained
   dotnet run -r win-x64 --no-self-contained
   ```

## Deploy the Cloudflare Worker

The app needs a Cloudflare Worker proxy that holds your API keys. The worker code is in the [Clicky for Mac repo](https://github.com/farzaa/clicky/tree/main/worker).

```bash
cd worker
npm install
npx wrangler login
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put ASSEMBLYAI_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
npx wrangler deploy
```

The worker exposes three routes used by the app:
- `/chat` — proxies requests to Claude (Anthropic API)
- `/transcribe-token` — generates temporary AssemblyAI streaming tokens
- `/tts` — proxies requests to ElevenLabs (optional — the app falls back to Windows TTS if unavailable)

## Architecture

- **C# / WPF / .NET 8** native Windows app
- **System tray only** — no taskbar icon, no main window
- **Transparent overlay** with click-through for the blue cursor
- **NAudio** for microphone capture and audio playback
- **Persistent mic with ring buffer** — mic runs continuously; Ctrl+Alt toggles audio forwarding to AssemblyAI, with a ring buffer to capture audio before the WebSocket connects
- **Manual modifier key tracking** in the low-level keyboard hook (WH_KEYBOARD_LL) — `GetAsyncKeyState` lags behind inside hook callbacks, so key state is tracked directly from key events
- **Spring physics** for smooth cursor following
- **Bezier arc animation** for cursor pointing at UI elements

## Credits

Windows port of [Clicky](https://github.com/farzaa/clicky) by [@farzaa](https://github.com/farzaa).
