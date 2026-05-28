using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Effortless.Services;
using Effortless.ViewModels;

namespace Effortless.Views;

public partial class ScratchPadWindow : System.Windows.Window, INotifyPropertyChanged
{
    private readonly TaskViewModel? _viewModel;
    private string _noteText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScratchPadWindow(TaskViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = this;

        // Load saved note
        _noteText = StorageService.LoadScratchPad();
        OnPropertyChanged(nameof(NoteText));

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
        if (_viewModel == null)
            return false;

        var text = NoteEditor.Text;
        var caret = NoteEditor.CaretIndex;

        var lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = text.Length;

        var line = text.Substring(lineStart, lineEnd - lineStart);
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("/"))
            return false;

        var taskInput = trimmed.Substring(1).Trim();
        if (string.IsNullOrWhiteSpace(taskInput))
            return false;

        _viewModel.AddTask(taskInput);

        var indent = line.Substring(0, line.Length - trimmed.Length);
        var replacement = $"{indent}- [ ] {taskInput}\n";
        NoteEditor.Text = text.Remove(lineStart, lineEnd - lineStart).Insert(lineStart, replacement);
        NoteEditor.CaretIndex = lineStart + replacement.Length;

        return true;
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
