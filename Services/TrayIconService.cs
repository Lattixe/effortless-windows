using System.Drawing;
using System.Windows.Forms;
using Effortless.ViewModels;
using Application = System.Windows.Application;

namespace Effortless.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TaskViewModel _viewModel;
    private readonly ContextMenuStrip _contextMenu;
    private bool _disposed;

    public event EventHandler? ToggleListRequested;
    public event EventHandler? ToggleScratchPadRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(TaskViewModel viewModel)
    {
        _viewModel = viewModel;

        _contextMenu = CreateContextMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "Effortless",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // Create a simple icon programmatically
        _notifyIcon.Icon = CreateIcon();

        _notifyIcon.DoubleClick += (_, _) => ToggleListRequested?.Invoke(this, EventArgs.Empty);

        // Subscribe to view model changes
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.TimerCompleted += ViewModel_TimerCompleted;

        UpdateTooltip();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Toggle Task List (Ctrl+Alt+L)");
        toggleItem.Click += (_, _) => ToggleListRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(toggleItem);

        var scratchPadItem = new ToolStripMenuItem("Toggle Scratch Pad (Ctrl+Alt+P)");
        scratchPadItem.Click += (_, _) => ToggleScratchPadRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(scratchPadItem);

        menu.Items.Add(new ToolStripSeparator());

        var doneItem = new ToolStripMenuItem("Mark Done (Ctrl+Alt+D)");
        doneItem.Click += (_, _) => _viewModel.MarkCurrentTaskDone();
        menu.Items.Add(doneItem);

        var addTimeItem = new ToolStripMenuItem("Add 5 Minutes (Ctrl+Alt+R)");
        addTimeItem.Click += (_, _) => _viewModel.AddTime(5);
        menu.Items.Add(addTimeItem);

        var pauseItem = new ToolStripMenuItem("Pause/Resume (Ctrl+Alt+Space)");
        pauseItem.Click += (_, _) => _viewModel.TogglePause();
        menu.Items.Add(pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskViewModel.MenuBarTitle))
        {
            UpdateTooltip();
        }
    }

    private void ViewModel_TimerCompleted(object? sender, string taskName)
    {
        ShowBalloon("Timer Complete", $"'{taskName}' timer has finished!");
    }

    private void UpdateTooltip()
    {
        var title = _viewModel.MenuBarTitle;
        // NotifyIcon.Text has a 64 character limit
        _notifyIcon.Text = title.Length > 63 ? title[..63] : title;
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    private static Icon CreateIcon()
    {
        // Create a simple clock-like icon
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Fill with transparent
        g.Clear(Color.Transparent);

        // Draw circle (clock face)
        using var brush = new SolidBrush(Color.FromArgb(100, 149, 237)); // Cornflower blue
        g.FillEllipse(brush, 2, 2, 28, 28);

        // Draw clock hands
        using var pen = new Pen(Color.White, 2);
        // Hour hand
        g.DrawLine(pen, 16, 16, 16, 8);
        // Minute hand
        g.DrawLine(pen, 16, 16, 22, 16);

        // Convert to icon
        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.TimerCompleted -= ViewModel_TimerCompleted;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
