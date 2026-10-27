using MyDock.Core.Docker;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App;

/// <summary><c>mydock --dump</c>: prints one reading of every container to the terminal, without any UI.</summary>
internal static class DumpCommand
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // The first sample of a stats stream has no CPU delta; the second one arrives about a second later.
    private static readonly TimeSpan SamplingTime = TimeSpan.FromSeconds(2.5);

    public static async Task<int> RunAsync()
    {
        using var docker = new DockerService();
        await using var monitor = new ContainerMonitor(docker);

        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += () =>
        {
            if (monitor.Current.Connection != ConnectionState.Connecting)
                settled.TrySetResult();
        };
        monitor.StatsEnabled = true;
        monitor.Start();

        await Task.WhenAny(settled.Task, Task.Delay(ConnectTimeout));
        switch (monitor.Current.Connection)
        {
            case ConnectionState.Connected:
                break;
            case ConnectionState.PermissionDenied:
                Console.Error.WriteLine($"Permission denied on {docker.Endpoint}.");
                Console.Error.WriteLine("Add your user to the docker group, then log out and back in:");
                Console.Error.WriteLine("  sudo usermod -aG docker $USER");
                return 1;
            default:
                Console.Error.WriteLine($"Docker unreachable at {docker.Endpoint}.");
                return 1;
        }

        if (monitor.Current.ActiveCount > 0)
            await Task.Delay(SamplingTime);

        Print(monitor.Current);
        return 0;
    }

    private static void Print(MonitorSnapshot snapshot)
    {
        var rows = new List<string[]> { new[] { "NAME", "STATUS", "CPU", "MEMORY", "NET RX / TX", "DISK R / W", "PID" } };
        foreach (var (info, stats) in snapshot.Containers.OrderByDescending(c => c.Info.IsActive))
        {
            rows.Add(stats is null
                ? [info.Name, info.Status, "-", "-", "-", "-", "-"]
                :
                [
                    info.Name,
                    info.Status,
                    Format.Percent(stats.CpuPercent),
                    $"{Format.Memory(stats.MemoryUsed)} / {Format.Memory(stats.MemoryLimit)}",
                    $"{Format.Rate(stats.NetRxRate)} / {Format.Rate(stats.NetTxRate)}",
                    $"{Format.Rate(stats.BlockReadRate)} / {Format.Rate(stats.BlockWriteRate)}",
                    stats.Pids.ToString(),
                ]);
        }

        var widths = Enumerable.Range(0, rows[0].Length).Select(i => rows.Max(r => r[i].Length)).ToArray();
        foreach (var row in rows)
            Console.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());

        if (snapshot.Containers.Count == 0)
            Console.WriteLine("(no containers)");
    }
}
