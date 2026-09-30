<div align="center">

# ScreenAssistant

**A fast, keyboard-driven desktop assistant that reads what's on your screen and answers questions about it.**

Press a hotkey → it captures the screen → extracts the text with on-device OCR → an LLM streams a concise answer into an always-on-top overlay you drive entirely with the arrow keys.

[![Download](https://img.shields.io/github/v/release/Satyam1616/ScreenAssistant?label=download&color=0078D6)](https://github.com/Satyam1616/ScreenAssistant/releases/latest/download/ScreenAssistant-Setup.exe)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

</div>

---

## ⬇️ Download

**[Download ScreenAssistant-Setup.exe →](https://github.com/Satyam1616/ScreenAssistant/releases/latest/download/ScreenAssistant-Setup.exe)**

Double-click it. It installs per-user (no admin prompt), adds Start Menu and optional
desktop shortcuts, and launches a one-time setup screen. **No .NET runtime required** —
the installer bundles everything.

> **SmartScreen note:** the installer is unsigned, so Windows may show
> *"Windows protected your PC → More info → Run anyway."* Removing that prompt requires a
> paid code-signing certificate. The app itself is a normal, visible Windows program.

---

## What it is

ScreenAssistant lets you ask questions about anything on screen without switching windows,
copying text, or touching the mouse. It's built for speed and keyboard flow:

- **Global hotkey** (default **Ctrl+Space**) captures the current screen instantly.
- **On-device OCR** (`Windows.Media.Ocr`) pulls the text out — no image ever leaves your PC for reading.
- **An LLM streams the answer** token-by-token into a compact overlay.
- **Arrow keys do everything** — scroll the answer, flip through past captures, ask follow-ups.

This is a normal, visible Windows application. It shows a taskbar/tray presence, appears to
the OS like any other program, and does **nothing** to hide itself from other software.

## Features

- ⚡ **Instant capture-to-answer** on a single hotkey, streamed live.
- ⌨️ **100% keyboard-driven** overlay — no mouse needed.
- 🔒 **Two backends, your choice** (picked on first run, switchable any time):
  - **Local & Private** — runs a small model on your own PC via [Ollama](https://ollama.com), fully offline.
  - **Free Cloud** — answers from a free-tier, OpenAI-compatible cloud model (default: [Groq](https://groq.com)); nothing to install.
- 🧠 **Conversational memory** — follow-up questions remember earlier captures.
- 🗂️ **Capture history** — arrow through recent screens and their answers.
- 🚀 **Warm & fast** — the local model is kept warm; OCR downscaling is tunable.
- 🎛️ **Configurable** — hotkey, model, endpoint, system prompt, and more via `settings.json`.

## Choosing a backend

On first launch a setup screen offers two options. You can reopen it any time from the
tray menu → **Settings…**

| | **Local & Private** | **Free Cloud** |
|---|---|---|
| Runs on | Your PC (offline) | A free cloud API (Groq by default) |
| Setup | App auto-installs Ollama + pulls a ~1 GB model | Nothing — uses a bundled or pasted free key |
| Privacy | Screen text never leaves your machine | OCR text is sent to the cloud provider |
| Speed / limits | Depends on your hardware; unlimited | Fast; shares the free tier's rate limit |
| Needs internet | No | Yes |

Both are OpenAI-compatible under the hood, so the cloud option also works with OpenRouter or
any other compatible endpoint — just edit `CloudEndpoint` / `CloudModel` in `settings.json`.

## Usage

- **Ctrl+Space** — capture the screen and answer (default hotkey).
- In the overlay (no mouse needed):
  - **↑ / ↓** — scroll the answer
  - **← / →** — move through your recent captures
  - **Enter** — type a custom question about the current capture
  - **C** — copy the answer
  - **Esc** — hide the overlay
- Right-click the tray icon to capture, open **Settings…**, or quit.

## Configuration

A `settings.json` is written next to the executable. Editable fields include:

| Field | What it does |
|-------|--------------|
| `HotkeyModifiers` / `HotkeyVirtualKey` | The global hotkey (default Ctrl+Space) |
| `Provider` | `"ollama"` (local) or `"cloud"` |
| `Model` | Local Ollama model (default `qwen2.5:1.5b`) |
| `CloudEndpoint` / `CloudModel` | Cloud API URL and model id |
| `CloudApiKey` | Your cloud key (overrides any baked-in key) |
| `SystemPrompt` | The instruction prepended to every conversation |
| `MaxCaptureEdge` | Longest image edge before OCR (smaller = faster) |
| `MaxTokens` | Cloud answer length cap |

## How it works

| Component | File |
|-----------|------|
| Global hotkey (Win32 `RegisterHotKey`) | `Services/HotkeyService.cs` |
| Full-screen capture (GDI BitBlt) | `Services/CaptureService.cs` |
| On-device OCR (`Windows.Media.Ocr`) | `Services/OcrService.cs` |
| Streaming LLM (Ollama + OpenAI-compatible cloud) | `Services/LlmService.cs` |
| First-run backend chooser | `UI/SetupWindow.xaml(.cs)` |
| Arrow-key overlay UI | `UI/OverlayWindow.xaml(.cs)` |
| Startup / tray / autostart / wiring | `App.xaml.cs`, `Services/StartupService.cs` |

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/). OCR is built into Windows 10/11
(an appropriate language pack must be present).

```powershell
git clone https://github.com/Satyam1616/ScreenAssistant.git
cd ScreenAssistant
dotnet build
dotnet run
```

For the **Local & Private** backend you'll also want [Ollama](https://ollama.com) — but the
in-app setup screen can install it and pull the model for you.

## Building a shareable installer

To produce the single-file `ScreenAssistant-Setup.exe` yourself (and optionally bake in a
free cloud key so recipients need zero setup), see **[SHARING.md](SHARING.md)**. In short:

```powershell
# Self-contained single-file build (bundles .NET → no runtime on target)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist-selfcontained

# Compile the installer (requires Inno Setup: winget install JRSoftware.InnoSetup)
iscc installer\ScreenAssistant.iss
```

## Privacy & security

- **Visible by design.** No stealth, anti-detection, screen-capture blocking, or proctoring
  evasion. It behaves like any ordinary Windows app and can be seen and captured normally.
- **Local mode is fully offline** — screen text never leaves your machine.
- **Cloud mode** sends OCR text to the chosen provider (Groq by default). Don't use it for
  content you can't share with a third-party API.
- **API keys:** a baked-in key can be extracted from a distributed binary, so only ever
  embed a free-tier key you're willing to rotate. Keep real keys out of source control — the
  tracked `cloud.key` is only a placeholder.

## License

[MIT](LICENSE) © 2026 Satyam1616
