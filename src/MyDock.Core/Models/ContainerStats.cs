namespace MyDock.Core.Models;

/// <summary>Resource usage derived from the stats stream. Rates are in bytes per second.</summary>
public sealed record ContainerStats(
    double CpuPercent,
    ulong MemoryUsed,
    ulong MemoryLimit,
    double NetRxRate,
    double NetTxRate,
    double BlockReadRate,
    double BlockWriteRate,
    ulong Pids)
{
    public double MemoryPercent => MemoryLimit == 0 ? 0 : 100.0 * MemoryUsed / MemoryLimit;
}
