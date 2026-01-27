using System.ComponentModel;
using System.Windows;
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

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
