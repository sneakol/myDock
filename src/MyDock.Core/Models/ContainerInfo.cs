namespace MyDock.Core.Models;

/// <summary>Identity and lifecycle state of a container, as reported by the container list.</summary>
/// <param name="Status">Docker's own human-readable status, e.g. "Up 2 hours (healthy)".</param>
/// <param name="Ports">Published and exposed ports, e.g. "8080→80/tcp, 5432/tcp"; null when there are none.</param>
/// <param name="PublicPorts">Host TCP ports the container publishes, ascending; null when there are none.</param>
/// <param name="Networks">Names of the user-defined networks the container is on, comma separated; null when only on a built-in one.</param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string ImageId,
    ContainerState State,
    HealthState Health,
    string Status,
    DateTimeOffset Created,
    string? ComposeProject,
    string? Ports = null,
    string? Networks = null,
    IReadOnlyList<int>? PublicPorts = null)
{
    /// <summary>True while the container holds resources and can be stopped or restarted.</summary>
    public bool IsActive => State is ContainerState.Running or ContainerState.Paused or ContainerState.Restarting;
}
