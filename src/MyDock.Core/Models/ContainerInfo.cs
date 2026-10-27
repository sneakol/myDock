namespace MyDock.Core.Models;

/// <summary>Identity and lifecycle state of a container, as reported by the container list.</summary>
/// <param name="Status">Docker's own human-readable status, e.g. "Up 2 hours (healthy)".</param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    ContainerState State,
    HealthState Health,
    string Status,
    DateTimeOffset Created,
    string? ComposeProject)
{
    /// <summary>True while the container holds resources and can be stopped or restarted.</summary>
    public bool IsActive => State is ContainerState.Running or ContainerState.Paused or ContainerState.Restarting;
}
