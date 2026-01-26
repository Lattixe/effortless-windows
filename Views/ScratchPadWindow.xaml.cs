using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Effortless.Services;

namespace Effortless.Views;

public partial class ScratchPadWindow : System.Windows.Window, INotifyPropertyChanged
{
    private string _noteText = string.Empty;
    private bool _isPinned = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScratchPadWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Load saved note
        _noteText = StorageService.LoadScratchPad();
        OnPropertyChanged(nameof(NoteText));

        // Position in bottom-right corner
        Loaded += (_, _) =>
        {
            PositionWindow();
            UpdatePinVisual();
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

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 20;
        Top = workArea.Bottom - ActualHeight - 60;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        UpdatePinVisual();
    }

    private void UpdatePinVisual()
    {
        if (PinIcon != null)
        {
            // Bright when pinned, dim when unpinned
            PinIcon.Opacity = _isPinned ? 1.0 : 0.4;
        }
        if (PinButton != null)
        {
            PinButton.ToolTip = _isPinned ? "Unpin (always on top)" : "Pin (always on top)";
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
