namespace MyDock.Core.Models;

public enum ContainerState
{
    Unknown,
    Created,
    Running,
    Paused,
    Restarting,
    Removing,
    Exited,
    Dead,
}

public enum HealthState
{
    None,
    Starting,
    Healthy,
    Unhealthy,
}

public enum ConnectionState
{
    Connecting,
    Connected,
    DaemonUnavailable,
    PermissionDenied,
}
