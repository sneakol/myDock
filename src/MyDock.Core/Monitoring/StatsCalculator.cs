using MyDock.Core.Models;

namespace MyDock.Core.Monitoring;

public static class StatsCalculator
{
    /// <summary>
    /// CPU usage as shown by <c>docker stats</c>: 100% means one full core.
    /// Returns 0 for the first sample of a stream, which has no previous reading to diff against.
    /// </summary>
    public static double CpuPercent(StatsSample s)
    {
        if (s.PreSystemCpu == 0 || s.SystemCpu <= s.PreSystemCpu || s.CpuTotal <= s.PreCpuTotal)
            return 0;

        var cpuDelta = (double)(s.CpuTotal - s.PreCpuTotal);
        var systemDelta = (double)(s.SystemCpu - s.PreSystemCpu);
        return cpuDelta / systemDelta * Math.Max(1u, s.OnlineCpus) * 100.0;
    }

    /// <summary>Memory in use excluding reclaimable page cache, matching <c>docker stats</c>.</summary>
    public static ulong MemoryUsed(StatsSample s) =>
        s.MemoryInactiveFile < s.MemoryUsage ? s.MemoryUsage - s.MemoryInactiveFile : s.MemoryUsage;
}
