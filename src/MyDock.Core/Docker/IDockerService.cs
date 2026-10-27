using MyDock.Core.Models;

namespace MyDock.Core.Docker;

/// <param name="Action">Docker event action, e.g. "start", "die", "health_status: healthy".</param>
public sealed record ContainerEvent(string Action, string ContainerId);

/// <summary>The only seam that knows about the Docker client library.</summary>
public interface IDockerService : IDisposable
{
    /// <summary>Lists every container, running or not.</summary>
    Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct);

    /// <summary>Streams roughly one sample per second until cancelled or the daemon closes the stream.</summary>
    IAsyncEnumerable<StatsSample> StreamStatsAsync(string id, CancellationToken ct);

    /// <summary>Streams container events from <paramref name="since"/> onwards until cancelled or the daemon closes the stream.</summary>
    IAsyncEnumerable<ContainerEvent> WatchEventsAsync(DateTimeOffset since, CancellationToken ct);

    Task StartAsync(string id, CancellationToken ct);
    Task StopAsync(string id, CancellationToken ct);
    Task RestartAsync(string id, CancellationToken ct);

    /// <summary>Removes a stopped container; the daemon refuses a running one.</summary>
    Task RemoveContainerAsync(string id, CancellationToken ct);

    /// <summary>Removes an image by tag (only that tag is dropped) or by id; the daemon refuses one that a container still uses.</summary>
    Task RemoveImageAsync(string reference, CancellationToken ct);
}
