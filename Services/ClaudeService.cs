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
}

/// <summary>
/// Runs the Claude Code CLI headlessly to "prompt against" the user's notes.
///
/// Design notes (verified against claude 2.x):
/// - The prompt is delivered on <c>stdin</c>, never on the command line, so there
///   is no quoting/escaping to get wrong and notes can contain anything.
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

        // The prompt is on stdin, so the command line carries no user text.
        var arguments =
            $"/c {claudeCmd} -p --output-format text --allowedTools Read,Grep,Glob {modelFlag}{sessionFlag}";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            WorkingDirectory = Directory.Exists(workingDir) ? workingDir : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
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
            return new ClaudeResult { Success = false, Error = $"Could not start Claude CLI: {ex.Message}" };
        }

        if (process == null)
            return new ClaudeResult { Success = false, Error = "Could not start Claude CLI." };

        try
        {
            // Start draining stdout/stderr before writing stdin to avoid pipe deadlocks.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Writing can fail if the process already exited (e.g. claude not found);
            // swallow it so we still report the real stderr below.
            try
            {
                await using var stdin = process.StandardInput;
                await stdin.WriteAsync(prompt);
            }
            catch (IOException)
            {
                // process ended before consuming stdin
            }

            await process.WaitForExitAsync(ct);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode == 0 && stdout.Length > 0)
                return new ClaudeResult { Success = true, Text = stdout };

            var error = stderr.Length > 0
                ? stderr
                : stdout.Length > 0 ? stdout : "Claude returned no output.";

            if (error.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("is not recognized", StringComparison.OrdinalIgnoreCase))
            {
                error += "\n\nMake sure Claude Code is installed and on your PATH, or set " +
                         "\"ClaudeCommand\" in settings.json (e.g. a full path, or \"wsl claude\").";
            }

            return new ClaudeResult { Success = false, Error = error };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ClaudeResult { Success = false, Error = "Cancelled." };
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new ClaudeResult { Success = false, Error = ex.Message };
        }
        finally
        {
            process.Dispose();
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
}
