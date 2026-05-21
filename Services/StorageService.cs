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
    private static readonly string ScratchPadFolder = Path.Combine(DataFolder, "ScratchPad");
    private static readonly string ScratchPadFile = Path.Combine(ScratchPadFolder, "current.md");
    private static readonly string LegacyScratchPadFile = Path.Combine(DataFolder, "scratchpad.txt");

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
            if (File.Exists(ScratchPadFile))
                return File.ReadAllText(ScratchPadFile);

            // One-time migration from legacy scratchpad.txt
            if (File.Exists(LegacyScratchPadFile))
            {
                var legacy = File.ReadAllText(LegacyScratchPadFile);
                SaveScratchPad(legacy);
                try { File.Delete(LegacyScratchPadFile); } catch { }
                return legacy;
            }

            return string.Empty;
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
            if (!Directory.Exists(ScratchPadFolder))
                Directory.CreateDirectory(ScratchPadFolder);

            File.WriteAllText(ScratchPadFile, content);
        }
        catch
        {
            // Silently fail
        }
    }
}
