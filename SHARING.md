# Sharing ScreenAssistant

This app can answer about your screen in two ways. Each recipient picks one on
first launch (and can change it later from the tray menu → **Settings…**):

- **Free Cloud** — answers come from a free online model (default: **Groq**).
  The recipient installs nothing and needs only an internet connection.
- **Local & Private** — the app installs **Ollama** and downloads a small model
  (`qwen2.5:1.5b`, ~1 GB) onto the recipient's PC, then runs fully offline.

Neither option requires the recipient to install .NET — the shared build bundles
it.

---

## Build a shareable installer

From the project folder (`C:\Users\saroj\ScreenAssistant`):

```powershell
# 1. Self-contained, single-file build (bundles .NET → no runtime on target)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist-selfcontained

# 2. Install Inno Setup once (the installer compiler)
winget install --id JRSoftware.InnoSetup -e

# 3. Compile the installer → installer\ScreenAssistant-Setup.exe
iscc installer\ScreenAssistant.iss
```

Send recipients the single **`ScreenAssistant-Setup.exe`**. They double-click it;
it installs to Program Files, adds Start Menu / optional Desktop shortcuts and an
optional "start with Windows" entry, and launches the setup screen.

> **SmartScreen note:** the installer is unsigned, so Windows may show
> "Windows protected your PC → More info → Run anyway." Removing that prompt
> requires a paid code-signing certificate (out of scope here).

---

## (Optional) Bake in a cloud key for zero recipient setup

If you want **Free Cloud** recipients to not even enter a key:

1. Get a free key at <https://console.groq.com/keys>.
2. Paste it (the raw key, nothing else) into **`cloud.key`** in the project root,
   replacing the `PASTE_YOUR_...` placeholder.
3. Build as above. The key is embedded into the app.

**Do this locally — never paste your key into a chat/assistant.** The `cloud.key`
placeholder is safe to commit; a real key is not (keep it out of source control).

### Honest caveats about a baked-in key
- A key embedded in a distributed binary **can be extracted** by a determined
  recipient. Only bake in a free-tier key you're willing to rotate/revoke.
- The free tier's **rate limit is shared** across everyone using your key, so
  heavy combined use can hit limits. Recipients who pick **Local & Private** use
  no key and have no such limit.

If you leave `cloud.key` as the placeholder, Free-Cloud recipients simply paste
their own free key on the setup screen instead.

---

## Changing the cloud model

Groq occasionally retires model ids. The default is `openai/gpt-oss-20b` (fast,
concise; verified working Sep 2026). If a recipient sees a model error, they can
edit **`CloudModel`** (and `CloudEndpoint` for a different OpenAI-compatible
provider, e.g. OpenRouter) in `settings.json` next to the exe — no rebuild needed.
To see which models your key can use, call `https://api.groq.com/openai/v1/models`.
