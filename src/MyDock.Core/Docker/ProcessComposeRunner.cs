using System.Diagnostics;

namespace MyDock.Core.Docker;

/// <summary>Runs the docker CLI; the daemon API has no compose support.</summary>
public sealed class ProcessComposeRunner : IComposeRunner
{
    public async Task RunAsync(string path, string? envFile, CancellationToken ct, bool recreate = false)
    {
        foreach (var args in ComposeFiles.Commands(path, envFile, recreate))
            await RunDockerAsync(args, Path.GetDirectoryName(Path.GetFullPath(path))!, ct);
    }

    public Task RunProjectAsync(string project, ProjectAction action, CancellationToken ct) =>
        RunDockerAsync(ComposeFiles.ProjectCommand(project, action), null, ct);

    private static async Task RunDockerAsync(IReadOnlyList<string> args, string? workingDirectory, CancellationToken ct)
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

        try
        {
            using var process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync(ct);
            // stdout is drained so the child never blocks on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                throw new DockerOperationException(Tail(await stderr), new InvalidOperationException($"docker exited with {process.ExitCode}"));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new DockerOperationException("docker CLI not found.", ex);
        }
    }

    // Build output is long and the failure is at the end.
    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "docker failed." : string.Join('\n', lines[^Math.Min(lines.Length, 3)..]);
    }
}
