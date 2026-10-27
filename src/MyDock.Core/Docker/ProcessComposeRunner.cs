using System.Diagnostics;
using System.Text;

namespace MyDock.Core.Docker;

/// <summary>Runs the docker CLI; the daemon API has no compose support.</summary>
public sealed class ProcessComposeRunner : IComposeRunner
{
    // Builds are slow, project actions are not; both stop hanging forever on a stuck daemon.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ProjectTimeout = TimeSpan.FromMinutes(5);

    public async Task RunAsync(string path, string? envFile, CancellationToken ct, bool recreate = false, Action<string>? output = null)
    {
        foreach (var args in ComposeFiles.Commands(path, envFile, recreate))
            await RunDockerAsync(args, Path.GetDirectoryName(Path.GetFullPath(path))!, BuildTimeout, ct, output);
    }

    public Task RunProjectAsync(string project, ProjectAction action, CancellationToken ct) =>
        RunDockerAsync(ComposeFiles.ProjectCommand(project, action), null, ProjectTimeout, ct);

    private static async Task RunDockerAsync(
        IReadOnlyList<string> args, string? workingDirectory, TimeSpan timeout, CancellationToken ct, Action<string>? output = null)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (workingDirectory is not null)
            info.WorkingDirectory = workingDirectory;
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        output?.Invoke($"$ docker {string.Join(' ', args)}\n");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        Process? process = null;
        try
        {
            process = Process.Start(info)!;
            // Both pipes are always drained so the child never blocks on a full one; only stderr is kept, for the error message.
            var stderr = PumpAsync(process.StandardError, output, keep: true, limit.Token);
            var stdout = PumpAsync(process.StandardOutput, output, keep: false, limit.Token);
            await process.WaitForExitAsync(limit.Token);
            await stdout;
            if (process.ExitCode != 0)
                throw new DockerOperationException(Tail(await stderr), new InvalidOperationException($"docker exited with {process.ExitCode}"));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new DockerOperationException("docker CLI not found.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new DockerOperationException($"docker timed out after {timeout.TotalMinutes:0} min.", new TimeoutException());
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<string> PumpAsync(StreamReader reader, Action<string>? output, bool keep, CancellationToken ct)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            output?.Invoke(line + "\n");
            if (keep)
                text.AppendLine(line);
        }

        return text.ToString();
    }

    private static void Kill(Process? process)
    {
        try
        {
            process?.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited, or not ours to kill: the original failure matters more.
        }
    }

    // Build output is long and the failure is at the end.
    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "docker failed." : string.Join('\n', lines[^Math.Min(lines.Length, 3)..]);
    }
}
