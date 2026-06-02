using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Effortless.Services;

public sealed class ClaudeResult
{
    public bool Success { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool Cancelled { get; init; }
}

/// <summary>
/// Runs the Claude Code CLI headlessly to "prompt against" the user's notes.
///
/// Design notes (verified against claude 2.x):
/// - The prompt is written to a temp file and fed to the CLI via cmd's own
///   <c>&lt; file</c> redirection. This avoids putting note text on the command
///   line (no quoting) AND guarantees the CLI sees end-of-input, so it never
///   waits for more stdin (a hang we hit when piping across the cmd.exe layer).
/// - <c>--allowedTools Read,Grep,Glob</c> lets the model read files in the working
///   directory without an interactive permission prompt, while edits/Bash stay
///   blocked in headless mode.
/// - A stable <c>--session-id</c> on the first call + <c>--resume</c> on later calls
///   gives multi-turn follow-ups against the same context.
/// - Launched through <c>cmd.exe /c</c> so an npm <c>claude.cmd</c> shim resolves.
/// </summary>
public static class ClaudeService
{
    public static async Task<ClaudeResult> AskAsync(
        string prompt,
        string workingDir,
        string sessionId,
        bool resume,
        CancellationToken ct)
    {
        var settings = StorageService.LoadSettings();
        var claudeCmd = string.IsNullOrWhiteSpace(settings.ClaudeCommand)
            ? "claude"
            : settings.ClaudeCommand.Trim();

        var sessionFlag = resume ? $"--resume {sessionId}" : $"--session-id {sessionId}";
        var modelFlag = string.IsNullOrWhiteSpace(settings.ClaudeModel)
            ? string.Empty
            : $"--model {settings.ClaudeModel.Trim()} ";

        var promptFile = Path.Combine(Path.GetTempPath(), $"effortless-ask-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(promptFile, prompt, new UTF8Encoding(false), ct);
        }
        catch (Exception ex)
        {
            return new ClaudeResult { Success = false, Error = $"Could not stage prompt: {ex.Message}" };
        }

        // Prompt comes from the redirected file; the command line carries no note text.
        var arguments =
            $"/c {claudeCmd} -p --output-format text --allowedTools Read,Grep,Glob " +
            $"{modelFlag}{sessionFlag} < \"{promptFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            WorkingDirectory = Directory.Exists(workingDir) ? workingDir : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            TryDelete(promptFile);
            return new ClaudeResult { Success = false, Error = $"Could not start Claude CLI: {ex.Message}" };
        }

        if (process == null)
        {
            TryDelete(promptFile);
            return new ClaudeResult { Success = false, Error = "Could not start Claude CLI." };
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode == 0 && stdout.Length > 0)
                return new ClaudeResult { Success = true, Text = stdout };

            var error = stderr.Length > 0
                ? stderr
                : stdout.Length > 0 ? stdout : "Claude returned no output (exit code " + process.ExitCode + ").";

            if (error.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            {
                error += "\n\nMake sure Claude Code is installed and on your PATH, or set " +
                         "\"ClaudeCommand\" in settings.json (e.g. a full path, or \"wsl claude\").";
            }
            else if (error.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                     error.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                     error.Contains("api key", StringComparison.OrdinalIgnoreCase))
            {
                error += "\n\nClaude may not be authenticated. Run `claude` once in a terminal and sign in.";
            }

            return new ClaudeResult { Success = false, Error = error };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ClaudeResult { Success = false, Cancelled = true, Error = "Cancelled." };
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new ClaudeResult { Success = false, Error = ex.Message };
        }
        finally
        {
            process.Dispose();
            TryDelete(promptFile);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
