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
    private static readonly string VaultIndexFile = Path.Combine(VaultFolder, "index.md");
    private static readonly string VaultReadmeFile = Path.Combine(VaultFolder, "README.md");

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

    // ---------------------------------------------------------------------
    // Thought Vault
    //
    // An agent-native markdown knowledge base. Each captured thought becomes a
    // standalone .md file with YAML frontmatter under Documents/Effortless Vault/
    // thoughts/, and a wiki-style index.md is regenerated on every change.
    // ---------------------------------------------------------------------

    public static string GetVaultFolder() => VaultFolder;

    public static string GetVaultIndexFile() => VaultIndexFile;

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
            sb.Append("Agent-native markdown knowledge base. See [`README.md`](README.md) ");
            sb.Append("for how to query it.\n");

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

                    var rel = "thoughts/" + Path.GetFileName(t.FilePath);
                    var tagStr = t.Tags.Count > 0
                        ? "  " + string.Join(" ", t.Tags.Select(tag => $"`#{tag}`"))
                        : "";
                    sb.Append($"- **[{t.Title}]({EscapeLink(rel)})** — *{t.Created:yyyy-MM-dd}*{tagStr}\n");
                    if (!string.IsNullOrWhiteSpace(t.Preview))
                        sb.Append($"  - {t.Preview}\n");
                }
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

    private static string EscapeLink(string value) => value.Replace(" ", "%20");

    private static string VaultReadmeContent()
    {
        return """
        # Effortless Thought Vault

        This folder is an **agent-native markdown knowledge base** — a personal
        wiki of captured thoughts. It is designed to be read by humans *and*
        prompted against by AI agents (Claude Code, etc.).

        ## Structure

        ```
        Effortless Vault/
        ├── README.md          ← you are here (how to use / query the vault)
        ├── index.md           ← auto-generated catalog of every thought
        └── thoughts/
            └── YYYY-MM-DD-slug.md   ← one file per captured thought
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

        The body of the thought, exactly as captured in the scratch pad.
        ```

        - `index.md` is regenerated automatically whenever a thought is added or
          removed — never edit it by hand (your changes will be overwritten).
        - Thought files are yours to edit freely; add tags, refine titles, link
          between them with standard `[wiki](thoughts/other.md)` links.

        ## Prompting against the vault (agent workflow)

        Point an AI agent at this folder and ask it to reason over your thoughts:

        - "Read `index.md`, then summarize the themes across my thoughts."
        - "Search `thoughts/` for anything related to pricing and draft a plan."
        - "Find duplicate or related ideas and suggest how to merge them."
        - "Turn the thought titled X into a structured spec."

        Because everything is plain markdown, no special tooling is required —
        `cat`, `grep`, and ripgrep all work, and any LLM can ingest the files
        directly.
        """;
    }
}
