using MyDock.Core.Models;

namespace MyDock.Core.Docker;

/// <param name="Action">Docker event action, e.g. "start", "die", "health_status: healthy".</param>
/// <param name="Name">The container's name, when the event carries it.</param>
/// <param name="ExitCode">Only set on "die".</param>
public sealed record ContainerEvent(string Action, string ContainerId, string? Name = null, int? ExitCode = null);

public enum InspectKind
{
    Image,
    Volume,
    Network,
}

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

    Task PauseAsync(string id, CancellationToken ct);
    Task UnpauseAsync(string id, CancellationToken ct);

    /// <summary>Sends SIGKILL at once, without the grace period of <see cref="StopAsync"/>.</summary>
    Task KillAsync(string id, CancellationToken ct);

    /// <summary>Sizes of images, containers, volumes and build cache; needs a daemon with API 1.52 or newer.</summary>
    Task<DiskUsage> GetDiskUsageAsync(CancellationToken ct);

    /// <summary>Lists the images, with how many containers use each.</summary>
    Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken ct);

    /// <summary>Lists the volumes with their size and how many containers mount them; needs API 1.52 or newer.</summary>
    Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(CancellationToken ct);

    /// <summary>Removes a volume and its data; the daemon refuses one that a container still mounts.</summary>
    Task RemoveVolumeAsync(string name, CancellationToken ct);

    Task<IReadOnlyList<DockerNetwork>> ListNetworksAsync(CancellationToken ct);

    /// <summary>Removes a network; the daemon refuses a built-in one and one that containers are still attached to.</summary>
    Task RemoveNetworkAsync(string id, CancellationToken ct);

    /// <summary>The daemon's inspect answer for an image (id or tag), volume (name) or network (id), as pretty-printed JSON.</summary>
    Task<string> InspectAsync(InspectKind kind, string id, CancellationToken ct);

    /// <summary>Names of the containers, running or not, that mount the volume.</summary>
    Task<IReadOnlyList<string>> ContainersUsingVolumeAsync(string name, CancellationToken ct);

    /// <summary>Removes dangling (untagged, unused) images; returns the bytes freed.</summary>
    Task<long> PruneImagesAsync(CancellationToken ct);

    /// <summary>Removes unused anonymous volumes, never named ones; returns the bytes freed.</summary>
    Task<long> PruneVolumesAsync(CancellationToken ct);

    /// <summary>Removes a stopped container; the daemon refuses a running one.</summary>
    Task RemoveContainerAsync(string id, CancellationToken ct);

    /// <summary>Removes an image by tag (only that tag is dropped) or by id; the daemon refuses one that a container still uses.</summary>
    Task RemoveImageAsync(string reference, CancellationToken ct);
}
