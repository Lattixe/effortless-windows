using System.Collections.Generic;
using Effortless.Services;

namespace Effortless.Models;

/// <summary>
/// A configured CLI-based AI provider for the "Ask" feature. Each provider
/// shells out to a command, sends the prompt on stdin (via cmd `&lt; file`
/// redirection), and reads stdout.
/// </summary>
public sealed record AskProvider(
    string Id,
    string Label,
    string Command,
    string Args,
    bool SupportsSession,
    string SessionIdArg,
    string ResumeArg)
{
    /// <summary>
    /// Build the live provider list from current settings. Providers with an
    /// empty Command are omitted (so a user can hide a provider by clearing
    /// it in settings.json).
    /// </summary>
    public static List<AskProvider> AllConfigured()
    {
        var s = StorageService.LoadSettings();
        var list = new List<AskProvider>();

        if (!string.IsNullOrWhiteSpace(s.ClaudeCommand))
        {
            var baseArgs = "-p --output-format text --allowedTools Read,Grep,Glob";
            var args = string.IsNullOrWhiteSpace(s.ClaudeModel)
                ? baseArgs
                : $"{baseArgs} --model {s.ClaudeModel.Trim()}";
            list.Add(new AskProvider(
                Id: "claude",
                Label: "Claude",
                Command: s.ClaudeCommand.Trim(),
                Args: args,
                SupportsSession: true,
                SessionIdArg: "--session-id",
                ResumeArg: "--resume"));
        }

        if (!string.IsNullOrWhiteSpace(s.HermesCommand))
        {
            list.Add(new AskProvider(
                Id: "hermes",
                Label: "Hermes",
                Command: s.HermesCommand.Trim(),
                Args: s.HermesArgs ?? string.Empty,
                SupportsSession: false,
                SessionIdArg: string.Empty,
                ResumeArg: string.Empty));
        }

        return list;
    }
}
