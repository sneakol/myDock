namespace MyDock.Core.Docker;

public interface IComposeRunner
{
    /// <summary>Builds and starts a Dockerfile or compose file, optionally with an env file. Throws <see cref="DockerOperationException"/> on failure.</summary>
    Task RunAsync(string path, string? envFile, CancellationToken ct, bool recreate = false);

    /// <summary>Applies <paramref name="action"/> to all containers of a compose project, found by its name. Throws <see cref="DockerOperationException"/> on failure.</summary>
    Task RunProjectAsync(string project, ProjectAction action, CancellationToken ct);
}
