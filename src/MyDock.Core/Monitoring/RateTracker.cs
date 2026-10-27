using MyDock.Core.Models;

namespace MyDock.Core.Monitoring;

/// <summary>Turns the cumulative counters of consecutive samples from one container into per-second rates.</summary>
public sealed class RateTracker
{
    private StatsSample? _previous;

    public ContainerStats Next(StatsSample sample)
    {
        var previous = _previous;
        _previous = sample;

        var seconds = previous is null ? 0 : (sample.Read - previous.Read).TotalSeconds;

        return new ContainerStats(
            StatsCalculator.CpuPercent(sample),
            StatsCalculator.MemoryUsed(sample),
            sample.MemoryLimit,
            Rate(previous?.NetRxBytes, sample.NetRxBytes, seconds),
            Rate(previous?.NetTxBytes, sample.NetTxBytes, seconds),
            Rate(previous?.BlockReadBytes, sample.BlockReadBytes, seconds),
            Rate(previous?.BlockWriteBytes, sample.BlockWriteBytes, seconds),
            sample.Pids);
    }

    // A counter going backwards means the container restarted: report 0 rather than a bogus spike.
    private static double Rate(ulong? before, ulong now, double seconds) =>
        before is { } b && seconds > 0 && now >= b ? (now - b) / seconds : 0;
}
