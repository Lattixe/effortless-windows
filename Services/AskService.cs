using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Effortless.Models;

namespace Effortless.Services;

public sealed class AskResult
{
    public bool Success { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool Cancelled { get; init; }
}

/// <summary>
/// Runs a configured CLI AI provider headlessly. Generic over any provider
/// described by an <see cref="AskProvider"/>: the prompt is staged to a temp
/// file and fed via cmd's <c>&lt; file</c> redirection (avoids any command-line
/// quoting issues and guarantees the CLI sees end-of-input), the process is
/// launched through <c>cmd.exe /c</c> so npm shims like <c>claude.cmd</c>
/// resolve, and stdout is returned.
/// </summary>
public static class AskService
{
    public static async Task<AskResult> AskAsync(
        AskProvider provider,
        string prompt,
        string workingDir,
        string sessionId,
        bool resume,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider.Command))
            return new AskResult
            {
                Success = false,
                Error = $"{provider.Label} is not configured. Set its Command in settings.json."
            };

        var args = provider.Args ?? string.Empty;
        if (provider.SupportsSession && !string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionPart = resume
                ? $"{provider.ResumeArg} {sessionId}"
                : $"{provider.SessionIdArg} {sessionId}";
            args = string.IsNullOrWhiteSpace(args) ? sessionPart : $"{args} {sessionPart}";
        }

        var promptFile = Path.Combine(Path.GetTempPath(), $"effortless-ask-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(promptFile, prompt, new UTF8Encoding(false), ct);
        }
        catch (Exception ex)
        {
            return new AskResult { Success = false, Error = $"Could not stage prompt: {ex.Message}" };
        }

        // Prompt comes from the redirected file; the command line carries no note text.
        var arguments = $"/c {provider.Command} {args} < \"{promptFile}\"";

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
            return new AskResult { Success = false, Error = $"Could not start {provider.Label}: {ex.Message}" };
        }

        if (process == null)
        {
            TryDelete(promptFile);
            return new AskResult { Success = false, Error = $"Could not start {provider.Label}." };
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode == 0 && stdout.Length > 0)
                return new AskResult { Success = true, Text = stdout };

            var error = stderr.Length > 0
                ? stderr
                : stdout.Length > 0
                    ? stdout
                    : $"{provider.Label} returned no output (exit code {process.ExitCode}).";

            if (error.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            {
                error += $"\n\nMake sure '{provider.Command}' is installed and on your PATH, " +
                         "or set it explicitly in settings.json (e.g. a full path, or \"wsl <cmd>\").";
            }
            else if (error.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                     error.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                     error.Contains("api key", StringComparison.OrdinalIgnoreCase))
            {
                error += $"\n\n{provider.Label} may not be authenticated. Run it once in a terminal and sign in.";
            }

            return new AskResult { Success = false, Error = error };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new AskResult { Success = false, Cancelled = true, Error = "Cancelled." };
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new AskResult { Success = false, Error = ex.Message };
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
        catch { /* best effort */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
