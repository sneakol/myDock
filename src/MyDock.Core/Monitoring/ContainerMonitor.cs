using System.Threading.Channels;
using MyDock.Core.Docker;
using MyDock.Core.Models;
using MyDock.Core.Localization;

namespace MyDock.Core.Monitoring;

public sealed record MonitorOptions
{
    /// <summary>Wait after an event before re-listing, so a burst of events costs one refresh.</summary>
    public TimeSpan EventDebounce { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>How often the list is re-read while stats are enabled, to keep status texts fresh.</summary>
    public TimeSpan PeriodicRefresh { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ReconnectMin { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectMax { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Wait before reopening a stats stream that ended on its own.</summary>
    public TimeSpan StatsRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Keeps an up-to-date picture of the daemon's containers: the list follows Docker events,
/// and per-container stats streams run only while <see cref="StatsEnabled"/> is set.
/// </summary>
public sealed class ContainerMonitor : IAsyncDisposable
{
    private readonly IDockerService _docker;
    private readonly MonitorOptions _options;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Tracked> _containers = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly AlertTracker _alerts = new();

    private ConnectionState _connection = ConnectionState.Connecting;
    private bool _statsEnabled;
    private IReadOnlySet<string>? _statsScope;
    private CancellationToken _sessionToken;
    private ChannelWriter<bool>? _refreshRequests;
    private volatile MonitorSnapshot _current = MonitorSnapshot.Initial;
    private Task? _loop;

    public ContainerMonitor(IDockerService docker, MonitorOptions? options = null)
    {
        _docker = docker;
        _options = options ?? new MonitorOptions();
    }

    /// <summary>Raised from a background thread whenever <see cref="Current"/> has been replaced.</summary>
    public event Action? Changed;

    /// <summary>Raised from a background thread for a crash, OOM kill, unhealthy transition or restart loop. Handler exceptions are swallowed.</summary>
    public event Action<ContainerAlert>? Alerted;

    public MonitorSnapshot Current => _current;

    public bool StatsEnabled
    {
        get
        {
            lock (_gate)
                return _statsEnabled;
        }
        set
        {
            lock (_gate)
            {
                if (_statsEnabled == value)
                    return;
                _statsEnabled = value;
                ReconcileStats();
            }

            if (value)
                RequestRefresh();
        }
    }

    /// <summary>
    /// Limits the stats streams to these containers, e.g. the rows that are on screen; null means all running ones.
    /// A container left out keeps its last stats, so sorting by load does not jump around.
    /// </summary>
    public void SetStatsScope(IReadOnlySet<string>? ids)
    {
        lock (_gate)
        {
            if (_statsScope is null ? ids is null : ids is not null && _statsScope.SetEquals(ids))
                return;

            _statsScope = ids is null ? null : new HashSet<string>(ids);
            ReconcileStats();
        }
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    public IAsyncEnumerable<string> StreamLogsAsync(string id, int tail, bool follow, CancellationToken ct) =>
        _docker.StreamLogsAsync(id, tail, follow, ct);

    public Task StartContainerAsync(string id, CancellationToken ct = default) => _docker.StartAsync(id, ct);

    public Task StopContainerAsync(string id, CancellationToken ct = default) => _docker.StopAsync(id, ct);

    public Task RestartContainerAsync(string id, CancellationToken ct = default) => _docker.RestartAsync(id, ct);

    public Task PauseContainerAsync(string id, CancellationToken ct = default) => _docker.PauseAsync(id, ct);

    public Task UnpauseContainerAsync(string id, CancellationToken ct = default) => _docker.UnpauseAsync(id, ct);

    public Task KillContainerAsync(string id, CancellationToken ct = default) => _docker.KillAsync(id, ct);

    public Task<DiskUsage> GetDiskUsageAsync(CancellationToken ct = default) => _docker.GetDiskUsageAsync(ct);

    public Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken ct = default) => _docker.ListImagesAsync(ct);

    /// <summary>
    /// Removes every tag of the image, which removes the image itself with the last one; a dangling image goes by id.
    /// Refuses an image that a container uses, because the daemon would still untag all but the last tag.
    /// </summary>
    public async Task DeleteImageAsync(ImageInfo image, CancellationToken ct = default)
    {
        if (image.Containers > 0 || Current.Containers.Any(c => c.Info.ImageId == image.Id))
            throw new InvalidOperationException(Loc.Get("Err.ImageUsed"));

        if (image.Tags.Count == 0)
        {
            await _docker.RemoveImageAsync(image.Id, ct).ConfigureAwait(false);
            return;
        }

        foreach (var tag in image.Tags)
            await _docker.RemoveImageAsync(tag, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(CancellationToken ct = default) => _docker.ListVolumesAsync(ct);

    public Task DeleteVolumeAsync(DockerVolume volume, CancellationToken ct = default) => _docker.RemoveVolumeAsync(volume.Name, ct);

    public Task<IReadOnlyList<DockerNetwork>> ListNetworksAsync(CancellationToken ct = default) => _docker.ListNetworksAsync(ct);

    /// <summary>The known containers on the user-defined network <paramref name="name"/>; the built-in ones are not tracked.</summary>
    public IReadOnlyList<string> ContainersOnNetwork(string name) =>
        Current.Containers.Where(c => c.Info.Networks?.Split(", ").Contains(name) == true).Select(c => c.Info.Name).ToList();

    public async Task<Inspection> InspectImageAsync(ImageInfo image, CancellationToken ct = default) => new(
        await _docker.InspectAsync(InspectKind.Image, image.Id, ct).ConfigureAwait(false),
        Current.Containers.Where(c => c.Info.ImageId == image.Id).Select(c => c.Info.Name).ToList());

    public async Task<Inspection> InspectVolumeAsync(DockerVolume volume, CancellationToken ct = default) => new(
        await _docker.InspectAsync(InspectKind.Volume, volume.Name, ct).ConfigureAwait(false),
        await _docker.ContainersUsingVolumeAsync(volume.Name, ct).ConfigureAwait(false));

    public async Task<Inspection> InspectNetworkAsync(DockerNetwork network, CancellationToken ct = default) => new(
        await _docker.InspectAsync(InspectKind.Network, network.Id, ct).ConfigureAwait(false),
        ContainersOnNetwork(network.Name));

    public Task DeleteNetworkAsync(DockerNetwork network, CancellationToken ct = default) =>
        network.IsBuiltIn
            ? throw new InvalidOperationException(Loc.Get("Err.BuiltInNetwork", network.Name))
            : _docker.RemoveNetworkAsync(network.Id, ct);

    public Task<long> PruneImagesAsync(CancellationToken ct = default) => _docker.PruneImagesAsync(ct);

    public Task<long> PruneVolumesAsync(CancellationToken ct = default) => _docker.PruneVolumesAsync(ct);

    /// <summary>True when a container other than <paramref name="exceptContainerId"/>, running or not, uses the image.</summary>
    public bool IsImageShared(string imageId, string exceptContainerId) =>
        imageId.Length > 0 && Current.Containers.Any(c => c.Info.Id != exceptContainerId && c.Info.ImageId == imageId);

    /// <summary>Removes a container and, when asked, its image, unless another container still uses that image.</summary>
    public async Task DeleteContainerAsync(string id, bool alsoImage, CancellationToken ct = default)
    {
        var info = Current.Containers.FirstOrDefault(c => c.Info.Id == id)?.Info;
        if (alsoImage && info is null)
            throw new InvalidOperationException(Loc.Get("Err.ContainerGone"));
        if (alsoImage && IsImageShared(info!.ImageId, id))
            throw new InvalidOperationException(Loc.Get("Err.ImageUsedOther"));

        await _docker.RemoveContainerAsync(id, ct).ConfigureAwait(false);
        if (!alsoImage)
            return;

        try
        {
            await _docker.RemoveImageAsync(ImageReference(info!), ct).ConfigureAwait(false);
        }
        catch (DockerOperationException ex)
        {
            throw new DockerOperationException(Loc.Get("Err.ImageNotRemoved", ex.Message), ex);
        }
    }

    // When a tag has moved on, the list shows a bare id instead of a name; deleting by name would then fail.
    private static string ImageReference(ContainerInfo info) =>
        info.Image.StartsWith("sha256:", StringComparison.Ordinal) || info.ImageId.StartsWith("sha256:" + info.Image, StringComparison.Ordinal)
            ? info.ImageId
            : info.Image;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
            await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>Events that can change what the list shows; exec_* (health checks) and the like are noise.</summary>
    internal static bool IsRelevant(string action) =>
        action is "create" or "start" or "restart" or "stop" or "die" or "kill" or "pause" or "unpause"
            or "destroy" or "rename" or "oom" or "update"
        || action.StartsWith("health_status", StringComparison.Ordinal);

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = _options.ReconnectMin;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A session that got as far as connecting earns a fast first retry.
                if (Disconnect(DockerErrors.Classify(ex)))
                    delay = _options.ReconnectMin;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.ReconnectMax.Ticks));
        }
    }

    /// <summary>One connection to the daemon. Returns only by throwing: cancellation, or the reason the daemon was lost.</summary>
    private async Task RunSessionAsync(CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

        lock (_gate)
        {
            _sessionToken = session.Token;
            _refreshRequests = requests.Writer;
        }

        // Subscribe from slightly in the past so nothing that happens while listing is missed.
        var watcher = WatchEventsAsync(DateTimeOffset.UtcNow.AddSeconds(-1), requests.Writer, session.Token);
        var ticker = TickAsync(session.Token);
        try
        {
            await RefreshAsync(session.Token).ConfigureAwait(false);
            await foreach (var _ in requests.Reader.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
                await Task.Delay(_options.EventDebounce, session.Token).ConfigureAwait(false);
                await RefreshAsync(session.Token).ConfigureAwait(false);
            }

            throw new IOException("The Docker event stream was closed.");
        }
        finally
        {
            lock (_gate)
                _refreshRequests = null;
            session.Cancel();
            await Task.WhenAll(watcher, ticker).ConfigureAwait(false);
        }
    }

    /// <summary>Requests a refresh for every relevant event; ends the session when the stream ends. Never throws.</summary>
    private async Task WatchEventsAsync(DateTimeOffset since, ChannelWriter<bool> requests, CancellationToken ct)
    {
        try
        {
            await foreach (var e in _docker.WatchEventsAsync(since, ct).ConfigureAwait(false))
            {
                if (_alerts.Process(e, DateTimeOffset.UtcNow) is { } alert)
                {
                    try
                    {
                        Alerted?.Invoke(alert);
                    }
                    catch (Exception)
                    {
                        // A broken handler must not end the event session.
                    }
                }
                if (IsRelevant(e.Action))
                    requests.TryWrite(true);
            }

            requests.TryComplete();
        }
        catch (Exception ex)
        {
            requests.TryComplete(ex);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.PeriodicRefresh);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (StatsEnabled)
                    RequestRefresh();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RequestRefresh()
    {
        ChannelWriter<bool>? requests;
        lock (_gate)
            requests = _refreshRequests;
        requests?.TryWrite(true);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var list = await _docker.ListAsync(ct).ConfigureAwait(false);

        lock (_gate)
        {
            var seen = new HashSet<string>(list.Count);
            foreach (var info in list)
            {
                seen.Add(info.Id);
                if (_containers.TryGetValue(info.Id, out var tracked))
                    tracked.Info = info;
                else
                    _containers[info.Id] = new Tracked(info);
            }

            foreach (var id in _containers.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _containers[id].StopStats();
                _containers.Remove(id);
            }

            _connection = ConnectionState.Connected;
            ReconcileStats();
        }

        Publish();
    }

    /// <summary>Starts and stops stats streams so that exactly the running containers have one. Caller holds the gate.</summary>
    private void ReconcileStats()
    {
        foreach (var (id, tracked) in _containers)
        {
            var wanted = _statsEnabled
                && _connection == ConnectionState.Connected
                && tracked.Info.State is ContainerState.Running or ContainerState.Paused
                && (_statsScope is null || _statsScope.Contains(id));

            if (wanted && tracked.StatsCts is null)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken);
                tracked.StatsCts = cts;
                _ = Task.Run(() => PumpStatsAsync(id, tracked, cts));
            }
            else if (!wanted)
            {
                tracked.StopStats();
                if (!tracked.Info.IsActive)
                    tracked.Stats = null;
            }
        }
    }

    private async Task PumpStatsAsync(string id, Tracked tracked, CancellationTokenSource cts)
    {
        using var _ = cts;
        var ct = cts.Token;
        var rates = new RateTracker();
        try
        {
            await foreach (var sample in _docker.StreamStatsAsync(id, ct).ConfigureAwait(false))
            {
                var stats = rates.Next(sample);
                lock (_gate)
                {
                    if (!ReferenceEquals(tracked.StatsCts, cts))
                        return;
                    tracked.Stats = stats;
                }

                Publish();
            }
        }
        catch (Exception)
        {
            // Cancelled, container gone, or daemon lost. The session loop notices the last two by itself.
        }

        // The stream ended although nobody cancelled it: free the slot and let a refresh decide whether to reopen it.
        lock (_gate)
        {
            if (!ReferenceEquals(tracked.StatsCts, cts))
                return;
            tracked.StatsCts = null;
        }

        try
        {
            await Task.Delay(_options.StatsRetryDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RequestRefresh();
    }

    /// <returns>True if the monitor was connected until now.</returns>
    private bool Disconnect(ConnectionState reason)
    {
        bool wasConnected;
        lock (_gate)
        {
            wasConnected = _connection == ConnectionState.Connected;
            _connection = reason;
            foreach (var tracked in _containers.Values)
                tracked.StopStats();
            _containers.Clear();
        }

        Publish();
        return wasConnected;
    }

    private void Publish()
    {
        lock (_gate)
        {
            _current = new MonitorSnapshot(
                _connection,
                _containers.Values
                    .OrderBy(t => t.Info.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Info.Id, StringComparer.Ordinal)
                    .Select(t => new ContainerSnapshot(t.Info, t.Stats))
                    .ToList());
        }

        Changed?.Invoke();
    }

    private sealed class Tracked(ContainerInfo info)
    {
        public ContainerInfo Info { get; set; } = info;
        public ContainerStats? Stats { get; set; }

        /// <summary>Non-null while a stats stream is open; owned and disposed by the pump.</summary>
        public CancellationTokenSource? StatsCts { get; set; }

        public void StopStats()
        {
            // Clear the slot first: the pump may resume inside Cancel() and checks whether it still owns it.
            var cts = StatsCts;
            StatsCts = null;
            cts?.Cancel();
        }
    }
}
