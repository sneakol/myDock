using System.Net.Sockets;
using System.Text;

namespace MyDock.App.Services;

/// <summary>
/// Keeps one instance per session and lets later launches hand it a command,
/// over a Unix socket in the user's runtime directory.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string Show = "show";
    public const string Toggle = "toggle";
    public const string Quit = "quit";

    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Socket listener)
    {
        _listener = listener;
        _ = AcceptLoopAsync();
    }

    /// <summary>Raised on a background thread with the command sent by another launch.</summary>
    public event Action<string>? CommandReceived;

    private static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath(),
        "mydock.sock");

    /// <returns>True if a running instance took the command.</returns>
    public static bool TrySend(string command)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static SingleInstance Listen()
    {
        // Nobody answered TrySend, so a socket file still lying around is a leftover from a crash.
        File.Delete(SocketPath);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        listener.Listen();
        return new SingleInstance(listener);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        File.Delete(SocketPath);
    }

    private async Task AcceptLoopAsync()
    {
        var buffer = new byte[64];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
                var read = await client.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                var command = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                if (command.Length > 0)
                    CommandReceived?.Invoke(command);
            }
        }
        catch (Exception) when (_cts.IsCancellationRequested)
        {
        }
        catch (SocketException)
        {
        }
    }
}
