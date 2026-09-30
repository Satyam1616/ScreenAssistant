using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScreenAssistant.Models;

/// <summary>
/// One turn in the ongoing conversation. <see cref="Content"/> is what the model
/// sees (full OCR text / question); <see cref="Display"/> is the shorter label
/// shown in the overlay transcript (falls back to Content when null).
///
/// Implements <see cref="INotifyPropertyChanged"/> so the chat UI updates live
/// as assistant tokens stream into <see cref="Content"/>.
/// </summary>
public sealed class ChatMessage : INotifyPropertyChanged
{
    public required string Role { get; init; }   // "system" | "user" | "assistant"

    private string _content = "";
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            OnChanged();
            OnChanged(nameof(ForDisplay));
        }
    }

    private string? _display;
    public string? Display
    {
        get => _display;
        set
        {
            if (_display == value) return;
            _display = value;
            OnChanged();
            OnChanged(nameof(ForDisplay));
        }
    }

    public string ForDisplay => Display ?? Content;

    /// <summary>True for the screen-capture user turns (📷) vs typed questions.</summary>
    public bool IsCapture { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
