using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MyDock.Core.Docker;
using MyDock.Core.Models;

namespace MyDock.Core.Tests;

internal sealed class FakeDocker : IDockerService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Channel<StatsSample>> _stats = [];
    private List<ContainerInfo> _containers = [];
    private Channel<ContainerEvent> _events = Channel.CreateUnbounded<ContainerEvent>();
    private Exception? _failure;
    private int _listCalls;

    public int ListCalls => Volatile.Read(ref _listCalls);

    public List<string> Actions { get; } = [];

    public string[] OpenStatsStreams
    {
        get
        {
            lock (_gate)
                return [.. _stats.Keys.Order()];
        }
    }

    /// <param name="imageId">Unique per container unless given, so sharing an image is opt-in.</param>
    public static ContainerInfo Container(
        string id, ContainerState state = ContainerState.Running, string? name = null,
        string image = "image:latest", string? imageId = null) =>
        new(id, name ?? id, image, imageId ?? "sha256:" + id, state, HealthState.None, state.ToString(), DateTimeOffset.UnixEpoch, null);

    public void Set(params ContainerInfo[] containers)
    {
        lock (_gate)
            _containers = [.. containers];
    }

    public void Raise(string action, string id = "any")
    {
        lock (_gate)
            _events.Writer.TryWrite(new ContainerEvent(action, id));
    }

    public void PushStats(string id, StatsSample sample)
    {
        lock (_gate)
            Assert.True(_stats[id].Writer.TryWrite(sample));
    }

    public void EndStats(string id)
    {
        lock (_gate)
            _stats[id].Writer.TryComplete();
    }

    public void Break(Exception failure)
    {
        lock (_gate)
        {
            _failure = failure;
            _events.Writer.TryComplete(failure);
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            _events = Channel.CreateUnbounded<ContainerEvent>();
            _failure = null;
        }
    }

    public Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _listCalls);
        lock (_gate)
        {
            return _failure is null
                ? Task.FromResult<IReadOnlyList<ContainerInfo>>(_containers)
                : Task.FromException<IReadOnlyList<ContainerInfo>>(_failure);
        }
    }

    public async IAsyncEnumerable<ContainerEvent> WatchEventsAsync(
        DateTimeOffset since, [EnumeratorCancellation] CancellationToken ct)
    {
        Channel<ContainerEvent> events;
        lock (_gate)
        {
            if (_failure is not null)
                throw _failure;
            events = _events;
        }

        await foreach (var e in events.Reader.ReadAllAsync(ct))
            yield return e;
    }

    public async IAsyncEnumerable<StatsSample> StreamStatsAsync(string id, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<StatsSample>();
        lock (_gate)
            _stats[id] = channel;
        try
        {
            await foreach (var sample in channel.Reader.ReadAllAsync(ct))
                yield return sample;
        }
        finally
        {
            lock (_gate)
            {
                if (_stats.GetValueOrDefault(id) == channel)
                    _stats.Remove(id);
            }
        }
    }

    /// <summary>What <see cref="StreamLogsAsync"/> yields, and the arguments of its last call.</summary>
    public string[] LogChunks { get; set; } = [];

    public (string Id, int Tail, bool Follow)? LastLogsRequest { get; private set; }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string id, int tail, bool follow, [EnumeratorCancellation] CancellationToken ct)
    {
        LastLogsRequest = (id, tail, follow);
        foreach (var chunk in LogChunks)
            yield return chunk;
        await Task.CompletedTask;
    }

    public Task StartAsync(string id, CancellationToken ct) => Record("start", id);

    public Task StopAsync(string id, CancellationToken ct) => Record("stop", id);

    public Task RestartAsync(string id, CancellationToken ct) => Record("restart", id);

    public Task PauseAsync(string id, CancellationToken ct) => Record("pause", id);

    public Task UnpauseAsync(string id, CancellationToken ct) => Record("unpause", id);

    public Task KillAsync(string id, CancellationToken ct) => Record("kill", id);

    public List<ImageInfo> Images { get; set; } = [];

    public Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ImageInfo>>(Images);

    public List<DockerVolume> Volumes { get; set; } = [];

    public Task<IReadOnlyList<DockerVolume>> ListVolumesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerVolume>>(Volumes);

    public Task RemoveVolumeAsync(string name, CancellationToken ct) => Record("rmvol", name);

    public List<DockerNetwork> Networks { get; set; } = [];

    public Task<IReadOnlyList<DockerNetwork>> ListNetworksAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerNetwork>>(Networks);

    public Task RemoveNetworkAsync(string id, CancellationToken ct) => Record("rmnet", id);

    public DiskUsage Usage { get; set; } = new(new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), new(0, 0, 0));

    public Task<DiskUsage> GetDiskUsageAsync(CancellationToken ct)
    {
        Record("df", "");
        return Task.FromResult(Usage);
    }

    public Task<long> PruneImagesAsync(CancellationToken ct) => Record("prune-images", "").ContinueWith(_ => 123L, ct);

    public Task<long> PruneVolumesAsync(CancellationToken ct) => Record("prune-volumes", "").ContinueWith(_ => 456L, ct);

    public Task RemoveContainerAsync(string id, CancellationToken ct) => Record("rm", id);

    public Task RemoveImageAsync(string reference, CancellationToken ct) => Record("rmi", reference);

    public void Dispose()
    {
    }

    private Task Record(string action, string id)
    {
        lock (_gate)
            Actions.Add($"{action} {id}");
        return Task.CompletedTask;
    }
}
