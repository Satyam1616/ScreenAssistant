using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using ScreenAssistant.Config;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace ScreenAssistant.UI;

/// <summary>
/// First-run (and re-openable) setup screen. Lets the user pick a backend:
///  • Free Cloud — a free-tier OpenAI-compatible API (default Groq); no install.
///  • Local &amp; Private — installs Ollama + pulls the model on this PC; offline.
/// On success it writes the choice into <see cref="Settings"/> and returns true.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly Settings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string _mode = ""; // "cloud" | "local"
    private bool _busy;

    public SetupWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        // Drag the frameless window by its header.
        Header.MouseLeftButtonDown += (_, e) =>
        { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // Prefill the key box from a baked-in or previously saved key.
        Loaded += (_, _) =>
        {
            KeyBox.Text = _settings.ResolveCloudApiKey();
            // If re-opening from the tray, jump to the current choice.
            if (string.Equals(_settings.Provider, "cloud", StringComparison.OrdinalIgnoreCase))
                ChooseCloud_Click(this, new RoutedEventArgs());
            else if (string.Equals(_settings.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
                ChooseLocal_Click(this, new RoutedEventArgs());
        };
    }

    // ---- Choice selection --------------------------------------------------

    private void ChooseCloud_Click(object sender, RoutedEventArgs e)
    {
        _mode = "cloud";
        CloudPanel.Visibility = Visibility.Visible;
        LocalPanel.Visibility = Visibility.Collapsed;
        ContinueBtn.IsEnabled = true;
        ContinueBtn.Content = "Save & Continue";
        Highlight(CloudCard, true);
        Highlight(LocalCard, false);
    }

    private async void ChooseLocal_Click(object sender, RoutedEventArgs e)
    {
        _mode = "local";
        LocalPanel.Visibility = Visibility.Visible;
        CloudPanel.Visibility = Visibility.Collapsed;
        ContinueBtn.IsEnabled = true;
        ContinueBtn.Content = "Set up & Continue";
        Highlight(LocalCard, true);
        Highlight(CloudCard, false);

        var exe = await Task.Run(ResolveOllamaExe);
        LocalStatus.Text = exe is null
            ? "Ollama isn't installed yet — it will be installed when you continue."
            : "Ollama is already installed. Continue to make sure the model is downloaded.";
    }

    private static void Highlight(Button card, bool on)
    {
        if (card.Template?.FindName("b", card) is Border b)
            b.BorderBrush = on
                ? (Brush)card.FindResource("Accent")
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3F));
    }

    // ---- Actions -----------------------------------------------------------

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_mode == "cloud") SaveCloud();
        else if (_mode == "local") await RunLocalSetupAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        DialogResult = false;
        Close();
    }

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* no browser available */ }
        e.Handled = true;
    }

    private void SaveCloud()
    {
        var key = KeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(Settings.EmbeddedCloudKey()))
        {
            CloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
            CloudStatus.Text = "Enter a free API key first (or pick Local & Private).";
            return;
        }
        _settings.Provider = "cloud";
        _settings.CloudApiKey = key; // may be empty → runtime falls back to the baked key
        _settings.SetupComplete = true;
        _settings.Save();
        DialogResult = true;
        Close();
    }

    private async void TestKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var key = KeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(key)) key = Settings.EmbeddedCloudKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            CloudStatus.Text = "No key to test.";
            return;
        }

        SetBusy(true);
        CloudStatus.Foreground = (Brush)FindResource("Muted");
        CloudStatus.Text = "Testing…";
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _settings.CloudModel,
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "ping" } },
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, _settings.CloudEndpoint)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                CloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE7, 0x9A));
                CloudStatus.Text = "Key works ✓";
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                CloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
                CloudStatus.Text = "Key rejected (invalid or expired).";
            }
            else
            {
                CloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
                CloudStatus.Text = $"Server said {(int)resp.StatusCode}. Check the model id in settings.";
            }
        }
        catch
        {
            CloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
            CloudStatus.Text = "Could not reach the service (check your internet).";
        }
        finally { SetBusy(false); }
    }

    // ---- Local (Ollama) setup ---------------------------------------------

    private async Task RunLocalSetupAsync()
    {
        SetBusy(true);
        LocalBar.Visibility = Visibility.Visible;
        LocalBar.IsIndeterminate = true;
        try
        {
            var exe = await Task.Run(ResolveOllamaExe);
            if (exe is null)
            {
                SetLocal("Installing Ollama (this can take a few minutes)…");
                var code = await RunProcessAsync("winget",
                    "install --id Ollama.Ollama -e --silent " +
                    "--accept-package-agreements --accept-source-agreements",
                    line => SetLocal(line));
                exe = await Task.Run(ResolveOllamaExe);
                if (exe is null)
                {
                    SetLocal($"Ollama install did not complete (winget exit {code}). " +
                             "You can install it manually from ollama.com, then reopen setup.");
                    return;
                }
            }

            // Make sure the local server is up (harmless if it already is).
            EnsureOllamaServer(exe);

            SetLocal($"Downloading model {_settings.Model} (~1 GB, one time)…");
            var pull = await RunProcessAsync(exe, $"pull {_settings.Model}",
                line => { if (!string.IsNullOrWhiteSpace(line)) SetLocal($"Downloading… {line}"); });
            if (pull != 0)
            {
                SetLocal($"Model download failed (exit {pull}). Check your connection and try again.");
                return;
            }

            _settings.Provider = "ollama";
            _settings.SetupComplete = true;
            _settings.Save();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetLocal($"Setup error: {ex.Message}");
        }
        finally
        {
            LocalBar.IsIndeterminate = false;
            LocalBar.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void EnsureOllamaServer(string exe)
    {
        try
        {
            // Fire-and-forget; if a server is already running this exits quickly.
            Process.Start(new ProcessStartInfo(exe, "serve")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { /* best-effort */ }
    }

    /// <summary>Locates ollama.exe on PATH, or at its default install location.</summary>
    private static string? ResolveOllamaExe()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ollama", "--version")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is not null) { p.WaitForExit(4000); if (p.HasExited && p.ExitCode == 0) return "ollama"; }
        }
        catch { /* not on PATH */ }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe");
        return File.Exists(local) ? local : null;
    }

    private static async Task<int> RunProcessAsync(string exe, string args, Action<string> onLine)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, ev) => { if (ev.Data is { } d) onLine(d); };
        p.ErrorDataReceived += (_, ev) => { if (ev.Data is { } d) onLine(d); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    private void SetLocal(string text) =>
        Dispatcher.Invoke(() => LocalStatus.Text = text);

    private void SetBusy(bool on)
    {
        _busy = on;
        ContinueBtn.IsEnabled = !on;
        TestBtn.IsEnabled = !on;
        CloudCard.IsEnabled = !on;
        LocalCard.IsEnabled = !on;
    }
}
