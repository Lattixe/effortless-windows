using System.Linq;

namespace Effortless.Models;

/// <summary>
/// A single captured thought in the vault. Persisted as an agent-native
/// markdown file with YAML frontmatter so it can be browsed by humans and
/// prompted against by AI agents.
/// </summary>
public class VaultThought
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<string> Tags { get; set; } = new();
    public string Content { get; set; } = string.Empty;

    /// <summary>Full path to the .md file on disk (runtime only, not serialized).</summary>
    public string FilePath { get; set; } = string.Empty;

    public string FileName => string.IsNullOrEmpty(FilePath)
        ? string.Empty
        : System.IO.Path.GetFileName(FilePath);

    /// <summary>First non-empty line of the body, trimmed for list display.</summary>
    public string Preview
    {
        get
        {
            var firstLine = Content
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            return firstLine.Length > 120 ? firstLine[..120] + "…" : firstLine;
        }
    }

    public string DisplayDate => Created.ToString("MMM d, yyyy · h:mm tt");
}
