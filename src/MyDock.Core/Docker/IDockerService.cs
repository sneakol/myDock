using MyDock.Core.Models;

namespace MyDock.Core.Docker;

/// <param name="Action">Docker event action, e.g. "start", "die", "health_status: healthy".</param>
/// <param name="Name">The container's name, when the event carries it.</param>
/// <param name="ExitCode">Only set on "die".</param>
public sealed record ContainerEvent(string Action, string ContainerId, string? Name = null, int? ExitCode = null);

/// <summary>The only seam that knows about the Docker client library.</summary>
public interface IDockerService : IDisposable
{
    /// <summary>Lists every container, running or not.</summary>
    Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct);

    /// <summary>Streams roughly one sample per second until cancelled or the daemon closes the stream.</summary>
    IAsyncEnumerable<StatsSample> StreamStatsAsync(string id, CancellationToken ct);

    /// <summary>Streams container events from <paramref name="since"/> onwards until cancelled or the daemon closes the stream.</summary>
    IAsyncEnumerable<ContainerEvent> WatchEventsAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Streams the last <paramref name="tail"/> log lines as text chunks, each ending with a newline; with <paramref name="follow"/>
    /// it keeps going with new output until cancelled or the container goes away.
    /// </summary>
    IAsyncEnumerable<string> StreamLogsAsync(string id, int tail, bool follow, CancellationToken ct);

    Task StartAsync(string id, CancellationToken ct);
    Task StopAsync(string id, CancellationToken ct);
    Task RestartAsync(string id, CancellationToken ct);

    /// <summary>Removes a stopped container; the daemon refuses a running one.</summary>
    Task RemoveContainerAsync(string id, CancellationToken ct);

    /// <summary>Removes an image by tag (only that tag is dropped) or by id; the daemon refuses one that a container still uses.</summary>
    Task RemoveImageAsync(string reference, CancellationToken ct);
}
