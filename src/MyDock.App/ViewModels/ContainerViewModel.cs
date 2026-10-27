using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.App.Services;
using MyDock.Core.Docker;
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
    private readonly Settings _settings;
    private readonly Action<ContainerViewModel> _requestDelete;
    private readonly Action<ContainerViewModel> _showLogs;
    private readonly Action<string> _toggleGroup;
    private readonly Func<string, ProjectAction, Task> _runGroup;
    private readonly Queue<double> _cpuSamples = new(HistoryLength);
    private ContainerStats? _lastStats;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string? _portsText;
    [ObservableProperty] private string? _networksText;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isPaused;

    // The daemon refuses to pause a container that is restarting.
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _isMoreOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublicPorts))]
    private IReadOnlyList<int> _publicPorts = [];
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
    [ObservableProperty] private bool _isGroupCollapsed;
    [ObservableProperty] private int _groupCount;
    [ObservableProperty] private bool _isGroupBusy;
    [ObservableProperty] private string? _groupError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComposeHeader), nameof(IsPlainHeader))]
    private bool _showGroupHeader;

    public ContainerViewModel(
        ContainerMonitor monitor,
        ContainerSnapshot snapshot,
        Settings settings,
        Action<ContainerViewModel> requestDelete,
        Action<string> toggleGroup,
        Func<string, ProjectAction, Task> runGroup,
        Action<ContainerViewModel> showLogs)
    {
        _monitor = monitor;
        _settings = settings;
        _requestDelete = requestDelete;
        _showLogs = showLogs;
        _toggleGroup = toggleGroup;
        _runGroup = runGroup;
        Id = snapshot.Info.Id;
        Update(snapshot);
    }

    public string Id { get; }

    /// <summary>The 12-character id that <c>docker ps</c> shows.</summary>
    public string ShortId => Id[..Math.Min(12, Id.Length)];

    public bool HasPublicPorts => PublicPorts.Count > 0;

    public string Image { get; private set; } = "";

    public string ImageId { get; private set; } = "";

    public string? ComposeProject { get; private set; }

    public bool IsUnhealthy { get; private set; }

    public bool IsComposeHeader => ShowGroupHeader && ComposeProject is not null;

    public bool IsPlainHeader => ShowGroupHeader && ComposeProject is null;

    public double CpuPercent => _lastStats?.CpuPercent ?? 0;

    public ulong MemoryUsed => _lastStats?.MemoryUsed ?? 0;

    public void Update(ContainerSnapshot snapshot)
    {
        var (info, stats) = snapshot;

        Name = info.Name;
        Image = info.Image;
        ImageId = info.ImageId;
        Subtitle = $"{info.Image} · {info.Status}";
        IsActive = info.IsActive;
        IsPaused = info.State == ContainerState.Paused;
        CanPause = info.State == ContainerState.Running;
        Indicator = IndicatorFor(info, stats, _settings);
        ComposeProject = info.ComposeProject;
        IsUnhealthy = info.Health == HealthState.Unhealthy;
        // Rebuilding the list on every refresh would redraw the port buttons each second.
        var ports = info.PublicPorts ?? [];
        if (!PublicPorts.SequenceEqual(ports))
            PublicPorts = ports;
        PortsText = info.Ports is null ? null : "Ports: " + info.Ports;
        NetworksText = info.Networks is null ? null : "Network: " + info.Networks;

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
        Details = $"{info.Image}\n{Format.Percent(stats.MemoryPercent)} of memory · {stats.Pids} processes";
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

    [RelayCommand]
    private Task PauseAsync() => RunAsync(_monitor.PauseContainerAsync);

    [RelayCommand]
    private Task UnpauseAsync() => RunAsync(_monitor.UnpauseContainerAsync);

    [RelayCommand]
    private Task KillAsync() => RunAsync(_monitor.KillContainerAsync);

    public void OpenPort(int port)
    {
        Error = null;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("xdg-open", $"http://localhost:{port}"));
        }
        catch (Exception ex)
        {
            Error = $"Could not open the browser: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Delete() => _requestDelete(this);

    [RelayCommand]
    private void ShowLogs() => _showLogs(this);

    [RelayCommand]
    private void OpenShell()
    {
        Error = null;
        try
        {
            ContainerShell.Open(Id, _settings.Terminal);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void ToggleGroup() => _toggleGroup(ComposeProject!);

    [RelayCommand]
    private async Task GroupActionAsync(ProjectAction action)
    {
        GroupError = null;
        IsGroupBusy = true;
        try
        {
            await _runGroup(ComposeProject!, action);
        }
        catch (Exception ex)
        {
            GroupError = ex.Message;
        }
        finally
        {
            IsGroupBusy = false;
        }
    }

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

    private static Indicator IndicatorFor(ContainerInfo info, ContainerStats? stats, Settings settings)
    {
        var state = info.State switch
        {
            ContainerState.Running when info.Health == HealthState.Unhealthy => Indicator.Bad,
            ContainerState.Running when info.Health == HealthState.Starting => Indicator.Warn,
            ContainerState.Running => Indicator.Ok,
            ContainerState.Paused or ContainerState.Restarting => Indicator.Warn,
            ContainerState.Dead => Indicator.Bad,
            _ => Indicator.Off,
        };
        if (state != Indicator.Ok || stats is null)
            return state;

        // Load only turns a healthy running container yellow or red.
        var cpu = Thresholds.Classify(stats.CpuPercent, settings.CpuWarn, settings.CpuBad);
        var memory = Thresholds.Classify(stats.MemoryPercent, settings.MemoryWarn, settings.MemoryBad);
        return (Load)Math.Max((int)cpu, (int)memory) switch
        {
            Load.Critical => Indicator.Bad,
            Load.High => Indicator.Warn,
            _ => Indicator.Ok,
        };
    }
}
