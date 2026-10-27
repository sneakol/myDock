namespace MyDock.Core.Docker;

public interface IComposeRunner
{
    /// <summary>Builds and starts a Dockerfile or compose file, optionally with an env file. Throws <see cref="DockerOperationException"/> on failure.</summary>
    Task RunAsync(string path, string? envFile, CancellationToken ct);
}
