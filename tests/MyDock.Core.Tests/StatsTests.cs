using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.Core.Tests;

public sealed class StatsCalculatorTests
{
    [Fact]
    public void Cpu_percent_scales_the_container_share_by_core_count()
    {
        // 0.2 s of CPU time during 1 s of wall time on an 8-core host.
        var sample = new StatsSample
        {
            PreCpuTotal = 1_000_000_000,
            CpuTotal = 1_200_000_000,
            PreSystemCpu = 80_000_000_000,
            SystemCpu = 88_000_000_000,
            OnlineCpus = 8,
        };

        Assert.Equal(20.0, StatsCalculator.CpuPercent(sample), 6);
    }

    [Fact]
    public void Cpu_percent_is_zero_for_the_first_sample_of_a_stream()
    {
        var sample = new StatsSample { CpuTotal = 5_000_000_000, SystemCpu = 88_000_000_000, OnlineCpus = 8 };

        Assert.Equal(0, StatsCalculator.CpuPercent(sample));
    }

    [Theory]
    [InlineData(100ul, 100ul, 200ul, 300ul)] // no CPU used
    [InlineData(100ul, 200ul, 300ul, 300ul)] // system clock did not advance
    [InlineData(200ul, 100ul, 200ul, 300ul)] // counter went backwards
    public void Cpu_percent_is_zero_without_a_positive_delta(ulong preCpu, ulong cpu, ulong preSystem, ulong system)
    {
        var sample = new StatsSample
        {
            PreCpuTotal = preCpu, CpuTotal = cpu, PreSystemCpu = preSystem, SystemCpu = system, OnlineCpus = 4,
        };

        Assert.Equal(0, StatsCalculator.CpuPercent(sample));
    }

    [Fact]
    public void Cpu_percent_treats_missing_core_count_as_one()
    {
        var sample = new StatsSample { PreCpuTotal = 0, CpuTotal = 50, PreSystemCpu = 100, SystemCpu = 200 };

        Assert.Equal(50.0, StatsCalculator.CpuPercent(sample), 6);
    }

    [Fact]
    public void Memory_used_excludes_inactive_page_cache()
    {
        var sample = new StatsSample { MemoryUsage = 500, MemoryInactiveFile = 120 };

        Assert.Equal(380ul, StatsCalculator.MemoryUsed(sample));
    }

    [Fact]
    public void Memory_used_ignores_page_cache_larger_than_usage()
    {
        var sample = new StatsSample { MemoryUsage = 100, MemoryInactiveFile = 120 };

        Assert.Equal(100ul, StatsCalculator.MemoryUsed(sample));
    }
}

public sealed class RateTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_sample_has_no_rates()
    {
        var stats = new RateTracker().Next(new StatsSample { Read = T0, NetRxBytes = 10_000, BlockWriteBytes = 10_000 });

        Assert.Equal(0, stats.NetRxRate);
        Assert.Equal(0, stats.BlockWriteRate);
    }

    [Fact]
    public void Rates_are_counter_deltas_per_second()
    {
        var tracker = new RateTracker();
        tracker.Next(new StatsSample { Read = T0, NetRxBytes = 1000, NetTxBytes = 500, BlockReadBytes = 0, BlockWriteBytes = 100 });

        var stats = tracker.Next(new StatsSample
        {
            Read = T0.AddSeconds(2), NetRxBytes = 5000, NetTxBytes = 700, BlockReadBytes = 4096, BlockWriteBytes = 100,
        });

        Assert.Equal(2000, stats.NetRxRate);
        Assert.Equal(100, stats.NetTxRate);
        Assert.Equal(2048, stats.BlockReadRate);
        Assert.Equal(0, stats.BlockWriteRate);
    }

    [Fact]
    public void Counter_reset_yields_zero_instead_of_a_spike()
    {
        var tracker = new RateTracker();
        tracker.Next(new StatsSample { Read = T0, NetRxBytes = 9000 });

        var stats = tracker.Next(new StatsSample { Read = T0.AddSeconds(1), NetRxBytes = 10 });

        Assert.Equal(0, stats.NetRxRate);
    }

    [Fact]
    public void Samples_with_the_same_timestamp_yield_zero()
    {
        var tracker = new RateTracker();
        tracker.Next(new StatsSample { Read = T0, NetRxBytes = 0 });

        var stats = tracker.Next(new StatsSample { Read = T0, NetRxBytes = 1000 });

        Assert.Equal(0, stats.NetRxRate);
    }

    [Fact]
    public void Carries_memory_and_pids_through()
    {
        var stats = new RateTracker().Next(new StatsSample { MemoryUsage = 300, MemoryInactiveFile = 100, MemoryLimit = 800, Pids = 3 });

        Assert.Equal(200ul, stats.MemoryUsed);
        Assert.Equal(800ul, stats.MemoryLimit);
        Assert.Equal(25, stats.MemoryPercent);
        Assert.Equal(3ul, stats.Pids);
    }

    [Fact]
    public void Memory_percent_is_zero_without_a_limit()
    {
        Assert.Equal(0, new ContainerStats(0, 100, 0, 0, 0, 0, 0, 0).MemoryPercent);
    }
}
