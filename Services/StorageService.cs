using System.IO;
using System.Linq;
using System.Text;
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
    private static readonly string SettingsFile = Path.Combine(DataFolder, "settings.json");
    private static readonly string ScratchPadFolder = Path.Combine(DataFolder, "ScratchPad");
    private static readonly string ScratchPadFile = Path.Combine(ScratchPadFolder, "current.md");
    private static readonly string LegacyScratchPadFile = Path.Combine(DataFolder, "scratchpad.txt");

    // The vault lives in Documents so it is easy to find, sync (OneDrive/Dropbox),
    // back up, and point an AI agent at for "prompt against my vault" workflows.
    private static readonly string VaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Effortless Vault"
    );

    private static readonly string VaultThoughtsFolder = Path.Combine(VaultFolder, "thoughts");
    private static readonly string VaultDailyFolder = Path.Combine(VaultFolder, "daily");
    private static readonly string VaultIndexFile = Path.Combine(VaultFolder, "index.md");
    private static readonly string VaultReadmeFile = Path.Combine(VaultFolder, "README.md");

    // Obsidian-style inline hashtags: #tag, #nested/tag — must start with a
    // letter (so markdown headers "# " and pure numbers don't match) and be
    // preceded by whitespace or line start.
    private static readonly System.Text.RegularExpressions.Regex HashtagPattern =
        new(@"(?<!\S)#([A-Za-z][\w/-]*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

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

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);

            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Silently fail
        }
    }

    public static double LoadScratchPadFontSize() => LoadSettings().ScratchPadFontSize;

    public static void SaveScratchPadFontSize(double size)
    {
        var settings = LoadSettings();
        settings.ScratchPadFontSize = size;
        SaveSettings(settings);
    }

    // ---------------------------------------------------------------------
    // Thought Vault
    //
    // An agent-native markdown knowledge base. Each captured thought becomes a
    // standalone .md file with YAML frontmatter under Documents/Effortless Vault/
    // thoughts/, and a wiki-style index.md is regenerated on every change.
    // ---------------------------------------------------------------------

    public static string GetVaultFolder() => VaultFolder;

    public static string GetVaultIndexFile() => VaultIndexFile;

    public static string GetVaultDailyFolder() => VaultDailyFolder;

    /// <summary>Pull Obsidian-style #hashtags out of note content, de-duplicated.</summary>
    private static List<string> ExtractTags(string content)
    {
        var tags = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in HashtagPattern.Matches(content))
        {
            var tag = m.Groups[1].Value;
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }
        return tags;
    }

    /// <summary>
    /// Append a timestamped entry to today's daily note (daily/YYYY-MM-DD.md),
    /// the Obsidian daily-note convention. Creates the file with frontmatter on
    /// first write of the day. Returns the file path, or null if nothing/failed.
    /// </summary>
    public static string? AppendToDailyNote(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            Directory.CreateDirectory(VaultDailyFolder);
            var now = DateTime.Now;
            var file = Path.Combine(VaultDailyFolder, $"{now:yyyy-MM-dd}.md");
            var body = content.Replace("\r\n", "\n").TrimEnd();

            var sb = new StringBuilder();
            if (!File.Exists(file))
            {
                sb.Append("---\n");
                sb.Append($"date: {now:yyyy-MM-dd}\n");
                sb.Append("type: daily\n");
                sb.Append("tags: [daily]\n");
                sb.Append("---\n\n");
                sb.Append($"# {now:dddd, MMMM d, yyyy}\n");
            }
            sb.Append($"\n## {now:HH:mm}\n\n{body}\n");

            File.AppendAllText(file, sb.ToString());
            EnsureVaultReadme();
            RegenerateVaultIndex();
            return file;
        }
        catch
        {
            return null;
        }
    }

    public static string GetScratchPadFolder() => ScratchPadFolder;

    /// <summary>
    /// Root for transient per-Ask working folders. Lives under the user profile
    /// (e.g. C:\Users\me\.effortless\ask) rather than %TEMP% — CLIs like Claude
    /// Code misbehave when their working directory is under hidden AppData.
    /// </summary>
    public static string GetAskWorkspaceRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".effortless", "ask");
        try { Directory.CreateDirectory(root); } catch { /* best effort */ }
        return root;
    }

    /// <summary>
    /// Persist the given content as a new thought and refresh the vault index.
    /// Returns the created thought (with FilePath populated), or null when the
    /// content is empty.
    /// </summary>
    public static VaultThought? SaveThought(string content, string? explicitTitle = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            Directory.CreateDirectory(VaultThoughtsFolder);

            var title = !string.IsNullOrWhiteSpace(explicitTitle)
                ? explicitTitle!.Trim()
                : DeriveTitle(content);

            var thought = new VaultThought
            {
                Title = title,
                Created = DateTime.Now,
                Tags = ExtractTags(content),
                Content = content.Replace("\r\n", "\n").TrimEnd() + "\n"
            };

            var fileName = BuildUniqueFileName(thought);
            thought.FilePath = Path.Combine(VaultThoughtsFolder, fileName);

            File.WriteAllText(thought.FilePath, SerializeThought(thought));

            EnsureVaultReadme();
            RegenerateVaultIndex();

            return thought;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Overwrites an existing thought's file in place with new content,
    /// preserving its Id, Created date, tags, and filename. The title in the
    /// frontmatter is re-derived from the new content. Returns the updated
    /// thought, or null on failure.
    /// </summary>
    public static VaultThought? UpdateThought(VaultThought existing, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            // If the underlying file vanished, fall back to creating a new one.
            if (string.IsNullOrEmpty(existing.FilePath) || !File.Exists(existing.FilePath))
                return SaveThought(content);

            var updated = new VaultThought
            {
                Id = existing.Id,
                Created = existing.Created,
                Tags = ExtractTags(content),
                Title = DeriveTitle(content),
                FilePath = existing.FilePath,
                Content = content.Replace("\r\n", "\n").TrimEnd() + "\n"
            };

            File.WriteAllText(updated.FilePath, SerializeThought(updated));
            RegenerateVaultIndex();
            return updated;
        }
        catch
        {
            return null;
        }
    }

    public static List<VaultThought> LoadVaultThoughts()
    {
        var result = new List<VaultThought>();
        try
        {
            if (!Directory.Exists(VaultThoughtsFolder))
                return result;

            foreach (var path in Directory.EnumerateFiles(VaultThoughtsFolder, "*.md"))
            {
                var thought = ParseThought(path);
                if (thought != null)
                    result.Add(thought);
            }
        }
        catch
        {
            // Return whatever parsed successfully
        }

        return result.OrderByDescending(t => t.Created).ToList();
    }

    public static bool DeleteThought(VaultThought thought)
    {
        try
        {
            if (File.Exists(thought.FilePath))
                File.Delete(thought.FilePath);
            RegenerateVaultIndex();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void RegenerateVaultIndex()
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            var thoughts = LoadVaultThoughts();

            var sb = new StringBuilder();
            sb.Append("# 🧠 Thought Vault\n\n");
            sb.Append($"> Auto-generated catalog — {thoughts.Count} thought(s) captured.\n");
            sb.Append($"> Last updated {DateTime.Now:yyyy-MM-dd HH:mm}.\n\n");
            sb.Append("Agent-native markdown vault — also an [Obsidian](https://obsidian.md) ");
            sb.Append("vault (open this folder in Obsidian). See [`README.md`](README.md) ");
            sb.Append("for how to query it. Links below are `[[wikilinks]]` so the graph view connects.\n");

            if (thoughts.Count == 0)
            {
                sb.Append("\n_No thoughts yet. Capture one from the Effortless scratch pad ");
                sb.Append("(type `/vault` or click **Vault**)._\n");
            }
            else
            {
                string? currentMonth = null;
                foreach (var t in thoughts)
                {
                    var month = t.Created.ToString("yyyy-MM");
                    if (month != currentMonth)
                    {
                        currentMonth = month;
                        sb.Append($"\n## {t.Created:MMMM yyyy}\n\n");
                    }

                    // Wikilink by basename (unique) with the title as display alias.
                    // Sanitise the alias: '|' and ']' would break the wikilink.
                    var name = Path.GetFileNameWithoutExtension(t.FilePath);
                    var alias = t.Title.Replace("|", "/").Replace("]", ")").Replace("[", "(");
                    var tagStr = t.Tags.Count > 0
                        ? "  " + string.Join(" ", t.Tags.Select(tag => $"`#{tag}`"))
                        : "";
                    sb.Append($"- **[[{name}|{alias}]]** — *{t.Created:yyyy-MM-dd}*{tagStr}\n");
                    if (!string.IsNullOrWhiteSpace(t.Preview))
                        sb.Append($"  - {t.Preview}\n");
                }
            }

            // Daily log section
            var dailies = Directory.Exists(VaultDailyFolder)
                ? Directory.EnumerateFiles(VaultDailyFolder, "*.md")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderByDescending(n => n, StringComparer.Ordinal)
                    .ToList()
                : new List<string?>();

            if (dailies.Count > 0)
            {
                sb.Append("\n## 📅 Daily log\n\n");
                foreach (var d in dailies)
                    sb.Append($"- [[{d}]]\n");
            }

            File.WriteAllText(VaultIndexFile, sb.ToString());
        }
        catch
        {
            // Silently fail
        }
    }

    public static void EnsureVaultReadme()
    {
        try
        {
            Directory.CreateDirectory(VaultFolder);
            if (File.Exists(VaultReadmeFile))
                return;
            File.WriteAllText(VaultReadmeFile, VaultReadmeContent());
        }
        catch
        {
            // Silently fail
        }
    }

    private static string DeriveTitle(string content)
    {
        var firstLine = content
            .Split('\n')
            .Select(l => l.Trim().TrimStart('#', '-', '*', '>', ' ').Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrWhiteSpace(firstLine))
            return "Untitled";

        return firstLine.Length > 80 ? firstLine[..80].Trim() : firstLine;
    }

    private static string BuildUniqueFileName(VaultThought thought)
    {
        var slug = Slugify(thought.Title);
        var baseName = $"{thought.Created:yyyy-MM-dd}-{slug}";
        var fileName = baseName + ".md";

        var counter = 2;
        while (File.Exists(Path.Combine(VaultThoughtsFolder, fileName)))
        {
            fileName = $"{baseName}-{counter}.md";
            counter++;
        }

        return fileName;
    }

    private static string Slugify(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '.')
                sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        if (slug.Length > 50)
            slug = slug[..50].Trim('-');

        return string.IsNullOrEmpty(slug) ? "thought" : slug;
    }

    private static string SerializeThought(VaultThought t)
    {
        var tags = t.Tags.Count > 0 ? string.Join(", ", t.Tags) : "";
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append($"title: {EscapeYaml(t.Title)}\n");
        sb.Append($"id: {t.Id}\n");
        sb.Append($"created: {t.Created:yyyy-MM-ddTHH:mm:ss}\n");
        sb.Append($"tags: [{tags}]\n");
        sb.Append("---\n\n");
        sb.Append(t.Content);
        return sb.ToString();
    }

    private static VaultThought? ParseThought(string path)
    {
        try
        {
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            var thought = new VaultThought
            {
                FilePath = path,
                Title = string.Empty,
                Created = File.GetCreationTime(path)
            };

            var body = text;
            if (text.StartsWith("---"))
            {
                var endIdx = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (endIdx > 0)
                {
                    var frontmatter = text.Substring(3, endIdx - 3);
                    body = text[(endIdx + 4)..].TrimStart('\n');
                    ParseFrontmatter(frontmatter, thought);
                }
            }

            thought.Content = body;
            if (string.IsNullOrWhiteSpace(thought.Title))
                thought.Title = Path.GetFileNameWithoutExtension(path);

            return thought;
        }
        catch
        {
            return null;
        }
    }

    private static void ParseFrontmatter(string frontmatter, VaultThought thought)
    {
        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim().ToLowerInvariant();
            var val = line[(colon + 1)..].Trim().Trim('"');

            switch (key)
            {
                case "title":
                    thought.Title = val;
                    break;
                case "id":
                    if (Guid.TryParse(val, out var id))
                        thought.Id = id;
                    break;
                case "created":
                    if (DateTime.TryParse(val, out var created))
                        thought.Created = created;
                    break;
                case "tags":
                    val = val.Trim('[', ']');
                    thought.Tags = val
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
            }
        }
    }

    private static string EscapeYaml(string value)
    {
        if (value.Contains(':') || value.Contains('"') || value.Contains('#') || value.Contains('\''))
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        return value;
    }

    private static string VaultReadmeContent()
    {
        return """
        # Effortless Thought Vault

        This folder is an **agent-native markdown knowledge base** AND an
        **Obsidian vault** — a personal wiki of captured thoughts. It's designed
        to be browsed by you (in Obsidian), and prompted against by AI agents
        (Claude Code, gbrain, etc.).

        ## Open it in Obsidian

        In Obsidian: **Open folder as vault** → pick this folder. Then:

        - **Graph view** lights up from the `[[wikilinks]]` in `index.md` (and any
          you add between notes).
        - **Tags pane** is populated from `#hashtags` in your notes and the
          `tags:` frontmatter.
        - Point the **Daily Notes** plugin at the `daily/` folder (format
          `YYYY-MM-DD`) to line up with the daily log Effortless writes.

        ## Structure

        ```
        Effortless Vault/
        ├── README.md          ← you are here
        ├── index.md           ← auto-generated map of content (wikilinks)
        ├── thoughts/
        │   └── YYYY-MM-DD-slug.md   ← one file per captured thought
        └── daily/
            └── YYYY-MM-DD.md         ← timestamped daily log (/daily)
        ```

        ## Thought format

        Every thought is a standalone markdown file with YAML frontmatter:

        ```markdown
        ---
        title: My Idea
        id: 00000000-0000-0000-0000-000000000000
        created: 2026-01-01T09:30:00
        tags: [ideas, product]
        ---

        The body of the thought. Use #hashtags and [[wikilinks]] freely —
        Effortless lifts #tags into the frontmatter, and Obsidian resolves both.
        ```

        - `index.md` is regenerated automatically — never edit it by hand.
        - Thought files are yours: refine titles, add `#tags`, link notes with
          `[[Other Note]]` to grow the graph.

        ## Capture vocabulary (from the Effortless scratch pad)

        - `/vault` — file the pad as a standalone thought in `thoughts/`.
        - `/daily` — append the pad as a timestamped entry in today's daily note.

        ## Prompting against the vault (agent workflow)

        Point an AI agent at this folder and ask it to reason over your notes:

        - "Read `index.md`, then summarize the themes across my thoughts."
        - "Search `thoughts/` for anything related to pricing and draft a plan."
        - "Find related ideas and suggest `[[wikilinks]]` to connect them."
        - "Turn the thought titled X into a structured spec."

        Everything is plain markdown — `cat`, `grep`, ripgrep, Obsidian, and any
        LLM all read it directly.
        """;
    }
}
