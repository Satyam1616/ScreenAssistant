using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ScreenAssistant.Config;

/// <summary>
/// User-configurable settings, persisted as JSON next to the executable.
/// Everything here is safe to expose in a future settings UI.
/// </summary>
public sealed class Settings
{
    // Global hotkey. Modifiers: 1=Alt, 2=Ctrl, 4=Shift, 8=Win (can be OR'd).
    // Default: Ctrl+Space  (Ctrl=2). Key is a Win32 virtual-key code; 0x20 = Space.
    public uint HotkeyModifiers { get; set; } = 2;      // MOD_CONTROL
    public uint HotkeyVirtualKey { get; set; } = 0x20;  // VK_SPACE

    // --- Backend choice (set by the first-run setup screen) ---------------
    // "cloud"  = answers come from a free-tier OpenAI-compatible cloud API.
    // "ollama" = answers come from a local Ollama server (private, offline).
    // Empty until the user picks in SetupWindow.
    public string? Provider { get; set; }
    public bool SetupComplete { get; set; }

    // --- Cloud backend (default: Groq free tier, OpenAI-compatible) --------
    // Endpoint/model/key are all editable so this also works with OpenRouter
    // or any other OpenAI-compatible endpoint, and a retired model id is a
    // one-field fix (Groq rotates its model ids over time).
    public string CloudEndpoint { get; set; } =
        "https://api.groq.com/openai/v1/chat/completions";
    public string CloudModel { get; set; } = "openai/gpt-oss-20b";
    // Plaintext on the recipient's disk. This is the accepted "bake-in" risk:
    // to give recipients zero setup, the sharer embeds their own free key.
    public string CloudApiKey { get; set; } = "";
    public int MaxTokens { get; set; } = 1024;

    // Local LLM (Ollama) settings.
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    // qwen2.5:1.5b was benchmarked fastest AND most accurate on this class of
    // low-RAM CPU machine for screen Q&A. llama3.2:3b is a slower, higher-ceiling
    // alternative if you pull it and set it here.
    public string Model { get; set; } = "qwen2.5:1.5b";
    public string KeepAlive { get; set; } = "30m"; // keep the model warm

    // The instruction prepended to every conversation.
    public string SystemPrompt { get; set; } =
        "You are a fast on-screen assistant in an ongoing chat. The user sends you " +
        "screen captures as OCR text and asks questions about them. Treat the most " +
        "recent capture as the current screen, but remember earlier turns so the " +
        "user can keep asking follow-ups or capture a new screen. If a capture " +
        "contains a question, answer it directly; otherwise briefly say what is on " +
        "screen. Be concise.";

    // Cap the captured image's longest edge before OCR (0 = no downscale).
    // Smaller = faster OCR, at some cost to accuracy on tiny text. On-screen text
    // is usually large, so 1920 keeps OCR quick without hurting real-world reads.
    public int MaxCaptureEdge { get; set; } = 1920;

    // Free RAM by unloading any *other* model Ollama has resident, so the active
    // model has the machine to itself (faster, less swapping on low-RAM PCs).
    public bool UnloadOtherModels { get; set; } = true;

    private static string PathOnDisk =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
            {
                var json = File.ReadAllText(PathOnDisk);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { /* fall back to defaults on any parse/IO error */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathOnDisk, json);
        }
        catch { /* non-fatal: settings just won't persist */ }
    }

    /// <summary>
    /// The cloud API key to actually use, tried in order: the key saved in
    /// settings.json, then a key the sharer baked into the build via the
    /// embedded <c>cloud.key</c> resource. Returns "" if neither is set (the
    /// setup screen then asks the recipient to paste one in).
    /// </summary>
    public string ResolveCloudApiKey()
    {
        if (!string.IsNullOrWhiteSpace(CloudApiKey)) return CloudApiKey.Trim();
        return EmbeddedCloudKey();
    }

    /// <summary>Reads the baked-in key from the embedded cloud.key resource, if any.</summary>
    public static string EmbeddedCloudKey()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("ScreenAssistant.cloud.key");
            if (s is null) return "";
            using var r = new StreamReader(s);
            var key = r.ReadToEnd().Trim();
            // The placeholder shipped in the repo is not a real key.
            return key.StartsWith("PASTE_YOUR") ? "" : key;
        }
        catch { return ""; }
    }
}
