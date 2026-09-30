namespace ScreenAssistant.Models;

/// <summary>
/// One capture → answer record. History is a list of these; the overlay walks
/// them with Left/Right arrows.
/// </summary>
public sealed class Answer
{
    public DateTime CapturedAt { get; } = DateTime.Now;

    /// <summary>Raw OCR text pulled from the screen.</summary>
    public string ScreenText { get; set; } = "";

    /// <summary>Optional custom question the user typed (Enter in the overlay).</summary>
    public string? Question { get; set; }

    /// <summary>The model's answer, filled in as tokens stream.</summary>
    public string Text { get; set; } = "";

    /// <summary>Set when generation failed (e.g. Ollama not running).</summary>
    public string? Error { get; set; }

    public bool IsComplete { get; set; }
}
