using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ScreenAssistant.Services;

/// <summary>
/// Registers a system-wide hotkey and raises <see cref="Pressed"/> when it fires.
/// Uses a message-only window so it works without any visible window focused.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4A21; // arbitrary unique id
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    /// <summary>
    /// Registers the hotkey. Returns false if the combination is already taken
    /// by another app (caller should surface a friendly message).
    /// </summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        var parameters = new HwndSourceParameters("ScreenAssistantHotkey")
        {
            ParentWindow = HWND_MESSAGE, // message-only window (no UI)
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            if (_registered)
                UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
