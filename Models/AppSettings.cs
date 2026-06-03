namespace Effortless.Models;

/// <summary>Lightweight, user-tweakable preferences persisted to settings.json.</summary>
public class AppSettings
{
    public double ScratchPadFontSize { get; set; } = 14;

    /// <summary>True for dark theme (the original "lights out" look), false for light.</summary>
    public bool IsDarkMode { get; set; } = true;

    // -------- Ask providers --------
    // Each provider's Command must resolve on PATH (or be a full path / "wsl <cmd>"
    // form). Set the Command to "" to hide a provider from the Ask switcher.

    /// <summary>Claude Code CLI command. Default "claude".</summary>
    public string ClaudeCommand { get; set; } = "claude";

    /// <summary>Optional model alias/id for Claude. Empty = the CLI default.</summary>
    public string ClaudeModel { get; set; } = "";

    /// <summary>Hermes CLI command. Default "hermes" (set to "" to hide).</summary>
    public string HermesCommand { get; set; } = "hermes";

    /// <summary>
    /// Extra args passed to the Hermes CLI before the prompt (which is sent on
    /// stdin). Adjust this if Hermes needs specific flags — e.g. "ask --quiet".
    /// </summary>
    public string HermesArgs { get; set; } = "";

    /// <summary>Last-used provider id in the Ask window (e.g. "claude" or "hermes").</summary>
    public string LastAskProvider { get; set; } = "claude";
}
