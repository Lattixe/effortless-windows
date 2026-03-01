using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Effortless.Services;

namespace Effortless.Views;

public partial class ScratchPadWindow : System.Windows.Window, INotifyPropertyChanged
{
    private string _noteText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScratchPadWindow()
    {
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
        // Only handle Escape key, let all other keys pass through to the TextBox
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
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
