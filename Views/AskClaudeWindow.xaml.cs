using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Effortless.Services;

namespace Effortless.Views;

public partial class AskClaudeWindow : System.Windows.Window
{
    private readonly string _workingDir;
    private readonly string? _inlineContext;
    private readonly string _contextLabel;
    private readonly string _sessionId = Guid.NewGuid().ToString();

    private CancellationTokenSource? _cts;
    private bool _firstSent;
    private bool _busy;
    private bool _userStopped;

    /// <param name="scopeLabel">e.g. "scratch pad" or "vault" (shown in the title).</param>
    /// <param name="workingDir">Directory Claude runs in (its read scope).</param>
    /// <param name="inlineContext">Optional text included with the first question.</param>
    public AskClaudeWindow(string scopeLabel, string workingDir, string? inlineContext = null)
    {
        InitializeComponent();
        _contextLabel = scopeLabel;
        _workingDir = workingDir;
        _inlineContext = inlineContext;

        ScopeText.Text = $"· {scopeLabel}";
        Transcript.Text =
            $"Ask anything about your {scopeLabel}. Claude can read your notes (read-only) and answer here.\n\n";

        Loaded += (_, _) => PromptBox.Focus();
    }

    private async void AskButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _userStopped = true;
        _cts?.Cancel();
    }

    private async void PromptBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (_busy)
            return;

        var question = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(question))
            return;

        // Include the live note text with the first question only; subsequent
        // turns resume the session, which already has it.
        var payload = question;
        if (!_firstSent && !string.IsNullOrWhiteSpace(_inlineContext))
            payload = $"{question}\n\n----- {_contextLabel} contents -----\n{_inlineContext}";

        AppendTurn("You", question);
        PromptBox.Clear();
        SetBusy(true);
        _userStopped = false;

        ClaudeResult result;
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(150));
        try
        {
            result = await ClaudeService.AskAsync(payload, _workingDir, _sessionId, _firstSent, _cts.Token);
        }
        catch (Exception ex)
        {
            result = new ClaudeResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        if (result.Success)
        {
            AppendTurn("Claude", result.Text);
            _firstSent = true;
        }
        else if (result.Cancelled)
        {
            AppendTurn("⚠", _userStopped
                ? "Stopped."
                : "Timed out — no response from claude. Run `claude -p \"hi\"` once in a terminal to confirm it's installed and signed in.");
        }
        else
        {
            AppendTurn("⚠", result.Error);
        }

        SetBusy(false);
        PromptBox.Focus();
    }

    private void AppendTurn(string role, string text)
    {
        Transcript.AppendText($"{role}\n{text}\n\n");
        Transcript.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        AskBtn.IsEnabled = !busy;
        PromptBox.IsEnabled = !busy;
        StopBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = busy
            ? "Thinking…"
            : "Enter to ask · Shift+Enter for a new line · read-only";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Abort any in-flight query so the CLI process is killed.
        _cts?.Cancel();
    }
}
