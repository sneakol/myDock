using System.Diagnostics;
using System.Net.Sockets;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.Core.Tests;

public sealed class ContainerMonitorTests : IAsyncDisposable
{
    private static readonly MonitorOptions FastOptions = new()
    {
        EventDebounce = TimeSpan.FromMilliseconds(5),
        PeriodicRefresh = TimeSpan.FromHours(1),
        ReconnectMin = TimeSpan.FromMilliseconds(10),
        ReconnectMax = TimeSpan.FromMilliseconds(20),
        StatsRetryDelay = TimeSpan.FromMilliseconds(10),
    };

    private readonly FakeDocker _docker = new();
    private readonly ContainerMonitor _monitor;

    public ContainerMonitorTests() => _monitor = new ContainerMonitor(_docker, FastOptions);

    public ValueTask DisposeAsync() => _monitor.DisposeAsync();

    [Fact]
    public async Task Lists_every_container_sorted_by_name_once_connected()
    {
        _docker.Set(
            FakeDocker.Container("2", name: "web"),
            FakeDocker.Container("1", ContainerState.Exited, name: "Db"),
            FakeDocker.Container("3", name: "api"));

        _monitor.Start();
        await Connected();

        Assert.Equal(["api", "Db", "web"], _monitor.Current.Containers.Select(c => c.Info.Name));
        Assert.Equal(2, _monitor.Current.ActiveCount);
    }

    [Fact]
    public async Task Raises_changed_when_the_snapshot_is_replaced()
    {
        var raised = 0;
        _monitor.Changed += () => Interlocked.Increment(ref raised);

        _monitor.Start();
        await Connected();

        Assert.True(Volatile.Read(ref raised) > 0);
    }

    [Fact]
    public async Task Relevant_event_refreshes_the_list()
    {
        _monitor.Start();
        await Connected();

        _docker.Set(FakeDocker.Container("a"));
        _docker.Raise("start", "a");

        await Eventually(() => _monitor.Current.Containers.Count == 1);
    }

    [Fact]
    public async Task Noise_events_do_not_refresh_the_list()
    {
        _monitor.Start();
        await Connected();
        var calls = _docker.ListCalls;

        _docker.Raise("exec_start: /bin/sh -c healthcheck");
        _docker.Raise("exec_die");
        await Task.Delay(100);

        Assert.Equal(calls, _docker.ListCalls);
    }

    [Fact]
    public async Task Opens_no_stats_stream_until_enabled()
    {
        _docker.Set(FakeDocker.Container("a"));

        _monitor.Start();
        await Connected();
        await Task.Delay(50);

        Assert.Empty(_docker.OpenStatsStreams);
    }

    [Fact]
    public async Task Enabling_stats_streams_running_containers_only()
    {
        _docker.Set(
            FakeDocker.Container("a"),
            FakeDocker.Container("b", ContainerState.Exited),
            FakeDocker.Container("c", ContainerState.Paused));
        _monitor.Start();
        await Connected();

        _monitor.StatsEnabled = true;

        await Eventually(() => _docker.OpenStatsStreams is ["a", "c"]);
    }

    [Fact]
    public async Task Enabling_stats_before_connecting_streams_once_connected()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;

        _monitor.Start();

        await Eventually(() => _docker.OpenStatsStreams is ["a"]);
    }

    [Fact]
    public async Task Publishes_stats_computed_from_samples()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);

        _docker.PushStats("a", new StatsSample { MemoryUsage = 1000, MemoryInactiveFile = 400, MemoryLimit = 4000, Pids = 7 });

        await Eventually(() => _monitor.Current.Containers[0].Stats is not null);
        var stats = _monitor.Current.Containers[0].Stats!;
        Assert.Equal(600ul, stats.MemoryUsed);
        Assert.Equal(15, stats.MemoryPercent);
        Assert.Equal(7ul, stats.Pids);
    }

    [Fact]
    public async Task Disabling_stats_closes_the_streams_but_keeps_last_values()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);
        _docker.PushStats("a", new StatsSample { Pids = 1 });
        await Eventually(() => _monitor.Current.Containers[0].Stats is not null);

        _monitor.StatsEnabled = false;

        await Eventually(() => _docker.OpenStatsStreams is []);
        Assert.NotNull(_monitor.Current.Containers[0].Stats);
    }

    [Fact]
    public async Task Stopped_container_loses_its_stream_and_stats()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);
        _docker.PushStats("a", new StatsSample { Pids = 1 });
        await Eventually(() => _monitor.Current.Containers[0].Stats is not null);

        _docker.Set(FakeDocker.Container("a", ContainerState.Exited));
        _docker.Raise("die", "a");

        await Eventually(() => _docker.OpenStatsStreams is []);
        await Eventually(() => _monitor.Current.Containers[0] is { Stats: null, Info.State: ContainerState.Exited });
    }

    [Fact]
    public async Task Removed_container_disappears_and_its_stream_closes()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);

        _docker.Set();
        _docker.Raise("destroy", "a");

        await Eventually(() => _monitor.Current.Containers.Count == 0 && _docker.OpenStatsStreams is []);
    }

    [Fact]
    public async Task Stats_stream_that_ends_by_itself_is_reopened()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);

        _docker.EndStats("a");
        await Eventually(() => _docker.OpenStatsStreams is []);

        await Eventually(() => _docker.OpenStatsStreams is ["a"]);
    }

    [Fact]
    public async Task Daemon_loss_is_reported_and_recovered_from()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);

        _docker.Break(new IOException("connection reset"));

        await Eventually(() => _monitor.Current.Connection == ConnectionState.DaemonUnavailable);
        Assert.Empty(_monitor.Current.Containers);
        await Eventually(() => _docker.OpenStatsStreams is []);

        _docker.Restore();

        await Connected();
        await Eventually(() => _monitor.Current.Containers.Count == 1 && _docker.OpenStatsStreams is ["a"]);
    }

    [Fact]
    public async Task Daemon_closing_the_event_stream_counts_as_loss()
    {
        _monitor.Start();
        await Connected();
        var calls = _docker.ListCalls;

        _docker.Break(new IOException("eof"));
        _docker.Restore();

        await Eventually(() => _docker.ListCalls > calls && _monitor.Current.Connection == ConnectionState.Connected);
    }

    [Fact]
    public async Task Socket_permission_error_is_reported_as_permission_denied()
    {
        _docker.Break(new HttpRequestException("connect failed", new SocketException((int)SocketError.AccessDenied)));

        _monitor.Start();

        await Eventually(() => _monitor.Current.Connection == ConnectionState.PermissionDenied);
    }

    [Fact]
    public async Task Actions_are_forwarded_to_docker()
    {
        await _monitor.StartContainerAsync("a");
        await _monitor.StopContainerAsync("b");
        await _monitor.RestartContainerAsync("c");

        Assert.Equal(["start a", "stop b", "restart c"], _docker.Actions);
    }

    [Theory]
    [InlineData("start", true)]
    [InlineData("die", true)]
    [InlineData("destroy", true)]
    [InlineData("rename", true)]
    [InlineData("health_status: unhealthy", true)]
    [InlineData("exec_create: sh", false)]
    [InlineData("exec_start: sh", false)]
    [InlineData("exec_die", false)]
    [InlineData("attach", false)]
    [InlineData("top", false)]
    public void Classifies_events(string action, bool relevant) =>
        Assert.Equal(relevant, ContainerMonitor.IsRelevant(action));

    private Task Connected() => Eventually(() => _monitor.Current.Connection == ConnectionState.Connected);

    private static async Task Eventually(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? what = null)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Timed out waiting for: {what}");
            await Task.Delay(5);
        }
    }
}
