using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Effortless.Models;
using Effortless.Services;
using Effortless.ViewModels;
// Disambiguate WPF types from the WinForms/System.Drawing globals.
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;

namespace Effortless.Views;

public partial class ScratchPadWindow : System.Windows.Window, INotifyPropertyChanged
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 40;
    private const double DefaultFontSize = 14;

    // Segoe Fluent Icons / MDL2 Assets glyphs (private-use codepoints).
    private const string SunGlyph = "\uE706";   // Brightness — sun with rays
    private const string MoonGlyph = "\uE793";  // ClearNight — crescent moon

    private readonly TaskViewModel? _viewModel;
    private readonly DispatcherTimer _statusTimer;
    private VaultWindow? _vaultWindow;
    private AskClaudeWindow? _askWindow;
    private double _editorFontSize = DefaultFontSize;
    private bool _suppressSave;

    // Content snapshot of the last vaulted/loaded state (in serialized-markdown
    // form), used to detect genuinely-unsaved edits so we never duplicate.
    private string _vaultedSnapshot = string.Empty;

    // The vault thought the pad is currently an editing session of. When set,
    // ⬇ Vault updates that note in place instead of creating a new one.
    private VaultThought? _loadedThought;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? ThoughtVaulted;

    public ScratchPadWindow(TaskViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = this;

        // Restore the saved editor font size before loading the document.
        _editorFontSize = Math.Clamp(StorageService.LoadScratchPadFontSize(), MinFontSize, MaxFontSize);
        OnPropertyChanged(nameof(EditorFontSize));

        // Route clicks that land on a task checkbox to the checkbox itself —
        // an editable RichTextBox otherwise just selects the embedded control.
        // handledEventsToo so it runs even though the editor handles the event.
        RichEditor.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RichEditor_PreviewMouseDown),
            handledEventsToo: true);

        // Load saved note into the rich editor.
        var markdown = StorageService.LoadScratchPad();
        SetMarkdown(markdown);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = string.Empty;
        };

        Loaded += (_, _) =>
        {
            RichEditor.Focus();
            UpdateThemeMenu();
            TitleDisplay.Text = ComputeTitle();
        };

        ThemeService.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;

        // If the loaded note matches a vaulted thought exactly, treat it as an
        // editing session of that thought so ⬇ Vault updates rather than dupes.
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            try
            {
                var snapshot = GetMarkdown().Trim();
                var match = StorageService.LoadVaultThoughts()
                    .FirstOrDefault(t => t.Content.Trim() == snapshot);
                if (match != null)
                {
                    _vaultedSnapshot = GetMarkdown();
                    _loadedThought = match;
                }
            }
            catch
            {
                // best effort — duplicate detection is a UX nicety
            }
        }
    }

    // ----------------------------------------------------- markdown <-> editor

    private string GetMarkdown() => MarkdownFlow.ToMarkdown(RichEditor.Document);

    private void SetMarkdown(string markdown)
    {
        _suppressSave = true;
        try
        {
            RichEditor.Document = MarkdownFlow.ToFlowDocument(markdown);
        }
        finally
        {
            _suppressSave = false;
        }
    }

    private void SaveNow() => StorageService.SaveScratchPad(GetMarkdown());

    private void RichEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSave) return;
        TitleDisplay.Text = ComputeTitle();
        SaveNow();
    }

    // Toggle a task by clicking its ☐/☑ glyph. Tasks are plain text glyphs (not
    // embedded controls), so we hit-test the click against the glyph's rect.
    private void RichEditor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var pt = e.GetPosition(RichEditor);
            if (RichEditor.GetPositionFromPoint(pt, true)?.Paragraph is not { } para)
                return;
            if (!MarkdownFlow.TryGetTaskGlyph(para, out _, out var isChecked))
                return;

            // Only toggle when the click lands on the glyph at line start.
            var glyphRect = para.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (glyphRect.IsEmpty) return;
            if (pt.Y < glyphRect.Top - 2 || pt.Y > glyphRect.Bottom + 2) return;
            if (pt.X > glyphRect.Right + 8) return; // clicked into the text → normal caret

            _suppressSave = true;
            try { MarkdownFlow.SetTaskCompletedVisual(para, !isChecked); }
            finally { _suppressSave = false; }
            e.Handled = true;
            SaveNow();
        }
        catch
        {
            // A stray click must never crash the pad.
        }
    }

    // ----------------------------------------------------- theme toggle

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeMenu();

    private void UpdateThemeMenu()
    {
        if (ThemeMenuItem != null)
            ThemeMenuItem.Header = ThemeService.IsDark ? "Switch to light" : "Switch to dark";
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        ShowStatus(ThemeService.IsDark ? "Dark mode" : "Light mode");
    }

    // ----------------------------------------------------- title (Google-Docs style)

    /// <summary>The displayed/edited title: the document's first non-empty line.</summary>
    private string ComputeTitle()
    {
        if (RichEditor.Document?.Blocks.FirstBlock is not Paragraph p)
            return "Untitled";

        var line = new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();
        if (line.StartsWith("- [ ]", StringComparison.Ordinal)) line = line[5..].Trim();
        else if (line.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) line = line[5..].Trim();

        if (string.IsNullOrWhiteSpace(line)) return "Untitled";
        if (line.Length > 80) line = line[..80].TrimEnd() + "…";
        return line;
    }

    private void TitleDisplay_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        BeginTitleEdit();
    }

    private void BeginTitleEdit()
    {
        var current = ComputeTitle();
        TitleEdit.Text = current == "Untitled" ? string.Empty : current;
        TitleDisplay.Visibility = Visibility.Collapsed;
        TitleEdit.Visibility = Visibility.Visible;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
    }

    private void TitleEdit_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            e.Handled = true;
            EndTitleEdit(commit: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndTitleEdit(commit: false);
        }
    }

    private void TitleEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (TitleEdit.Visibility == Visibility.Visible)
            EndTitleEdit(commit: true);
    }

    private void EndTitleEdit(bool commit)
    {
        if (commit)
        {
            var newTitle = TitleEdit.Text.Trim();
            SetFirstLineText(newTitle);
            TitleDisplay.Text = ComputeTitle();
            SaveNow();
        }
        TitleEdit.Visibility = Visibility.Collapsed;
        TitleDisplay.Visibility = Visibility.Visible;
        RichEditor.Focus();
    }

    /// <summary>Replace the first paragraph's text with <paramref name="text"/>,
    /// preserving a leading task glyph if there was one. Creates the paragraph
    /// if the document is empty.</summary>
    private void SetFirstLineText(string text)
    {
        _suppressSave = true;
        try
        {
            if (RichEditor.Document.Blocks.FirstBlock is not Paragraph p)
            {
                p = new Paragraph { Margin = new Thickness(0) };
                if (RichEditor.Document.Blocks.FirstBlock is { } existing)
                    RichEditor.Document.Blocks.InsertBefore(existing, p);
                else
                    RichEditor.Document.Blocks.Add(p);
            }

            bool? wasChecked = MarkdownFlow.TryGetTaskGlyph(p, out _, out var isChecked)
                ? isChecked
                : null;

            p.Inlines.Clear();
            if (wasChecked.HasValue)
                p.Inlines.Add(MarkdownFlow.MakeTaskGlyphRun(wasChecked.Value));
            p.Inlines.Add(new Run(text));
        }
        finally
        {
            _suppressSave = false;
        }
    }

    // ----------------------------------------------------- floating selection popup

    private void RichEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var sel = RichEditor.Selection;
        if (sel == null || sel.IsEmpty)
        {
            SelectionToolbar.IsOpen = false;
            return;
        }

        try
        {
            var rect = sel.Start.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
            {
                SelectionToolbar.IsOpen = false;
                return;
            }

            // Position the popup just above the selection's start, centered-ish.
            var screen = RichEditor.PointToScreen(new Point(rect.Left, rect.Top));
            SelectionToolbar.HorizontalOffset = screen.X - 40;
            SelectionToolbar.VerticalOffset = screen.Y - 42;
            if (!SelectionToolbar.IsOpen)
                SelectionToolbar.IsOpen = true;
        }
        catch
        {
            SelectionToolbar.IsOpen = false;
        }
    }

    // ----------------------------------------------------- hamburger menu

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = (Button)sender;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
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

    // ----------------------------------------------------- formatting toolbar

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, RichEditor);
        RichEditor.Focus();
        SaveNow();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, RichEditor);
        RichEditor.Focus();
        SaveNow();
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string hex)
            return;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            RichEditor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
            RichEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, MarkdownFlow.HighlightForegroundBrush);
        }
        catch { /* ignore bad color */ }

        RichEditor.Focus();
        SaveNow();
    }

    private void ClearHighlight_Click(object sender, RoutedEventArgs e)
    {
        var fg = (TryFindResource("TextPrimaryBrush") as Brush) ?? RichEditor.Foreground;
        RichEditor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
        RichEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, fg);
        RichEditor.Focus();
        SaveNow();
    }

    private void Task_Click(object sender, RoutedEventArgs e)
    {
        if (RichEditor.CaretPosition?.Paragraph is not { } para)
            return;

        try
        {
            if (MarkdownFlow.TryGetTaskGlyph(para, out _, out _))
                MarkdownFlow.RemoveTaskGlyph(para);
            else
                MarkdownFlow.AddTaskGlyph(para);
        }
        catch { /* never crash on a formatting toggle */ }

        RichEditor.Focus();
        SaveNow();
    }

    // ----------------------------------------------------- title-bar actions

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void VaultButton_Click(object sender, RoutedEventArgs e)
    {
        VaultPad(GetMarkdown(), explicitTitle: null);
        RichEditor.Focus();
    }

    private string? _askSnapshotDir;

    private void AskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_askWindow != null)
        {
            _askWindow.Close();
            _askWindow = null;
        }
        CleanupAskSnapshot();

        // Snapshot the pad (as markdown) to a fresh per-Ask folder and run the
        // provider there. Reading scratch-pad.md via the CLI's own Read tool is
        // more reliable than stuffing contents into the prompt. The folder lives
        // under the user profile (NOT %TEMP%, which is under AppData where
        // Claude's project init misbehaves).
        var dir = Path.Combine(
            StorageService.GetAskWorkspaceRoot(),
            "pad-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scratch-pad.md"), GetMarkdown());
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
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch
        {
            ShowStatus("Couldn't open vault folder", isError: true);
        }
    }

    // ----------------------------------------------------- keyboard

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

        // "- " or "* " at the start of a line → turn the line into a task.
        if (e.Key == Key.Space && TryConvertDashToTask())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (TryExecuteSlashCommand()) { e.Handled = true; return; }
            if (TryContinueTaskList()) { e.Handled = true; return; }
        }
    }

    /// <summary>Typing a space after a lone leading "-"/"*" converts the line to a task.</summary>
    private bool TryConvertDashToTask()
    {
        try
        {
            if (RichEditor.CaretPosition is not { } caret) return false;
            if (caret.Paragraph is not { } para) return false;
            if (MarkdownFlow.TryGetTaskGlyph(para, out _, out _)) return false; // already a task

            var before = new TextRange(para.ContentStart, caret).Text.TrimStart();
            if (before != "-" && before != "*") return false;

            var after = new TextRange(caret, para.ContentEnd).Text;
            para.Inlines.Clear();
            para.Inlines.Add(MarkdownFlow.MakeTaskGlyphRun(false));
            if (after.Length > 0)
                para.Inlines.Add(new Run(after));
            RichEditor.CaretPosition = para.ContentEnd;
            SaveNow();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Enter inside a task line continues the list; on an empty task it exits.</summary>
    private bool TryContinueTaskList()
    {
        try
        {
            if (RichEditor.CaretPosition?.Paragraph is not { } para) return false;
            if (!MarkdownFlow.TryGetTaskGlyph(para, out _, out _)) return false;

            // Content = the line's text minus the leading glyph (structure-agnostic,
            // so it's correct even if WPF merged the copy into the glyph's run).
            var line = new TextRange(para.ContentStart, para.ContentEnd).Text;
            var content = MarkdownFlow.StripGlyphPrefix(line);

            if (string.IsNullOrWhiteSpace(content))
            {
                // Empty task + Enter → exit the list (plain line).
                MarkdownFlow.RemoveTaskGlyph(para);
                RichEditor.CaretPosition = para.ContentStart;
                SaveNow();
                return true;
            }

            var next = new Paragraph { Margin = new Thickness(0) };
            next.Inlines.Add(MarkdownFlow.MakeTaskGlyphRun(false));
            RichEditor.Document.Blocks.InsertAfter(para, next);
            RichEditor.CaretPosition = next.ContentEnd;
            SaveNow();
            return true;
        }
        catch
        {
            return false; // fall back to the editor's default newline
        }
    }

    /// <summary>
    /// Vaults the current pad content. Invoked by the Shift+Alt+V global hotkey;
    /// surfaces a tray notification when the pad isn't on screen to give feedback.
    /// </summary>
    public void VaultCurrentThought()
    {
        var wasVisible = IsVisible;
        var thought = VaultPad(GetMarkdown(), explicitTitle: null);
        if (thought != null && !wasVisible)
            ThoughtVaulted?.Invoke(this, thought.Title);
    }

    private bool TryExecuteSlashCommand()
    {
        if (RichEditor.CaretPosition?.Paragraph is not { } para)
            return false;

        var line = new TextRange(para.ContentStart, para.ContentEnd).Text;
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
            var content = MarkdownFlow.ToMarkdown(RichEditor.Document, para).Trim();
            VaultPad(content, string.IsNullOrWhiteSpace(title) ? null : title);
            return true;
        }

        // /daily — append the whole pad to today's daily note, then start fresh.
        if (command.Equals("daily", StringComparison.OrdinalIgnoreCase))
        {
            var content = MarkdownFlow.ToMarkdown(RichEditor.Document, para).Trim();
            DailyLogPad(content);
            return true;
        }

        // Otherwise: create a task (e.g. "/read 30") and leave a checkbox record.
        if (_viewModel == null)
            return false;

        _viewModel.AddTask(command);

        // Turn the command line into a task, then drop to a fresh line.
        para.Inlines.Clear();
        para.Inlines.Add(MarkdownFlow.MakeTaskGlyphRun(false));
        para.Inlines.Add(new Run(command));

        var next = new Paragraph { Margin = new Thickness(0) };
        RichEditor.Document.Blocks.InsertAfter(para, next);
        RichEditor.CaretPosition = next.ContentStart;

        SaveNow();
        return true;
    }

    // ----------------------------------------------------- vault / daily

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
        SetMarkdown(string.Empty);
        StorageService.SaveScratchPad(string.Empty);
        _vaultedSnapshot = string.Empty;
        _loadedThought = null;
    }

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
        ShowStatus($"Logged to {Path.GetFileNameWithoutExtension(file)}");
    }

    /// <summary>
    /// Loads a vaulted thought back into the pad. Genuinely-unsaved edits are
    /// committed first so nothing is lost: edits to a previously-loaded thought
    /// update it in place, while fresh content is stashed as a new thought.
    /// </summary>
    public void LoadThoughtIntoPad(VaultThought thought)
    {
        var current = GetMarkdown();
        string? committed = null;
        var target = thought;

        var hasUnsavedEdits = !string.IsNullOrWhiteSpace(current)
            && current.Trim() != _vaultedSnapshot.Trim()
            && current.Trim() != thought.Content.Trim();

        if (hasUnsavedEdits)
        {
            if (_loadedThought != null)
            {
                if (StorageService.UpdateThought(_loadedThought, current) is { } u)
                {
                    if (_loadedThought.Id == thought.Id)
                        target = u;
                    else
                        committed = $"saved “{u.Title}”";
                }
            }
            else if (StorageService.SaveThought(current) != null)
            {
                committed = "stashed current";
            }
        }

        SetMarkdown(target.Content.TrimEnd());
        StorageService.SaveScratchPad(GetMarkdown());
        _vaultedSnapshot = GetMarkdown();
        _loadedThought = target;
        RichEditor.CaretPosition = RichEditor.Document.ContentEnd;

        _vaultWindow?.RefreshThoughts();
        ShowStatus(committed != null ? $"{committed} · loaded: {target.Title}" : $"Loaded: {target.Title}");

        Show();
        Activate();
        RichEditor.Focus();
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
            : new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        SaveNow();
        Hide();
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
