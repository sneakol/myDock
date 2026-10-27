using MyDock.Core.Docker;

namespace MyDock.Core.Monitoring;

public enum AlertKind
{
    Crashed,
    OutOfMemory,
    Unhealthy,
    RestartLoop,
}

public sealed record ContainerAlert(AlertKind Kind, string Name, int ExitCode = 0)
{
    public string Message => Kind switch
    {
        AlertKind.Crashed => $"{Name} exited with code {ExitCode}",
        AlertKind.OutOfMemory => $"{Name} was killed: out of memory",
        AlertKind.Unhealthy => $"{Name} is unhealthy",
        _ => $"{Name} keeps failing (restart loop)",
    };
}

/// <summary>Turns the raw event stream into the few things worth interrupting the user for. Not thread-safe: feed it from one consumer.</summary>
public sealed class AlertTracker
{
    /// <summary>This many failures within <see cref="LoopWindow"/> make a restart loop, announced once instead of every failure.</summary>
    public const int LoopThreshold = 3;

    public static readonly TimeSpan LoopWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, State> _states = [];

    public ContainerAlert? Process(ContainerEvent e, DateTimeOffset now)
    {
        var name = e.Name ?? e.ContainerId[..Math.Min(12, e.ContainerId.Length)];
        switch (e.Action)
        {
            case "destroy":
                _states.Remove(e.ContainerId);
                return null;

            case "start":
                // An oom that never led to a die must not taint the next, unrelated one.
                StateOf(e.ContainerId).Oom = false;
                return null;

            case "oom":
                StateOf(e.ContainerId).Oom = true;
                return null;

            case "health_status: unhealthy":
                return new ContainerAlert(AlertKind.Unhealthy, name);

            case "die":
                return Died(StateOf(e.ContainerId), name, e.ExitCode ?? 0, now);

            default:
                return null;
        }
    }

    private static ContainerAlert? Died(State state, string name, int exitCode, DateTimeOffset now)
    {
        var oom = state.Oom;
        state.Oom = false;

        // 0 is a clean exit; 143 and 137 are SIGTERM and SIGKILL, which is what `docker stop` does, unless the OOM killer was behind it.
        if (!oom && exitCode is 0 or 137 or 143)
            return null;

        state.Failures.Enqueue(now);
        while (state.Failures.Peek() < now - LoopWindow)
            state.Failures.Dequeue();

        if (state.Failures.Count < LoopThreshold)
        {
            state.InLoop = false;
            return new ContainerAlert(oom ? AlertKind.OutOfMemory : AlertKind.Crashed, name, exitCode);
        }

        if (state.InLoop)
            return null;

        state.InLoop = true;
        return new ContainerAlert(AlertKind.RestartLoop, name, exitCode);
    }

    private State StateOf(string id)
    {
        if (!_states.TryGetValue(id, out var state))
            _states[id] = state = new State();
        return state;
    }

    private sealed class State
    {
        public Queue<DateTimeOffset> Failures { get; } = new();
        public bool Oom { get; set; }
        public bool InLoop { get; set; }
    }
}
