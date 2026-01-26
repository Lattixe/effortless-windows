using Effortless.Services;
using Effortless.ViewModels;
using Effortless.Views;
using System.Windows;

namespace Effortless;

public partial class App : System.Windows.Application
{
    private TaskViewModel? _viewModel;
    private TrayIconService? _trayService;
    private HotkeyService? _hotkeyService;
    private TaskListWindow? _taskListWindow;
    private ScratchPadWindow? _scratchPadWindow;
    private TimerWidget? _timerWidget;
    private Window? _hiddenWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the shared view model
        _viewModel = new TaskViewModel();

        // Create a hidden window for hotkey message handling
        _hiddenWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        _hiddenWindow.Show();
        _hiddenWindow.Hide();

        // Create task list window (hidden initially)
        _taskListWindow = new TaskListWindow(_viewModel);

        // Create scratch pad window (hidden initially)
        _scratchPadWindow = new ScratchPadWindow();

        // Create timer widget (always visible)
        _timerWidget = new TimerWidget(_viewModel);
        _timerWidget.DoubleClicked += (_, _) => ToggleTaskList();
        _timerWidget.Show();

        // Setup tray icon
        _trayService = new TrayIconService(_viewModel);
        _trayService.ToggleListRequested += (_, _) => ToggleTaskList();
        _trayService.ToggleScratchPadRequested += (_, _) => ToggleScratchPad();
        _trayService.ExitRequested += (_, _) => Shutdown();

        // Setup global hotkeys
        _hotkeyService = new HotkeyService();
        _hotkeyService.Initialize(_hiddenWindow);
        _hotkeyService.MarkDonePressed += (_, _) => _viewModel.MarkCurrentTaskDone();
        _hotkeyService.AddTimePressed += (_, _) => _viewModel.AddTime(5);
        _hotkeyService.PausePressed += (_, _) => _viewModel.TogglePause();
        _hotkeyService.ToggleListPressed += (_, _) => ToggleTaskList();
        _hotkeyService.ToggleScratchPadPressed += (_, _) => ToggleScratchPad();
    }

    private void ToggleTaskList()
    {
        if (_taskListWindow == null)
            return;

        if (_taskListWindow.IsVisible)
        {
            _taskListWindow.Hide();
        }
        else
        {
            _taskListWindow.Show();
            _taskListWindow.Activate();
        }
    }

    private void ToggleScratchPad()
    {
        if (_scratchPadWindow == null)
            return;

        if (_scratchPadWindow.IsVisible)
        {
            _scratchPadWindow.Hide();
        }
        else
        {
            _scratchPadWindow.Show();
            _scratchPadWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _timerWidget?.Close();
        _scratchPadWindow?.Close();
        _taskListWindow?.Close();
        _hiddenWindow?.Close();
        base.OnExit(e);
    }
}
