using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Docker.DotNet;
using Docker.DotNet.Models;
using MyDock.Core.Models;

namespace MyDock.Core.Docker;

public sealed class DockerService : IDockerService
{
    public const string DefaultEndpoint = "unix:///var/run/docker.sock";
    private const string ComposeProjectLabel = "com.docker.compose.project";

    private readonly DockerClient _client;

    public DockerService(Uri? endpoint = null)
    {
        Endpoint = endpoint ?? ResolveEndpoint();
        _client = new DockerClientBuilder().WithEndpoint(Endpoint).Build();
    }

    public Uri Endpoint { get; }

    public static Uri ResolveEndpoint()
    {
        var host = Environment.GetEnvironmentVariable("DOCKER_HOST");
        return new Uri(string.IsNullOrWhiteSpace(host) ? DefaultEndpoint : host);
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct)
    {
        var containers = await _client.Containers
            .ListContainersAsync(new ContainersListParameters { All = true }, ct)
            .ConfigureAwait(false);
        return containers.Select(ToInfo).ToList();
    }

    public IAsyncEnumerable<StatsSample> StreamStatsAsync(string id, CancellationToken ct) =>
        Bridge<ContainerStatsResponse, StatsSample>(
            (progress, token) => _client.Containers.GetContainerStatsAsync(
                id, new ContainerStatsParameters { Stream = true }, progress, token),
            ToSample,
            ct);

    public IAsyncEnumerable<ContainerEvent> WatchEventsAsync(DateTimeOffset since, CancellationToken ct)
    {
        var parameters = new ContainerEventsParameters
        {
            Since = since.ToUnixTimeSeconds().ToString(),
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["type"] = new Dictionary<string, bool> { ["container"] = true },
            },
        };
        return Bridge<Message, ContainerEvent>(
            (progress, token) => _client.System.MonitorEventsAsync(parameters, progress, token),
            MapEvent,
            ct);
    }

    private static ContainerEvent? MapEvent(Message message)
    {
        if (message.Actor?.ID is not { } id)
            return null;

        string? Attribute(string key) => message.Actor.Attributes is { } a && a.TryGetValue(key, out var value) ? value : null;
        return new ContainerEvent(message.Action ?? "", id, Attribute("name"), int.TryParse(Attribute("exitCode"), out var code) ? code : null);
    }

    public IAsyncEnumerable<string> StreamLogsAsync(string id, int tail, bool follow, CancellationToken ct) =>
        Bridge<string, string>(
            (progress, token) => _client.Containers.GetContainerLogsAsync(
                id,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = tail.ToString(), Follow = follow },
                progress,
                token),
            // The library reports one line at a time with its newline already stripped.
            line => line + "\n",
            ct);

    public Task StartAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.StartContainerAsync(id, new ContainerStartParameters(), ct));

    public Task StopAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.StopContainerAsync(id, new ContainerStopParameters(), ct));

    public Task RestartAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.RestartContainerAsync(id, new ContainerRestartParameters(), ct));

    public Task PauseAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.PauseContainerAsync(id, ct));

    public Task UnpauseAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.UnpauseContainerAsync(id, ct));

    public Task KillAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.KillContainerAsync(id, new ContainerKillParameters(), ct));

    public Task RemoveContainerAsync(string id, CancellationToken ct) =>
        RunAsync(() => _client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters(), ct));

    public Task RemoveImageAsync(string reference, CancellationToken ct) =>
        RunAsync(() => _client.Images.DeleteImageAsync(reference, new ImageDeleteParameters(), ct));

    public void Dispose() => _client.Dispose();

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (DockerApiException ex)
        {
            throw new DockerOperationException(DockerOperationException.Describe(ex.ResponseBody, ex.Message), ex);
        }
    }

    internal static ContainerInfo ToInfo(ContainerListResponse c)
    {
        string? project = null;
        c.Labels?.TryGetValue(ComposeProjectLabel, out project);
        var name = c.Names?.FirstOrDefault()?.TrimStart('/');
        return new ContainerInfo(
            c.ID,
            string.IsNullOrEmpty(name) ? ShortId(c.ID) : name,
            c.Image ?? "",
            c.ImageID ?? "",
            ParseState(c.State),
            ParseHealth(c.Health?.Status),
            c.Status ?? "",
            new DateTimeOffset(DateTime.SpecifyKind(c.Created, DateTimeKind.Utc)),
            string.IsNullOrEmpty(project) ? null : project,
            FormatPorts(c.Ports?.Select(p => ((long)p.PrivatePort, (long)(p.PublicPort ?? 0), p.Type))),
            FormatNetworks(c.NetworkSettings?.Networks),
            WebPorts(c.Ports?.Select(p => ((long)(p.PublicPort ?? 0), p.Type))));
    }

    // Docker lists a published port once per host address (IPv4 and IPv6): the text is the same, so dedupe.
    internal static string? FormatPorts(IEnumerable<(long Private, long Public, string Type)>? ports)
    {
        var text = (ports ?? [])
            .OrderBy(p => p.Private).ThenBy(p => p.Public)
            .Select(p => p.Public > 0 ? $"{p.Public}→{p.Private}/{p.Type}" : $"{p.Private}/{p.Type}")
            .Distinct()
            .ToList();
        return text.Count == 0 ? null : string.Join(", ", text);
    }

    // Only TCP ports can be opened in a browser; IPv4 and IPv6 bindings of the same port collapse into one.
    internal static IReadOnlyList<int>? WebPorts(IEnumerable<(long Public, string Type)>? ports)
    {
        var list = (ports ?? [])
            .Where(p => p.Public > 0 && p.Type == "tcp")
            .Select(p => (int)p.Public)
            .Distinct()
            .Order()
            .ToList();
        return list.Count == 0 ? null : list;
    }

    // The default networks say nothing about how the container is wired.
    internal static string? FormatNetworks(IDictionary<string, EndpointSettings>? networks)
    {
        var names = (networks?.Keys ?? [])
            .Where(n => n is not ("bridge" or "host" or "none"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    internal static StatsSample ToSample(ContainerStatsResponse s)
    {
        var cpu = s.CPUStats;
        var preCpu = s.PreCPUStats;
        var memory = s.MemoryStats;
        var blkio = s.BlkioStats?.IoServiceBytesRecursive;

        return new StatsSample
        {
            Read = new DateTimeOffset(s.Read.ToUniversalTime()),
            CpuTotal = cpu?.CPUUsage?.TotalUsage ?? 0,
            PreCpuTotal = preCpu?.CPUUsage?.TotalUsage ?? 0,
            SystemCpu = cpu?.SystemUsage ?? 0,
            PreSystemCpu = preCpu?.SystemUsage ?? 0,
            OnlineCpus = cpu?.OnlineCPUs is > 0 and var online
                ? online
                : (uint)Math.Max(1, cpu?.CPUUsage?.PercpuUsage?.Count ?? 0),
            MemoryUsage = memory?.Usage ?? 0,
            MemoryInactiveFile = InactiveFile(memory?.Stats),
            MemoryLimit = memory?.Limit ?? 0,
            NetRxBytes = Sum(s.Networks?.Values, n => n.RxBytes),
            NetTxBytes = Sum(s.Networks?.Values, n => n.TxBytes),
            BlockReadBytes = Sum(blkio?.Where(e => IsOp(e, "read")), e => e.Value),
            BlockWriteBytes = Sum(blkio?.Where(e => IsOp(e, "write")), e => e.Value),
            Pids = s.PidsStats?.Current ?? 0,
        };
    }

    // cgroup v2 reports "inactive_file", cgroup v1 "total_inactive_file".
    private static ulong InactiveFile(IDictionary<string, ulong>? stats)
    {
        if (stats is null)
            return 0;
        return stats.TryGetValue("inactive_file", out var v2) ? v2
            : stats.TryGetValue("total_inactive_file", out var v1) ? v1
            : 0;
    }

    private static bool IsOp(BlkioStatEntry entry, string op) =>
        string.Equals(entry.Op, op, StringComparison.OrdinalIgnoreCase);

    private static ulong Sum<T>(IEnumerable<T>? items, Func<T, ulong> selector)
    {
        ulong total = 0;
        if (items is not null)
        {
            foreach (var item in items)
                total += selector(item);
        }

        return total;
    }

    private static string ShortId(string id) => id.Length > 12 ? id[..12] : id;

    private static ContainerState ParseState(string? state) =>
        Enum.TryParse<ContainerState>(state, ignoreCase: true, out var parsed) ? parsed : ContainerState.Unknown;

    private static HealthState ParseHealth(string? health) =>
        Enum.TryParse<HealthState>(health, ignoreCase: true, out var parsed) ? parsed : HealthState.None;

    /// <summary>Adapts the library's push-style <see cref="IProgress{T}"/> streams to a pull-style async sequence.</summary>
    private static async IAsyncEnumerable<TOut> Bridge<TIn, TOut>(
        Func<IProgress<TIn>, CancellationToken, Task> run,
        Func<TIn, TOut?> map,
        [EnumeratorCancellation] CancellationToken ct)
        where TOut : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateUnbounded<TOut>(new UnboundedChannelOptions { SingleReader = true });
        var progress = new CallbackProgress<TIn>(item =>
        {
            if (map(item) is { } mapped)
                channel.Writer.TryWrite(mapped);
        });

        var pump = Task.Run(async () =>
        {
            try
            {
                await run(progress, cts.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            cts.Cancel();
            await pump.ConfigureAwait(false);
        }
    }

    // Progress<T> would post to a synchronization context; samples must be handed over inline.
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
