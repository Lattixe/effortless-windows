using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Effortless.ViewModels;

namespace Effortless.Views;

public partial class TimerWidget : Window, INotifyPropertyChanged
{
    private readonly TaskViewModel _viewModel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimerWidget(TaskViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = this;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Position in top-right corner of primary screen
        Loaded += (_, _) => PositionWindow();
    }

    public string TaskName
    {
        get
        {
            var task = _viewModel.CurrentTask;
            return task?.DisplayName ?? "No tasks";
        }
    }

    public string TimerDisplay
    {
        get => _viewModel.FormattedTime;
    }

    public Visibility TimerVisibility
    {
        get => _viewModel.CurrentTask?.HasDuration == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public Visibility PauseVisibility
    {
        get => _viewModel.IsPaused && _viewModel.CurrentTask?.HasDuration == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TaskViewModel.CurrentTask):
            case nameof(TaskViewModel.IncompleteTasks):
                OnPropertyChanged(nameof(TaskName));
                OnPropertyChanged(nameof(TimerVisibility));
                OnPropertyChanged(nameof(PauseVisibility));
                break;

            case nameof(TaskViewModel.RemainingTime):
                OnPropertyChanged(nameof(TimerDisplay));
                break;

            case nameof(TaskViewModel.IsPaused):
                OnPropertyChanged(nameof(PauseVisibility));
                break;
        }
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 10;
        Top = workArea.Top + 10;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle task list - handled by App.xaml.cs
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            DragMove();
        }
    }

    public event EventHandler? DoubleClicked;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
