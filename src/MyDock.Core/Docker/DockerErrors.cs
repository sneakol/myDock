using System.Net.Sockets;
using MyDock.Core.Models;

namespace MyDock.Core.Docker;

public static class DockerErrors
{
    /// <summary>Maps a failure talking to the daemon to the state shown to the user.</summary>
    public static ConnectionState Classify(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException or SocketException { SocketErrorCode: SocketError.AccessDenied })
                return ConnectionState.PermissionDenied;
        }

        return ConnectionState.DaemonUnavailable;
    }
}
