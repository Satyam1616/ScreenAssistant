using Microsoft.Win32;

namespace ScreenAssistant.Services;

/// <summary>
/// Optional "start with Windows" via the per-user Run key. Fully visible — it
/// shows up in Task Manager → Startup. No hiding, easily toggled off.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenAssistant";

    private static string? ExePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && s.Trim('"') == ExePath;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        if (ExePath is null) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key?.SetValue(ValueName, $"\"{ExePath}\"");
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* nothing to remove */ }
    }

    public static void Toggle() { if (IsEnabled()) Disable(); else Enable(); }
}
