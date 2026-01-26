using System.ComponentModel;
using System.Windows.Input;
using Effortless.ViewModels;

namespace Effortless.Views;

public partial class TaskListWindow : System.Windows.Window, INotifyPropertyChanged
{
    private readonly TaskViewModel _viewModel;
    private string _taskText = string.Empty;
    private bool _isUpdatingText;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TaskListWindow(TaskViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = this;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Load initial text
        _taskText = _viewModel.GetTextFromTasks();
        OnPropertyChanged(nameof(TaskText));

        // Focus the editor
        Loaded += (_, _) => TaskEditor.Focus();
    }

    public string TaskText
    {
        get => _taskText;
        set
        {
            if (_taskText != value)
            {
                _taskText = value;
                OnPropertyChanged(nameof(TaskText));
            }
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TaskViewModel.CurrentTask):
            case nameof(TaskViewModel.IncompleteTasks):
                // Sync text if tasks changed externally
                if (!_isUpdatingText)
                {
                    var newText = _viewModel.GetTextFromTasks();
                    if (newText != _taskText)
                    {
                        _taskText = newText;
                        OnPropertyChanged(nameof(TaskText));
                    }
                }
                break;
        }
    }

    private void TaskEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _isUpdatingText = true;
        try
        {
            _viewModel.SyncTasksFromText(_taskText);
        }
        finally
        {
            _isUpdatingText = false;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
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
        // Hide instead of close
        e.Cancel = true;
        Hide();
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
