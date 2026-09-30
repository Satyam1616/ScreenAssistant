using System.Windows;
using ScreenAssistant.Config;
using ScreenAssistant.Services;
using ScreenAssistant.UI;
using Forms = System.Windows.Forms;

namespace ScreenAssistant;

// Base class (System.Windows.Application) is declared by the XAML-generated
// partial; omitted here to avoid the WinForms/WPF 'Application' ambiguity.
public partial class App
{
    private Settings _settings = null!;
    private HotkeyService _hotkey = null!;
    private OverlayWindow _overlay = null!;
    private LlmService _llm = null!;
    private Forms.NotifyIcon _tray = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = Settings.Load();

        // First run: let the user choose a backend (Free Cloud vs Local Ollama).
        // Cancelling the setup screen quits the app.
        if (!_settings.SetupComplete)
        {
            var setup = new SetupWindow(_settings);
            if (setup.ShowDialog() != true) { Shutdown(); return; }
        }

        var capture = new CaptureService();
        var ocr = new OcrService();
        _llm = new LlmService(_settings);

        _overlay = new OverlayWindow(capture, ocr, _llm, _settings);

        _hotkey = new HotkeyService();
        _hotkey.Pressed += () => Dispatcher.Invoke(_overlay.OnHotkey);
        if (!_hotkey.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey))
        {
            System.Windows.MessageBox.Show(
                "Could not register the global hotkey (default Ctrl+Space). Another " +
                "app may be using it. Change it in settings.json next to the app.",
                "ScreenAssistant", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SetupTray(ocr);

        // Warm the local model so the first answer is fast (no-op for cloud).
        _ = _llm.WarmAsync();
    }

    private void SetupTray(OcrService ocr)
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "ScreenAssistant — Ctrl+Space to read the screen",
        };
        // Double-clicking the tray icon captures, same as the hotkey.
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(_overlay.OnHotkey);

        var menu = new Forms.ContextMenuStrip();
        var capture = new Forms.ToolStripMenuItem("Capture now  (Ctrl+Space)", null,
            (_, _) => Dispatcher.Invoke(_overlay.OnHotkey))
        { Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(capture);
        menu.Items.Add(new Forms.ToolStripSeparator());

        // Reopen the setup screen to switch backend or re-enter a cloud key.
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.Invoke(OpenSettings));

        // Start-with-Windows toggle (per-user Run key; visible in Task Manager).
        var startup = new Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupService.IsEnabled(),
        };
        startup.CheckedChanged += (_, _) =>
        {
            if (startup.Checked) StartupService.Enable(); else StartupService.Disable();
        };
        menu.Items.Add(startup);

        if (!ocr.IsAvailable)
            menu.Items.Add("⚠ No OCR language installed").Enabled = false;

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit ScreenAssistant", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    /// <summary>Reopens the setup screen; changes take effect on the next answer.</summary>
    private void OpenSettings()
    {
        var setup = new SetupWindow(_settings) { Topmost = true };
        if (setup.ShowDialog() == true)
            _ = _llm.WarmAsync(); // warms the local model if they switched to Ollama
    }

    /// <summary>Loads the embedded app icon for the tray; falls back if missing.</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("ScreenAssistant.appicon.ico");
            if (s is not null) return new System.Drawing.Icon(s);
        }
        catch { /* fall back below */ }
        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        base.OnExit(e);
    }
}
