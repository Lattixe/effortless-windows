using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Effortless.Models;
using Effortless.Services;
using Effortless.ViewModels;

namespace Effortless.Views;

public partial class ScratchPadWindow : System.Windows.Window, INotifyPropertyChanged
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 40;
    private const double DefaultFontSize = 14;

    private readonly TaskViewModel? _viewModel;
    private readonly DispatcherTimer _statusTimer;
    private VaultWindow? _vaultWindow;
    private AskClaudeWindow? _askWindow;
    private string _noteText = string.Empty;
    private double _editorFontSize = DefaultFontSize;

    // Content snapshot of the last vaulted/loaded state, used to detect whether
    // the pad holds genuinely-unsaved edits (so we never create duplicate
    // thoughts when simply navigating the vault).
    private string _vaultedSnapshot = string.Empty;

    // The vault thought the pad is currently an editing session of (loaded from
    // the vault, or matched on startup). When set, ⬇ Vault updates that note in
    // place instead of creating a new one.
    private VaultThought? _loadedThought;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? ThoughtVaulted;

    public ScratchPadWindow(TaskViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = this;

        // Load saved note
        _noteText = StorageService.LoadScratchPad();
        OnPropertyChanged(nameof(NoteText));

        // Restore the saved editor font size
        _editorFontSize = Math.Clamp(StorageService.LoadScratchPadFontSize(), MinFontSize, MaxFontSize);
        OnPropertyChanged(nameof(EditorFontSize));

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = string.Empty;
        };

        // Focus editor when loaded
        Loaded += (_, _) =>
        {
            NoteEditor.Focus();
            UpdateThemeGlyph();
        };

        // Keep the toggle glyph in sync when the theme changes from anywhere.
        ThemeService.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;

        // Save on every text change. We read NoteEditor.Text directly rather
        // than _noteText so we never depend on the binding push order.
        NoteEditor.TextChanged += (_, _) => StorageService.SaveScratchPad(NoteEditor.Text);

        // If what we just loaded from disk happens to match a vaulted thought
        // exactly, mark it as already-vaulted so the next ⬇ Vault doesn't
        // create a duplicate. Handles the load-thought → close-app → reopen →
        // hit-Vault sequence.
        if (!string.IsNullOrWhiteSpace(_noteText))
        {
            try
            {
                var snapshot = _noteText.Trim();
                var match = StorageService.LoadVaultThoughts()
                    .FirstOrDefault(t => t.Content.Trim() == snapshot);
                if (match != null)
                {
                    _vaultedSnapshot = _noteText;
                    _loadedThought = match;
                }
            }
            catch
            {
                // best effort — duplicate detection is a UX nicety
            }
        }
    }

    // Segoe Fluent Icons / MDL2 Assets glyphs (private-use codepoints).
    private const string SunGlyph = "\uE706";   // Brightness — sun with rays
    private const string MoonGlyph = "\uE793";  // ClearNight — crescent moon

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeGlyph();

    private void UpdateThemeGlyph()
    {
        // Show the destination state: sun when currently dark (click for
        // light), moon when currently light (click for dark).
        ThemeToggleGlyph.Text = ThemeService.IsDark ? SunGlyph : MoonGlyph;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        ShowStatus(ThemeService.IsDark ? "Dark mode" : "Light mode");
    }

    public string NoteText
    {
        get => _noteText;
        set
        {
            if (_noteText != value)
            {
                _noteText = value;
                OnPropertyChanged(nameof(NoteText));
            }
        }
    }

    public double EditorFontSize
    {
        get => _editorFontSize;
        set
        {
            var clamped = Math.Clamp(value, MinFontSize, MaxFontSize);
            if (Math.Abs(_editorFontSize - clamped) > 0.01)
            {
                _editorFontSize = clamped;
                OnPropertyChanged(nameof(EditorFontSize));
                StorageService.SaveScratchPadFontSize(clamped);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void VaultButton_Click(object sender, RoutedEventArgs e)
    {
        VaultPad(NoteEditor.Text, explicitTitle: null);
        NoteEditor.Focus();
    }

    private string? _askSnapshotDir;

    private void AskButton_Click(object sender, RoutedEventArgs e)
    {
        // Fresh conversation each time so it reflects the current pad content.
        if (_askWindow != null)
        {
            _askWindow.Close();
            _askWindow = null;
        }
        CleanupAskSnapshot();

        // Snapshot the pad to a fresh per-Ask folder and run the provider
        // there. Letting it read scratch-pad.md via its own Read tool is far
        // more reliable than stuffing the pad's contents into the prompt — the
        // prompt stays small (so it can't time out on big pads), and pad text
        // that looks like slash commands ("/read 30") never enters the
        // prompt-parsing path.
        //
        // The folder lives under the user profile (NOT %TEMP%, which is under
        // AppData where Claude's project init misbehaves).
        var dir = Path.Combine(
            StorageService.GetAskWorkspaceRoot(),
            "pad-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scratch-pad.md"), NoteEditor.Text ?? string.Empty);
            _askSnapshotDir = dir;
        }
        catch
        {
            ShowStatus("Couldn't snapshot pad for Ask", isError: true);
            return;
        }

        const string directive =
            "The file `scratch-pad.md` in your working directory is the user's current " +
            "scratch pad. Read it before answering anything about \"the scratch pad\", " +
            "\"my notes\", or \"this pad\".";

        _askWindow = new AskClaudeWindow("scratch pad", dir, directive);
        _askWindow.Closed += (_, _) =>
        {
            _askWindow = null;
            CleanupAskSnapshot();
        };
        _askWindow.Show();
        _askWindow.Activate();
    }

    private void CleanupAskSnapshot()
    {
        if (_askSnapshotDir is null) return;
        try { Directory.Delete(_askSnapshotDir, recursive: true); } catch { /* best effort */ }
        _askSnapshotDir = null;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e) => OpenVaultBrowser();

    public void OpenVaultBrowser()
    {
        _vaultWindow ??= new VaultWindow(this);
        _vaultWindow.RefreshThoughts();
        _vaultWindow.Show();
        _vaultWindow.Activate();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = StorageService.GetVaultFolder();
            Directory.CreateDirectory(folder);
            StorageService.EnsureVaultReadme();
            StorageService.RegenerateVaultIndex();
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch
        {
            ShowStatus("Couldn't open vault folder", isError: true);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl +/- to resize the editor font, Ctrl+0 to reset.
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.OemPlus:
                case Key.Add:
                    EditorFontSize += 1;
                    ShowStatus($"Font size {EditorFontSize:0}");
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    EditorFontSize -= 1;
                    ShowStatus($"Font size {EditorFontSize:0}");
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    EditorFontSize = DefaultFontSize;
                    ShowStatus($"Font size {EditorFontSize:0}");
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && TryExecuteSlashCommand())
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Vaults the current pad content. Invoked by the Shift+Alt+V global hotkey;
    /// surfaces a tray notification when the pad isn't on screen to give feedback.
    /// </summary>
    public void VaultCurrentThought()
    {
        var wasVisible = IsVisible;
        var thought = VaultPad(NoteEditor.Text, explicitTitle: null);
        if (thought != null && !wasVisible)
            ThoughtVaulted?.Invoke(this, thought.Title);
    }

    private bool TryExecuteSlashCommand()
    {
        var text = NoteEditor.Text;
        var caret = NoteEditor.CaretIndex;

        var lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = text.Length;

        var line = text.Substring(lineStart, lineEnd - lineStart);
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("/"))
            return false;

        var command = trimmed.Substring(1).Trim();
        if (string.IsNullOrWhiteSpace(command))
            return false;

        // /vault [optional title] — archive the whole pad as a thought, start fresh.
        if (command.Equals("vault", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("vault ", StringComparison.OrdinalIgnoreCase))
        {
            var title = command.Length > 5 ? command[5..].Trim() : null;

            // Vault everything except the /vault command line itself.
            var before = text[..lineStart];
            var after = lineEnd < text.Length ? text[(lineEnd + 1)..] : string.Empty;
            var content = (before + after).Trim();

            VaultPad(content, string.IsNullOrWhiteSpace(title) ? null : title);
            return true;
        }

        // /daily — append the whole pad as a timestamped entry in today's daily
        // note, then start fresh (the running-log counterpart to /vault).
        if (command.Equals("daily", StringComparison.OrdinalIgnoreCase))
        {
            var before = text[..lineStart];
            var after = lineEnd < text.Length ? text[(lineEnd + 1)..] : string.Empty;
            var content = (before + after).Trim();

            DailyLogPad(content);
            return true;
        }

        // Otherwise: create a task (e.g. "/read 30") and leave a markdown record.
        if (_viewModel == null)
            return false;

        _viewModel.AddTask(command);

        var indent = line[..(line.Length - trimmed.Length)];
        var replacement = $"{indent}- [ ] {command}\n";
        NoteEditor.Text = text.Remove(lineStart, lineEnd - lineStart).Insert(lineStart, replacement);
        NoteEditor.CaretIndex = lineStart + replacement.Length;

        return true;
    }

    private VaultThought? VaultPad(string content, string? explicitTitle)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowStatus("Nothing to vault", isError: true);
            return null;
        }

        var keepingTitle = string.IsNullOrWhiteSpace(explicitTitle);

        // Unchanged since it was loaded/saved — just clear, don't write again.
        if (keepingTitle &&
            !string.IsNullOrWhiteSpace(_vaultedSnapshot) &&
            content.Trim() == _vaultedSnapshot.Trim())
        {
            ResetPad();
            _vaultWindow?.RefreshThoughts();
            ShowStatus("Already vaulted · pad cleared");
            return null;
        }

        VaultThought? thought;
        var updated = false;

        // If the pad is an editing session of a vaulted thought (and the user
        // didn't force a new title via /vault <title>), update that note in
        // place instead of creating a duplicate.
        if (_loadedThought != null && keepingTitle)
        {
            thought = StorageService.UpdateThought(_loadedThought, content);
            updated = thought != null;
        }
        else
        {
            thought = StorageService.SaveThought(content, explicitTitle);
        }

        if (thought == null)
        {
            ShowStatus("Couldn't vault thought", isError: true);
            return null;
        }

        ResetPad();
        _vaultWindow?.RefreshThoughts();
        ShowStatus(updated ? $"Updated: {thought.Title}" : $"Vaulted: {thought.Title}");
        return thought;
    }

    // Clear the pad and forget which thought it came from (fresh start).
    private void ResetPad()
    {
        NoteEditor.Clear();
        StorageService.SaveScratchPad(string.Empty);
        _vaultedSnapshot = string.Empty;
        _loadedThought = null;
    }

    // Append the pad to today's daily note, then start fresh.
    private void DailyLogPad(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowStatus("Nothing to log", isError: true);
            return;
        }

        var file = StorageService.AppendToDailyNote(content);
        if (file == null)
        {
            ShowStatus("Couldn't write daily note", isError: true);
            return;
        }

        ResetPad();
        _vaultWindow?.RefreshThoughts();
        ShowStatus($"Logged to {System.IO.Path.GetFileNameWithoutExtension(file)}");
    }

    /// <summary>
    /// Loads a vaulted thought back into the pad. Genuinely-unsaved edits are
    /// committed first so nothing is lost: edits to a previously-loaded thought
    /// update it in place, while fresh content is stashed as a new thought.
    /// </summary>
    public void LoadThoughtIntoPad(VaultThought thought)
    {
        var current = NoteEditor.Text;
        string? committed = null;
        var target = thought;   // the thought whose content we'll actually load

        var hasUnsavedEdits = !string.IsNullOrWhiteSpace(current)
            && current.Trim() != _vaultedSnapshot.Trim()
            && current.Trim() != thought.Content.Trim();

        if (hasUnsavedEdits)
        {
            if (_loadedThought != null)
            {
                // Persist the edits to the thought we were editing.
                if (StorageService.UpdateThought(_loadedThought, current) is { } u)
                {
                    if (_loadedThought.Id == thought.Id)
                        target = u;                       // reloading the same note — keep edits
                    else
                        committed = $"saved “{u.Title}”"; // switching notes — note we saved the old one
                }
            }
            else if (StorageService.SaveThought(current) != null)
            {
                committed = "stashed current";
            }
        }

        NoteEditor.Text = target.Content.TrimEnd();
        NoteEditor.CaretIndex = NoteEditor.Text.Length;
        StorageService.SaveScratchPad(NoteEditor.Text);
        _vaultedSnapshot = NoteEditor.Text;
        _loadedThought = target;

        _vaultWindow?.RefreshThoughts();
        ShowStatus(committed != null ? $"{committed} · loaded: {target.Title}" : $"Loaded: {target.Title}");

        Show();
        Activate();
        NoteEditor.Focus();
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x53, 0x50))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Hide instead of close, save content
        e.Cancel = true;
        StorageService.SaveScratchPad(NoteEditor.Text);
        Hide();
    }

    private void NoteEditor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (NoteEditor.Template.FindName("PART_ContentHost", NoteEditor) is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
