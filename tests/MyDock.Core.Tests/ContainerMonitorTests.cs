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
    public async Task Raises_an_alert_when_a_container_turns_unhealthy()
    {
        var alerts = new List<ContainerAlert>();
        _monitor.Alerted += alert =>
        {
            lock (alerts)
                alerts.Add(alert);
        };

        _monitor.Start();
        await Connected();
        _docker.Raise("health_status: unhealthy", "a");

        await Eventually(() =>
        {
            lock (alerts)
                return alerts.Any(a => a.Kind == AlertKind.Unhealthy);
        });
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
    public async Task Stats_scope_limits_the_streams_and_can_be_lifted()
    {
        _docker.Set(FakeDocker.Container("a"), FakeDocker.Container("b"), FakeDocker.Container("c"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a", "b", "c"]);

        _monitor.SetStatsScope(new HashSet<string> { "a", "c" });
        await Eventually(() => _docker.OpenStatsStreams is ["a", "c"]);

        _monitor.SetStatsScope(new HashSet<string> { "b" });
        await Eventually(() => _docker.OpenStatsStreams is ["b"]);

        _monitor.SetStatsScope(null);
        await Eventually(() => _docker.OpenStatsStreams is ["a", "b", "c"]);
    }

    [Fact]
    public async Task A_container_left_out_of_the_scope_keeps_its_last_stats()
    {
        _docker.Set(FakeDocker.Container("a"));
        _monitor.StatsEnabled = true;
        _monitor.Start();
        await Eventually(() => _docker.OpenStatsStreams is ["a"]);
        _docker.PushStats("a", new StatsSample { Pids = 1 });
        await Eventually(() => _monitor.Current.Containers[0].Stats is not null);

        _monitor.SetStatsScope(new HashSet<string>());
        await Eventually(() => _docker.OpenStatsStreams is []);

        Assert.NotNull(_monitor.Current.Containers[0].Stats);
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
    public async Task Disk_usage_and_prunes_are_forwarded_to_docker()
    {
        _docker.Usage = new(new(8, 100, 40), new(2, 1, 1), new(3, 50, 20), new(20, 500, 400));

        Assert.Equal(new UsageLine(8, 100, 40), (await _monitor.GetDiskUsageAsync()).Images);
        Assert.Equal(123, await _monitor.PruneImagesAsync());
        Assert.Equal(456, await _monitor.PruneVolumesAsync());
        Assert.Equal(["df ", "prune-images ", "prune-volumes "], _docker.Actions);
    }

    [Fact]
    public async Task Deleting_an_image_removes_each_tag_or_the_id_when_it_has_none()
    {
        await _monitor.DeleteImageAsync(new ImageInfo("sha256:aaa", ["nginx:1", "nginx:latest"], 10, DateTimeOffset.UnixEpoch, 0));
        await _monitor.DeleteImageAsync(new ImageInfo("sha256:bbb", [], 10, DateTimeOffset.UnixEpoch, 0));

        Assert.Equal(["rmi nginx:1", "rmi nginx:latest", "rmi sha256:bbb"], _docker.Actions);
    }

    [Fact]
    public async Task An_image_used_by_a_container_is_not_touched()
    {
        _docker.Set(FakeDocker.Container("a", imageId: "sha256:used"));
        _monitor.Start();
        await Connected();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _monitor.DeleteImageAsync(new ImageInfo("sha256:used", ["x:1", "x:2"], 1, DateTimeOffset.UnixEpoch, -1)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _monitor.DeleteImageAsync(new ImageInfo("sha256:other", ["y:1"], 1, DateTimeOffset.UnixEpoch, 2)));

        Assert.Empty(_docker.Actions);
    }

    [Fact]
    public async Task Volumes_are_listed_and_deleted_by_name()
    {
        _docker.Volumes = [new DockerVolume("db-data", "local", 10, 0)];

        Assert.Equal("db-data", (await _monitor.ListVolumesAsync()).Single().Name);
        await _monitor.DeleteVolumeAsync(_docker.Volumes[0]);

        Assert.Equal(["rmvol db-data"], _docker.Actions);
    }

    [Fact]
    public async Task Networks_are_deleted_by_id_but_built_in_ones_never()
    {
        await _monitor.DeleteNetworkAsync(new DockerNetwork("n1", "shop_default", "bridge", "local", DateTimeOffset.UnixEpoch));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _monitor.DeleteNetworkAsync(new DockerNetwork("n2", "bridge", "bridge", "local", DateTimeOffset.UnixEpoch)));

        Assert.Equal(["rmnet n1"], _docker.Actions);
    }

    [Fact]
    public async Task Counts_the_containers_on_a_user_defined_network()
    {
        _docker.Set(
            FakeDocker.Container("a") with { Networks = "shop_default, other" },
            FakeDocker.Container("b") with { Networks = "other" },
            FakeDocker.Container("c"));
        _monitor.Start();
        await Connected();

        Assert.Equal(["a"], _monitor.ContainersOnNetwork("shop_default"));
        Assert.Equal(["a", "b"], _monitor.ContainersOnNetwork("other"));
        Assert.Empty(_monitor.ContainersOnNetwork("shop"));
    }

    [Fact]
    public async Task Inspecting_returns_the_json_and_the_containers_that_use_the_thing()
    {
        _docker.Set(
            FakeDocker.Container("a", imageId: "sha256:img") with { Networks = "net" },
            FakeDocker.Container("b"));
        _docker.VolumeUsers = ["db"];
        _monitor.Start();
        await Connected();

        var image = await _monitor.InspectImageAsync(new ImageInfo("sha256:img", ["x:1"], 1, DateTimeOffset.UnixEpoch, 1));
        var volume = await _monitor.InspectVolumeAsync(new DockerVolume("vol", "local", 1, 1));
        var network = await _monitor.InspectNetworkAsync(new DockerNetwork("nid", "net", "bridge", "local", DateTimeOffset.UnixEpoch));

        Assert.Equal(["a"], image.UsedBy);
        Assert.Equal(["db"], volume.UsedBy);
        Assert.Equal(["a"], network.UsedBy);
        Assert.Equal(["inspect Image sha256:img", "inspect Volume vol", "inspect Network nid"], _docker.Actions);
    }

    [Fact]
    public async Task Actions_are_forwarded_to_docker()
    {
        await _monitor.StartContainerAsync("a");
        await _monitor.StopContainerAsync("b");
        await _monitor.RestartContainerAsync("c");
        await _monitor.PauseContainerAsync("d");
        await _monitor.UnpauseContainerAsync("d");
        await _monitor.KillContainerAsync("e");

        Assert.Equal(["start a", "stop b", "restart c", "pause d", "unpause d", "kill e"], _docker.Actions);
    }

    [Fact]
    public async Task Logs_are_streamed_from_docker()
    {
        _docker.LogChunks = ["one\n", "two\n"];

        var chunks = await _monitor.StreamLogsAsync("a", 200, follow: true, CancellationToken.None).ToListAsync();

        Assert.Equal(["one\n", "two\n"], chunks);
        Assert.Equal(("a", 200, true), _docker.LastLogsRequest);
    }

    [Fact]
    public async Task Deleting_a_container_keeps_its_image_unless_asked()
    {
        _docker.Set(FakeDocker.Container("a", ContainerState.Exited));
        _monitor.Start();
        await Connected();

        await _monitor.DeleteContainerAsync("a", alsoImage: false);
        Assert.Equal(["rm a"], _docker.Actions);

        await _monitor.DeleteContainerAsync("a", alsoImage: true);
        Assert.Equal(["rm a", "rm a", "rmi image:latest"], _docker.Actions);
    }

    [Fact]
    public async Task A_shared_image_is_never_deleted()
    {
        _docker.Set(
            FakeDocker.Container("a", ContainerState.Exited, imageId: "sha256:shared"),
            FakeDocker.Container("b", ContainerState.Exited, imageId: "sha256:shared"),
            FakeDocker.Container("c", imageId: "sha256:other"));
        _monitor.Start();
        await Connected();

        Assert.True(_monitor.IsImageShared("sha256:shared", "a"));
        Assert.False(_monitor.IsImageShared("sha256:other", "c"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _monitor.DeleteContainerAsync("a", alsoImage: true));
        Assert.Empty(_docker.Actions);
    }

    [Theory]
    [InlineData("sha256:abcdef", "sha256:abcdef0123")]
    [InlineData("abcdef", "sha256:abcdef0123")]
    public async Task An_image_shown_as_a_bare_id_is_deleted_by_id(string image, string imageId)
    {
        _docker.Set(FakeDocker.Container("a", ContainerState.Exited, image: image, imageId: imageId));
        _monitor.Start();
        await Connected();

        await _monitor.DeleteContainerAsync("a", alsoImage: true);

        Assert.Equal(["rm a", $"rmi {imageId}"], _docker.Actions);
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
