namespace MyDock.Core.Models;

/// <param name="Stats">Null until the first sample arrives, and for containers that are not running.</param>
public sealed record ContainerSnapshot(ContainerInfo Info, ContainerStats? Stats);

/// <summary>Immutable view of everything the monitor knows at one point in time. Containers are sorted by name.</summary>
public sealed record MonitorSnapshot(ConnectionState Connection, IReadOnlyList<ContainerSnapshot> Containers)
{
    public static MonitorSnapshot Initial { get; } = new(ConnectionState.Connecting, []);

    public int ActiveCount => Containers.Count(c => c.Info.IsActive);
}
