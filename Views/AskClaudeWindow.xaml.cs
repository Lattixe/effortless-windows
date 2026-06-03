using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Effortless.Models;
using Effortless.Services;

namespace Effortless.Views;

public partial class AskClaudeWindow : System.Windows.Window
{
    private readonly string _workingDir;
    private readonly string? _inlineContext;
    private readonly string _contextLabel;
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private List<AskProvider> _providers = new();
    private AskProvider? _provider;

    private CancellationTokenSource? _cts;
    private bool _firstSent;
    private bool _busy;
    private bool _userStopped;
    private bool _suppressProviderSwitch;

    /// <param name="scopeLabel">e.g. "scratch pad" or "vault" (shown in the title).</param>
    /// <param name="workingDir">Directory the CLI runs in (its read scope).</param>
    /// <param name="inlineContext">Optional text included with the first question.</param>
    public AskClaudeWindow(string scopeLabel, string workingDir, string? inlineContext = null)
    {
        InitializeComponent();
        _contextLabel = scopeLabel;
        _workingDir = workingDir;
        _inlineContext = inlineContext;

        ScopeText.Text = $"· {scopeLabel}";

        LoadProviders();
        UpdateTranscriptHeader();

        Loaded += (_, _) => PromptBox.Focus();
    }

    private void LoadProviders()
    {
        _suppressProviderSwitch = true;
        try
        {
            _providers = AskProvider.AllConfigured();
            ProviderCombo.ItemsSource = _providers;

            if (_providers.Count == 0)
            {
                ProviderCombo.IsEnabled = false;
                _provider = null;
                return;
            }

            var lastId = StorageService.LoadSettings().LastAskProvider;
            _provider = _providers.FirstOrDefault(p => p.Id == lastId) ?? _providers[0];
            ProviderCombo.SelectedItem = _provider;
        }
        finally
        {
            _suppressProviderSwitch = false;
        }
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProviderSwitch) return;
        if (ProviderCombo.SelectedItem is not AskProvider next) return;
        if (next.Id == _provider?.Id) return;

        // Switching mid-conversation means we lose multi-turn continuity with
        // the previous provider. Start a fresh session.
        _provider = next;
        _firstSent = false;
        UpdateTranscriptHeader();
        PersistLastProvider();
    }

    private void PersistLastProvider()
    {
        if (_provider == null) return;
        var settings = StorageService.LoadSettings();
        if (settings.LastAskProvider == _provider.Id) return;
        settings.LastAskProvider = _provider.Id;
        StorageService.SaveSettings(settings);
    }

    private void UpdateTranscriptHeader()
    {
        var name = _provider?.Label ?? "no provider";
        Transcript.Text =
            $"Ask {name} anything about your {_contextLabel}. " +
            $"{name} can read your notes (read-only) and answer here.\n\n";
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

        if (_provider == null)
        {
            AppendTurn("⚠", "No AI provider is configured. Set ClaudeCommand or HermesCommand in settings.json.");
            return;
        }

        var question = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(question))
            return;

        // Prepend the first-turn prefix (e.g. a directive that tells the CLI
        // where to find the user's notes) only on the first turn — later
        // turns resume the session and already have that context.
        var payload = question;
        if (!_firstSent && !string.IsNullOrWhiteSpace(_inlineContext))
            payload = $"{_inlineContext}\n\n{question}";

        AppendTurn("You", question);
        PromptBox.Clear();
        SetBusy(true);
        _userStopped = false;

        AskResult result;
        _cts = new CancellationTokenSource();
        _cts.CancelAfter(TimeSpan.FromSeconds(150));
        try
        {
            result = await AskService.AskAsync(
                _provider, payload, _workingDir, _sessionId, _firstSent, _cts.Token);
        }
        catch (Exception ex)
        {
            result = new AskResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        if (result.Success)
        {
            AppendTurn(_provider.Label, result.Text);
            _firstSent = true;
        }
        else if (result.Cancelled)
        {
            AppendTurn("⚠", _userStopped
                ? "Stopped."
                : $"Timed out — no response from {_provider.Label}. " +
                  $"Run `{_provider.Command} {_provider.Args}` in a terminal to check it works.");
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
        AskBtn.IsEnabled = !busy && _provider != null;
        PromptBox.IsEnabled = !busy;
        ProviderCombo.IsEnabled = !busy && _providers.Count > 1;
        StopBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = busy
            ? $"Thinking… ({_provider?.Label ?? "?"})"
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
