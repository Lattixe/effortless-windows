using System.Collections.ObjectModel;
using System.Media;
using System.Windows.Threading;
using Effortless.Models;
using Effortless.Services;

namespace Effortless.ViewModels;

public class TaskViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private TimeSpan _remainingTime;
    private bool _isPaused;
    private Guid? _currentTaskId;
    private bool _hasPlayedSound;
    private bool _isEditing;
    private bool _timerStarted; // Timer only starts after task list is closed

    public TaskViewModel()
    {
        Tasks = new ObservableCollection<TaskItem>(StorageService.LoadTasks());

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // Initialize timer display for current task (but don't start counting until edited)
        if (CurrentTask is { } task)
        {
            _currentTaskId = task.Id;
            _remainingTime = task.Duration;
        }
        _timerStarted = false; // Don't auto-start on app launch - wait for first edit
    }

    public ObservableCollection<TaskItem> Tasks { get; }

    public IEnumerable<TaskItem> IncompleteTasks => Tasks.Where(t => !t.IsCompleted);
    public IEnumerable<TaskItem> CompletedTasks => Tasks.Where(t => t.IsCompleted);
    public TaskItem? CurrentTask => IncompleteTasks.FirstOrDefault();

    public TimeSpan RemainingTime
    {
        get => _remainingTime;
        private set
        {
            if (SetProperty(ref _remainingTime, value))
            {
                OnPropertyChanged(nameof(FormattedTime));
                OnPropertyChanged(nameof(MenuBarTitle));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(MenuBarTitle));
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            var wasEditing = _isEditing;
            if (SetProperty(ref _isEditing, value))
            {
                // Start timer when editing ends (task list closes)
                if (wasEditing && !value)
                {
                    _timerStarted = true;
                }
            }
        }
    }

    public string FormattedTime
    {
        get
        {
            var totalSeconds = (int)RemainingTime.TotalSeconds;
            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }
    }

    public string MenuBarTitle
    {
        get
        {
            var task = CurrentTask;
            if (task == null)
                return "Effortless";

            if (!task.HasDuration)
                return task.DisplayName;

            var pauseIndicator = IsPaused ? " ⏸" : "";
            return $"{task.DisplayName} {FormattedTime}{pauseIndicator}";
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var current = CurrentTask;

        // Check if current task changed
        if (current?.Id != _currentTaskId)
        {
            _currentTaskId = current?.Id;
            _remainingTime = current?.Duration ?? TimeSpan.Zero;
            _isPaused = false;
            _hasPlayedSound = false;
            OnPropertyChanged(nameof(MenuBarTitle));
            return;
        }

        if (IsPaused || IsEditing || !_timerStarted || current == null || !current.HasDuration)
            return;

        if (RemainingTime > TimeSpan.Zero)
        {
            RemainingTime = RemainingTime.Subtract(TimeSpan.FromSeconds(1));
        }

        // Timer completed - auto-advance to next task
        if (RemainingTime <= TimeSpan.Zero && !_hasPlayedSound)
        {
            _hasPlayedSound = true;
            PlayNotificationSound();
            ShowNotification(current.DisplayName);

            // Mark current task as done and move to next
            MarkCurrentTaskDone();
        }
    }

    public void AddTask(string input)
    {
        var task = TaskItem.Parse(input);
        if (string.IsNullOrWhiteSpace(task.DisplayName))
            return;

        // Insert before completed tasks
        var insertIndex = Tasks.Count(t => !t.IsCompleted);
        Tasks.Insert(insertIndex, task);
        _timerStarted = true;
        SaveTasks();
        NotifyTaskListChanged();
    }

    public void MarkCurrentTaskDone()
    {
        var task = CurrentTask;
        if (task == null)
            return;

        var index = Tasks.IndexOf(task);
        if (index >= 0)
        {
            task.IsCompleted = true;
            // Move to end
            Tasks.RemoveAt(index);
            Tasks.Add(task);
            SaveTasks();
            NotifyTaskListChanged();
        }
    }

    public void SelectTask(TaskItem task)
    {
        if (task.IsCompleted)
            return;

        var index = Tasks.IndexOf(task);
        if (index <= 0)
            return;

        // Move to front
        Tasks.RemoveAt(index);
        Tasks.Insert(0, task);

        // Reset timer for new current task
        _currentTaskId = task.Id;
        _remainingTime = task.Duration;
        _isPaused = false;
        _hasPlayedSound = false;

        SaveTasks();
        NotifyTaskListChanged();
    }

    public void DeleteTask(TaskItem task)
    {
        Tasks.Remove(task);
        SaveTasks();
        NotifyTaskListChanged();
    }

    public void TogglePause()
    {
        if (CurrentTask?.HasDuration != true)
            return;

        IsPaused = !IsPaused;
    }

    public void AddTime(int minutes)
    {
        if (CurrentTask?.HasDuration != true)
            return;

        RemainingTime = RemainingTime.Add(TimeSpan.FromMinutes(minutes));
        _hasPlayedSound = false; // Reset sound flag since we added time
    }

    public void ClearCompleted()
    {
        var completed = Tasks.Where(t => t.IsCompleted).ToList();
        foreach (var task in completed)
        {
            Tasks.Remove(task);
        }
        SaveTasks();
        NotifyTaskListChanged();
    }

    public void ClearAll()
    {
        Tasks.Clear();
        SaveTasks();
        NotifyTaskListChanged();
    }

    public void SyncTasksFromText(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.None);
        var newIncompleteTasks = new List<TaskItem>();
        var existingIncomplete = IncompleteTasks.ToList();

        var lineIndex = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parsed = TaskItem.Parse(line);
            if (string.IsNullOrWhiteSpace(parsed.DisplayName))
                continue;

            // Preserve ID by position - if we have an existing task at this position, update it
            if (lineIndex < existingIncomplete.Count)
            {
                var existing = existingIncomplete[lineIndex];
                existing.RawInput = line;
                existing.DisplayName = parsed.DisplayName;
                existing.Duration = parsed.Duration;
                newIncompleteTasks.Add(existing);
            }
            else
            {
                // New task added at the end
                newIncompleteTasks.Add(parsed);
            }
            lineIndex++;
        }

        // Rebuild task list: new incomplete + completed
        var completed = CompletedTasks.ToList();
        Tasks.Clear();
        foreach (var task in newIncompleteTasks)
            Tasks.Add(task);
        foreach (var task in completed)
            Tasks.Add(task);

        // Update timer display while editing
        var newCurrent = CurrentTask;
        if (newCurrent?.Id != _currentTaskId)
        {
            _currentTaskId = newCurrent?.Id;
            _remainingTime = newCurrent?.Duration ?? TimeSpan.Zero;
            _isPaused = false;
            _hasPlayedSound = false;
        }
        else if (IsEditing && newCurrent != null)
        {
            // While editing, always sync the displayed time with the parsed duration
            _remainingTime = newCurrent.Duration;
            OnPropertyChanged(nameof(RemainingTime));
            OnPropertyChanged(nameof(FormattedTime));
        }

        SaveTasks();
        NotifyTaskListChanged();
    }

    public string GetTextFromTasks()
    {
        return string.Join("\n", IncompleteTasks.Select(t => t.RawInput));
    }

    private void SaveTasks()
    {
        StorageService.SaveTasks(Tasks.ToList());
    }

    private void NotifyTaskListChanged()
    {
        OnPropertyChanged(nameof(IncompleteTasks));
        OnPropertyChanged(nameof(CompletedTasks));
        OnPropertyChanged(nameof(CurrentTask));
        OnPropertyChanged(nameof(MenuBarTitle));
    }

    private void PlayNotificationSound()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // Ignore sound errors
        }
    }

    private void ShowNotification(string taskName)
    {
        // Notification will be handled by the tray icon
        TimerCompleted?.Invoke(this, taskName);
    }

    public event EventHandler<string>? TimerCompleted;
}
