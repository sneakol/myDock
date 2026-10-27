using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App.ViewModels;

public enum Indicator
{
    Off,
    Ok,
    Warn,
    Bad,
}

public sealed partial class ContainerViewModel : ObservableObject
{
    public const int HistoryLength = 60;
    private const string NoValue = "–";

    private readonly ContainerMonitor _monitor;
    private readonly Queue<double> _cpuSamples = new(HistoryLength);
    private ContainerStats? _lastStats;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private Indicator _indicator;
    [ObservableProperty] private string _cpuText = NoValue;
    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private string _memoryText = NoValue;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _networkText = NoValue;
    [ObservableProperty] private string _diskText = NoValue;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _groupHeader;
    [ObservableProperty] private bool _showGroupHeader;

    public ContainerViewModel(ContainerMonitor monitor, ContainerSnapshot snapshot)
    {
        _monitor = monitor;
        Id = snapshot.Info.Id;
        Update(snapshot);
    }

    public string Id { get; }

    public string? ComposeProject { get; private set; }

    public double CpuPercent => _lastStats?.CpuPercent ?? 0;

    public ulong MemoryUsed => _lastStats?.MemoryUsed ?? 0;

    public void Update(ContainerSnapshot snapshot)
    {
        var (info, stats) = snapshot;

        Name = info.Name;
        Subtitle = $"{info.Image} · {info.Status}";
        IsActive = info.IsActive;
        Indicator = IndicatorFor(info);
        ComposeProject = info.ComposeProject;

        // The same stats object is republished whenever another container changes: only a new one is a new sample.
        if (ReferenceEquals(stats, _lastStats))
            return;

        _lastStats = stats;
        if (stats is null)
        {
            ResetHistory();
            CpuText = MemoryText = NetworkText = DiskText = NoValue;
            MemoryPercent = 0;
            Details = info.Image;
            return;
        }

        if (_cpuSamples.Count == HistoryLength)
            _cpuSamples.Dequeue();
        _cpuSamples.Enqueue(stats.CpuPercent);
        CpuHistory = _cpuSamples.ToArray();

        CpuText = Format.Percent(stats.CpuPercent);
        MemoryText = $"{Format.Memory(stats.MemoryUsed)} / {Format.Memory(stats.MemoryLimit)}";
        MemoryPercent = stats.MemoryPercent;
        NetworkText = $"RX {Format.Rate(stats.NetRxRate)}  TX {Format.Rate(stats.NetTxRate)}";
        DiskText = $"R {Format.Rate(stats.BlockReadRate)}  W {Format.Rate(stats.BlockWriteRate)}";
        Details = $"{info.Image}\n{Format.Percent(stats.MemoryPercent)} della memoria · {stats.Pids} processi";
    }

    /// <summary>Drops the CPU history, e.g. after the panel was hidden and sampling paused.</summary>
    public void ResetHistory()
    {
        _cpuSamples.Clear();
        CpuHistory = [];
    }

    [RelayCommand]
    private Task StartAsync() => RunAsync(_monitor.StartContainerAsync);

    [RelayCommand]
    private Task StopAsync() => RunAsync(_monitor.StopContainerAsync);

    [RelayCommand]
    private Task RestartAsync() => RunAsync(_monitor.RestartContainerAsync);

    private async Task RunAsync(Func<string, CancellationToken, Task> action)
    {
        Error = null;
        IsBusy = true;
        try
        {
            await action(Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Indicator IndicatorFor(ContainerInfo info) => info.State switch
    {
        ContainerState.Running when info.Health == HealthState.Unhealthy => Indicator.Bad,
        ContainerState.Running when info.Health == HealthState.Starting => Indicator.Warn,
        ContainerState.Running => Indicator.Ok,
        ContainerState.Paused or ContainerState.Restarting => Indicator.Warn,
        ContainerState.Dead => Indicator.Bad,
        _ => Indicator.Off,
    };
}
