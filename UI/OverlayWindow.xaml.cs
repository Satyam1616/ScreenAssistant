using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Animation;
using ScreenAssistant.Config;
using ScreenAssistant.Models;
using ScreenAssistant.Services;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace ScreenAssistant.UI;

public partial class OverlayWindow : Window
{
    private readonly CaptureService _capture;
    private readonly OcrService _ocr;
    private readonly LlmService _llm;
    private readonly Settings _settings;

    // The running conversation, bound to the transcript. Index 0 is the system prompt.
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    private CancellationTokenSource? _cts;

    public OverlayWindow(CaptureService capture, OcrService ocr, LlmService llm, Settings settings)
    {
        InitializeComponent();
        _capture = capture;
        _ocr = ocr;
        _llm = llm;
        _settings = settings;
        Transcript.ItemsSource = Messages;
        NewChat();
    }

    private void NewChat()
    {
        _cts?.Cancel();
        Messages.Clear();
        Messages.Add(new ChatMessage { Role = "system", Content = _settings.SystemPrompt });
        SetThinking(false);
        ShowStatus("New chat — press Ctrl+Space to capture the screen");
        UpdateTurnLabel();
    }

    /// <summary>Global hotkey: capture the current screen into the chat.</summary>
    public async void OnHotkey() => await CaptureAsync();

    private async Task CaptureAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Hide so we never OCR our own overlay, then let the compositor catch up.
        // Only wait for the frame if we were actually on screen.
        bool wasVisible = Visibility == Visibility.Visible;
        Visibility = Visibility.Hidden;
        if (wasVisible) await Task.Delay(70, CancellationToken.None);

        System.Drawing.Bitmap bmp;
        try { bmp = _capture.CaptureVirtualScreen(_settings.MaxCaptureEdge); }
        catch (Exception ex) { ShowError($"Capture failed: {ex.Message}"); return; }

        PositionTopRight();
        Reveal();
        ShowStatus("Reading screen…");

        string ocrText;
        try { ocrText = await _ocr.RecognizeAsync(bmp); }
        catch (Exception ex) { ShowError(ex.Message); return; }
        finally { bmp.Dispose(); }

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            ShowStatus("No text found on screen. Try again or press Enter to ask.");
            return;
        }

        Messages.Add(new ChatMessage
        {
            Role = "user",
            IsCapture = true,
            Content = $"New screen capture (OCR):\n{ocrText}\n\n" +
                      "Answer any question on this screen; otherwise briefly say what's on it.",
            Display = $"📷 Screen captured · {DateTime.Now:HH:mm:ss}",
        });
        ScrollToEnd();
        await StreamAssistantAsync(ct);
    }

    // Ask a typed question in the current chat (screen context is already there).
    private async Task AskAsync(string question)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Messages.Add(new ChatMessage
        {
            Role = "user",
            Content = question,
            Display = $"🧑 {question}",
        });
        ScrollToEnd();
        await StreamAssistantAsync(_cts.Token);
    }

    private async Task StreamAssistantAsync(CancellationToken ct)
    {
        // Empty placeholder for the streamed reply (bubble stays hidden until the
        // first token arrives; the animated dots show progress meanwhile).
        var reply = new ChatMessage { Role = "assistant", Content = "" };
        Messages.Add(reply);
        var toSend = BuildContext();

        SetThinking(true);
        ShowStatus("Thinking…");
        UpdateTurnLabel();
        try
        {
            await _llm.GenerateAsync(toSend, token =>
            {
                if (ct.IsCancellationRequested) return;
                reply.Content += token;
                ScrollToEnd();
            }, ct);
            if (!ct.IsCancellationRequested)
            {
                var model = _llm.EffectiveModel ?? _settings.Model;
                SetThinking(false);
                ShowStatus($"Ready · {model} · Ctrl+Space capture · Enter ask · N new");
            }
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch (Exception ex)
        {
            reply.Content = $"[error] {ex.Message}";
            SetThinking(false);
            ShowStatus("Error");
        }
    }

    private void SetThinking(bool on) =>
        TypingDots.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Keeps the model's prompt small for speed: the system prompt, then every
    /// turn from the most recent screen capture onward (the current screen and
    /// its follow-ups). Older captures are dropped — the newest screen is the
    /// one being asked about. The trailing empty assistant placeholder is excluded.
    /// </summary>
    private List<ChatMessage> BuildContext()
    {
        int end = Messages.Count - 1;                 // exclude placeholder
        int lastCapture = -1;
        for (int i = end - 1; i >= 1; i--)
            if (Messages[i].IsCapture) { lastCapture = i; break; }

        var ctx = new List<ChatMessage>();
        if (Messages.Count > 0) ctx.Add(Messages[0]); // system prompt
        int start = lastCapture >= 1 ? lastCapture : 1;
        for (int i = start; i < end; i++) ctx.Add(Messages[i]);
        return ctx;
    }

    private void ScrollToEnd()
    {
        // Let the new item lay out first, then pin to the bottom.
        Dispatcher.BeginInvoke(new Action(() => AnswerScroll.ScrollToBottom()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Reveal()
    {
        Visibility = Visibility.Visible;
        Show();
        Activate();
        Focus();
        // Subtle fade + rise so it appears smoothly.
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void ShowStatus(string s) => StatusText.Text = s;

    private void ShowError(string message)
    {
        SetThinking(false);
        if (Visibility != Visibility.Visible) { PositionTopRight(); Reveal(); }
        StatusText.Text = "Error";
        Messages.Add(new ChatMessage { Role = "assistant", Content = message });
        ScrollToEnd();
    }

    private void UpdateTurnLabel()
    {
        int turns = Messages.Count(m => m.Role == "user");
        TurnText.Text = turns == 0 ? "" : $"{turns} turn{(turns == 1 ? "" : "s")}";
    }

    private void PositionTopRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Top + 24;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // While typing a custom question, only Enter/Esc are special.
        if (QuestionHost.Visibility == Visibility.Visible && QuestionBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter)
            {
                var q = QuestionBox.Text.Trim();
                CloseQuestionBox();
                if (q.Length > 0) _ = AskAsync(q);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseQuestionBox();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Up: AnswerScroll.LineUp(); e.Handled = true; break;
            case Key.Down: AnswerScroll.LineDown(); e.Handled = true; break;
            case Key.Left: AnswerScroll.PageUp(); e.Handled = true; break;
            case Key.Right: AnswerScroll.PageDown(); e.Handled = true; break;
            case Key.Enter:
                QuestionHost.Visibility = Visibility.Visible;
                QuestionBox.Focus();
                e.Handled = true;
                break;
            case Key.N:
                NewChat();
                e.Handled = true;
                break;
            case Key.C:
                var last = Messages.LastOrDefault(m => m.Role == "assistant");
                if (last is not null) try { Clipboard.SetText(last.Content); } catch { }
                e.Handled = true;
                break;
            case Key.Escape:
                Visibility = Visibility.Hidden;
                Hide();
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    private void CloseQuestionBox()
    {
        QuestionHost.Visibility = Visibility.Collapsed;
        QuestionBox.Clear();
        Focus();
    }
}
