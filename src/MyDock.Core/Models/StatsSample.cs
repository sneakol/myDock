namespace MyDock.Core.Models;

/// <summary>One raw reading from the stats stream: cumulative counters, independent of the Docker client library.</summary>
public sealed record StatsSample
{
    public DateTimeOffset Read { get; init; }

    public ulong CpuTotal { get; init; }
    public ulong PreCpuTotal { get; init; }
    public ulong SystemCpu { get; init; }
    public ulong PreSystemCpu { get; init; }
    public uint OnlineCpus { get; init; }

    public ulong MemoryUsage { get; init; }
    public ulong MemoryInactiveFile { get; init; }
    public ulong MemoryLimit { get; init; }

    public ulong NetRxBytes { get; init; }
    public ulong NetTxBytes { get; init; }
    public ulong BlockReadBytes { get; init; }
    public ulong BlockWriteBytes { get; init; }

    public ulong Pids { get; init; }
}
