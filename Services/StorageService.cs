using System.IO;
using System.Text.Json;
using Effortless.Models;

namespace Effortless.Services;

public static class StorageService
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Effortless"
    );

    private static readonly string TasksFile = Path.Combine(DataFolder, "tasks.json");
    private static readonly string ScratchPadFile = Path.Combine(DataFolder, "scratchpad.txt");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static List<TaskItem> LoadTasks()
    {
        try
        {
            if (!File.Exists(TasksFile))
                return new List<TaskItem>();

            var json = File.ReadAllText(TasksFile);
            return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
        }
        catch
        {
            return new List<TaskItem>();
        }
    }

    public static void SaveTasks(List<TaskItem> tasks)
    {
        try
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);

            var json = JsonSerializer.Serialize(tasks, JsonOptions);
            File.WriteAllText(TasksFile, json);
        }
        catch
        {
            // Silently fail - don't crash the app for storage issues
        }
    }

    public static string LoadScratchPad()
    {
        try
        {
            if (!File.Exists(ScratchPadFile))
                return string.Empty;

            return File.ReadAllText(ScratchPadFile);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void SaveScratchPad(string content)
    {
        try
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);

            File.WriteAllText(ScratchPadFile, content);
        }
        catch
        {
            // Silently fail
        }
    }
}
