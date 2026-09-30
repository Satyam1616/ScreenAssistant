using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScreenAssistant.Config;
using ScreenAssistant.Models;

namespace ScreenAssistant.Services;

/// <summary>
/// Streams the model's answer token-by-token from one of two backends chosen at
/// first run (see <see cref="Settings.Provider"/>):
///  • "ollama" — a local Ollama server (default http://localhost:11434), offline.
///  • "cloud"  — a free-tier, OpenAI-compatible cloud API (default Groq).
/// </summary>
public sealed class LlmService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Settings _settings;

    public LlmService(Settings settings) => _settings = settings;

    private bool IsCloud => string.Equals(_settings.Provider, "cloud", StringComparison.OrdinalIgnoreCase);

    private string? _effectiveModel;

    /// <summary>Which model actually answered (after fallback), for the UI.</summary>
    public string? EffectiveModel => IsCloud ? _settings.CloudModel : _effectiveModel;

    /// <summary>
    /// Resolves the model to use: the configured one if installed, otherwise the
    /// first model Ollama reports. Keeps the app working regardless of which
    /// model the user pulled.
    /// </summary>
    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        if (_effectiveModel is not null) return _effectiveModel;
        try
        {
            using var resp = await _http.GetAsync(_settings.OllamaEndpoint + "/api/tags", ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var names = new List<string>();
                if (doc.RootElement.TryGetProperty("models", out var models))
                    foreach (var m in models.EnumerateArray())
                        if (m.TryGetProperty("name", out var n) && n.GetString() is { } s)
                            names.Add(s);

                if (names.Contains(_settings.Model) || names.Contains(_settings.Model + ":latest"))
                    _effectiveModel = _settings.Model;
                else if (names.Count > 0)
                    _effectiveModel = names[0];
            }
        }
        catch { /* fall through to configured model; the call will error clearly */ }
        return _effectiveModel ??= _settings.Model;
    }

    /// <summary>
    /// Frees RAM by unloading any model Ollama currently holds resident that is
    /// NOT the one we're about to use (sets its keep_alive to 0). On a low-RAM
    /// machine this stops two models fighting over memory, which keeps answers fast.
    /// </summary>
    public async Task UnloadOtherModelsAsync(CancellationToken ct = default)
    {
        if (!_settings.UnloadOtherModels) return;
        try
        {
            var keep = await ResolveModelAsync(ct);
            using var resp = await _http.GetAsync(_settings.OllamaEndpoint + "/api/ps", ct);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("models", out var models)) return;

            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var n) || n.GetString() is not { } name) continue;
                if (name == keep || name == keep + ":latest") continue;

                var body = JsonSerializer.Serialize(new { model = name, keep_alive = 0 });
                using var c = new StringContent(body, Encoding.UTF8, "application/json");
                try { using var _ = await _http.PostAsync(_settings.OllamaEndpoint + "/api/generate", c, ct); }
                catch { /* best-effort unload */ }
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Preloads the model so the first real answer is fast. Safe to ignore failures.
    /// </summary>
    public async Task WarmAsync(CancellationToken ct = default)
    {
        if (IsCloud) return; // nothing to preload for a cloud endpoint
        try
        {
            var model = await ResolveModelAsync(ct);
            await UnloadOtherModelsAsync(ct);
            var body = JsonSerializer.Serialize(new
            {
                model,
                prompt = "",
                stream = false,
                keep_alive = _settings.KeepAlive,
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(
                _settings.OllamaEndpoint + "/api/generate", content, ct);
        }
        catch { /* best-effort warm-up */ }
    }

    /// <summary>
    /// Streams an answer from whichever backend is configured. Invokes
    /// <paramref name="onToken"/> on each delta and returns the full text.
    /// </summary>
    public Task<string> GenerateAsync(
        IReadOnlyList<ChatMessage> messages, Action<string> onToken, CancellationToken ct)
        => IsCloud
            ? GenerateCloudAsync(messages, onToken, ct)
            : GenerateOllamaAsync(messages, onToken, ct);

    /// <summary>
    /// Streams an answer from a local Ollama server. Throws with a friendly
    /// message if Ollama is down.
    /// </summary>
    private async Task<string> GenerateOllamaAsync(
        IReadOnlyList<ChatMessage> messages, Action<string> onToken, CancellationToken ct)
    {
        var model = await ResolveModelAsync(ct);
        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            keep_alive = _settings.KeepAlive,
            stream = true,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post, _settings.OllamaEndpoint + "/api/chat")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not reach the local model at {_settings.OllamaEndpoint}. " +
                "Is Ollama running? Start it and run `ollama pull " +
                $"{model}`.", ex);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Ollama returned {(int)resp.StatusCode}: {err}");
        }

        var sb = new StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentEl))
            {
                var token = contentEl.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    sb.Append(token);
                    onToken(token);
                }
            }

            if (root.TryGetProperty("done", out var done) &&
                done.ValueKind == JsonValueKind.True)
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Streams an answer from an OpenAI-compatible cloud endpoint (default Groq)
    /// using Server-Sent Events. The system prompt and prior turns pass through
    /// as-is (the "system" role stays in the messages array).
    /// </summary>
    private async Task<string> GenerateCloudAsync(
        IReadOnlyList<ChatMessage> messages, Action<string> onToken, CancellationToken ct)
    {
        var key = _settings.ResolveCloudApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "No cloud API key is set. Open Settings and paste a free key " +
                "(get one at https://console.groq.com/keys), or switch to Local mode.");

        var requestBody = JsonSerializer.Serialize(new
        {
            model = _settings.CloudModel,
            stream = true,
            max_tokens = _settings.MaxTokens,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.CloudEndpoint)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Could not reach the cloud service. Check your internet connection " +
                "and try again.", ex);
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(
                "The cloud API key was rejected (invalid or expired). Open Settings " +
                "and enter a valid free key, or switch to Local mode.");
        if (resp.StatusCode == (System.Net.HttpStatusCode)429)
            throw new InvalidOperationException(
                "The free cloud tier is rate-limited right now. Wait a moment and try " +
                "again, or switch to Local mode for unlimited offline use.");
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Cloud API returned {(int)resp.StatusCode}: {err}");
        }

        var sb = new StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            // SSE frames are "data: {json}" lines, ended by "data: [DONE]".
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line["data:".Length..].Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentEl))
                    token = contentEl.GetString();
            }
            catch { continue; /* skip malformed frames */ }

            if (!string.IsNullOrEmpty(token))
            {
                sb.Append(token);
                onToken(token);
            }
        }

        return sb.ToString();
    }
}
