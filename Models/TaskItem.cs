using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Effortless.Models;

public partial class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RawInput { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool HasDuration => Duration > TimeSpan.Zero;

    [JsonIgnore]
    public int DurationMinutes => (int)Duration.TotalMinutes;

    public static TaskItem Parse(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new TaskItem
            {
                RawInput = input,
                DisplayName = string.Empty,
                Duration = TimeSpan.Zero
            };
        }

        // Pattern: anything followed by whitespace and a number at the end
        // e.g., "Deep work 45" -> name="Deep work", duration=45 minutes
        var match = TaskDurationRegex().Match(trimmed);

        if (match.Success)
        {
            var name = match.Groups[1].Value.Trim();
            var minutes = int.Parse(match.Groups[2].Value);

            return new TaskItem
            {
                RawInput = input,
                DisplayName = name,
                Duration = TimeSpan.FromMinutes(minutes)
            };
        }

        // No duration specified
        return new TaskItem
        {
            RawInput = input,
            DisplayName = trimmed,
            Duration = TimeSpan.Zero
        };
    }

    public TaskItem Clone()
    {
        return new TaskItem
        {
            Id = Id,
            RawInput = RawInput,
            DisplayName = DisplayName,
            Duration = Duration,
            IsCompleted = IsCompleted,
            CreatedAt = CreatedAt
        };
    }

    [GeneratedRegex(@"^(.+?)\s+(\d+)$")]
    private static partial Regex TaskDurationRegex();
}
