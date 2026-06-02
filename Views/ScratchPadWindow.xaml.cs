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
    private readonly TaskViewModel? _viewModel;
    private readonly DispatcherTimer _statusTimer;
    private VaultWindow? _vaultWindow;
    private string _noteText = string.Empty;

    // Content snapshot of the last vaulted/loaded state, used to detect whether
    // the pad holds genuinely-unsaved edits (so we never create duplicate
    // thoughts when simply navigating the vault).
    private string _vaultedSnapshot = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScratchPadWindow(TaskViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = this;

        // Load saved note
        _noteText = StorageService.LoadScratchPad();
        OnPropertyChanged(nameof(NoteText));

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
        };

        // Save on text change
        NoteEditor.TextChanged += (_, _) => StorageService.SaveScratchPad(_noteText);
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

    private void VaultPad(string content, string? explicitTitle)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowStatus("Nothing to vault", isError: true);
            return;
        }

        var thought = StorageService.SaveThought(content, explicitTitle);
        if (thought == null)
        {
            ShowStatus("Couldn't vault thought", isError: true);
            return;
        }

        // Start fresh
        NoteEditor.Clear();
        StorageService.SaveScratchPad(string.Empty);
        _vaultedSnapshot = string.Empty;

        _vaultWindow?.RefreshThoughts();
        ShowStatus($"Vaulted: {thought.Title}");
    }

    /// <summary>
    /// Loads a vaulted thought back into the pad. Genuinely-unsaved edits in the
    /// pad are stashed to the vault first so nothing is ever lost — but simply
    /// navigating between thoughts never creates duplicates.
    /// </summary>
    public void LoadThoughtIntoPad(VaultThought thought)
    {
        var current = NoteEditor.Text;
        var stashed = false;

        var hasUnsavedEdits = !string.IsNullOrWhiteSpace(current)
            && current.Trim() != _vaultedSnapshot.Trim()
            && current.Trim() != thought.Content.Trim();

        if (hasUnsavedEdits)
            stashed = StorageService.SaveThought(current) != null;

        NoteEditor.Text = thought.Content.TrimEnd();
        NoteEditor.CaretIndex = NoteEditor.Text.Length;
        StorageService.SaveScratchPad(NoteEditor.Text);
        _vaultedSnapshot = NoteEditor.Text;

        _vaultWindow?.RefreshThoughts();
        ShowStatus(stashed ? $"Stashed current · loaded: {thought.Title}" : $"Loaded: {thought.Title}");

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
        StorageService.SaveScratchPad(_noteText);
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
