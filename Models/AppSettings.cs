namespace Effortless.Models;

/// <summary>Lightweight, user-tweakable preferences persisted to settings.json.</summary>
public class AppSettings
{
    public double ScratchPadFontSize { get; set; } = 14;

    /// <summary>
    /// Command used to invoke the Claude Code CLI for "Ask Claude". Defaults to
    /// "claude" (must be on PATH). Set to a full path, or e.g. "wsl claude" if
    /// Claude Code lives in WSL.
    /// </summary>
    public string ClaudeCommand { get; set; } = "claude";

    /// <summary>Optional model alias/id for Ask Claude. Empty = the CLI default.</summary>
    public string ClaudeModel { get; set; } = "";
}
