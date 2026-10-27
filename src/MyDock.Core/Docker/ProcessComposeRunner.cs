using System.Diagnostics;

namespace MyDock.Core.Docker;

/// <summary>Runs the docker CLI; the daemon API has no compose support.</summary>
public sealed class ProcessComposeRunner : IComposeRunner
{
    // Builds are slow, project actions are not; both stop hanging forever on a stuck daemon.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ProjectTimeout = TimeSpan.FromMinutes(5);

    public async Task RunAsync(string path, string? envFile, CancellationToken ct, bool recreate = false)
    {
        foreach (var args in ComposeFiles.Commands(path, envFile, recreate))
            await RunDockerAsync(args, Path.GetDirectoryName(Path.GetFullPath(path))!, BuildTimeout, ct);
    }

    public Task RunProjectAsync(string project, ProjectAction action, CancellationToken ct) =>
        RunDockerAsync(ComposeFiles.ProjectCommand(project, action), null, ProjectTimeout, ct);

    private static async Task RunDockerAsync(IReadOnlyList<string> args, string? workingDirectory, TimeSpan timeout, CancellationToken ct)
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

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        Process? process = null;
        try
        {
            process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            // stdout is drained so the child never blocks on a full pipe.
            var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
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
