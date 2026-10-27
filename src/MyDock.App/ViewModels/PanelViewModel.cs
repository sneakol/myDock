using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.App.Services;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App.ViewModels;

public sealed partial class PanelViewModel : ObservableObject
{
    private const string UngroupedHeader = "Altri";

    private readonly ContainerMonitor _monitor;
    private readonly SettingsStore _store;
    private readonly Settings _settings;
    private readonly Dictionary<string, ContainerViewModel> _byId = [];
    private int _updateQueued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnecting), nameof(IsConnected), nameof(IsUnavailable), nameof(IsPermissionDenied))]
    private ConnectionState _connection;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasStopped;
    [ObservableProperty] private string _stoppedHeader = "";
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isStoppedExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    private SortMode _sort;

    public PanelViewModel(ContainerMonitor monitor, SettingsStore store, string endpoint)
    {
        _monitor = monitor;
        _store = store;
        _settings = store.Load();
        _isPinned = _settings.Pinned;
        _isStoppedExpanded = _settings.StoppedExpanded;
        _sort = _settings.Sort;
        Endpoint = endpoint;

        monitor.Changed += OnMonitorChanged;
        Apply(monitor.Current);
    }

    public event Action? HideRequested;

    public ObservableCollection<ContainerViewModel> Active { get; } = [];

    public ObservableCollection<ContainerViewModel> Stopped { get; } = [];

    public string Endpoint { get; }

    public string FixCommand => "sudo usermod -aG docker $USER";

    public bool IsConnecting => Connection == ConnectionState.Connecting;

    public bool IsConnected => Connection == ConnectionState.Connected;

    public bool IsUnavailable => Connection == ConnectionState.DaemonUnavailable;

    public bool IsPermissionDenied => Connection == ConnectionState.PermissionDenied;

    public string SortLabel => Sort switch
    {
        SortMode.Cpu => "CPU",
        SortMode.Memory => "RAM",
        _ => "Nome",
    };

    public void Apply(MonitorSnapshot snapshot)
    {
        Connection = snapshot.Connection;

        var seen = new HashSet<string>(snapshot.Containers.Count);
        foreach (var container in snapshot.Containers)
        {
            seen.Add(container.Info.Id);
            if (_byId.TryGetValue(container.Info.Id, out var existing))
                existing.Update(container);
            else
                _byId[container.Info.Id] = new ContainerViewModel(_monitor, container);
        }

        foreach (var id in _byId.Keys.Where(id => !seen.Contains(id)).ToList())
            _byId.Remove(id);

        var active = OrderActive(_byId.Values.Where(c => c.IsActive));
        var stopped = _byId.Values
            .Where(c => !c.IsActive)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AssignGroupHeaders(active);
        foreach (var container in stopped)
            container.ShowGroupHeader = false;

        Sync(Active, active);
        Sync(Stopped, stopped);

        IsEmpty = IsConnected && active.Count == 0;
        HasStopped = stopped.Count > 0;
        StoppedHeader = $"Fermi ({stopped.Count})";
        Summary = !IsConnected ? ""
            : active.Count switch
            {
                0 => "",
                1 => "1 attivo",
                var n => $"{n} attivi",
            };
    }

    /// <summary>Forgets the CPU histories: they have a gap whenever the panel was hidden.</summary>
    public void ResetHistory()
    {
        foreach (var container in _byId.Values)
            container.ResetHistory();
    }

    partial void OnIsPinnedChanged(bool value)
    {
        _settings.Pinned = value;
        _store.Save(_settings);
    }

    partial void OnIsStoppedExpandedChanged(bool value)
    {
        _settings.StoppedExpanded = value;
        _store.Save(_settings);
    }

    partial void OnSortChanged(SortMode value)
    {
        _settings.Sort = value;
        _store.Save(_settings);
        Apply(_monitor.Current);
    }

    [RelayCommand]
    private void Hide() => HideRequested?.Invoke();

    [RelayCommand]
    private void CycleSort() => Sort = Sort switch
    {
        SortMode.Name => SortMode.Cpu,
        SortMode.Cpu => SortMode.Memory,
        _ => SortMode.Name,
    };

    [RelayCommand]
    private void ToggleStopped() => IsStoppedExpanded = !IsStoppedExpanded;

    // Stats arrive once per second per container: fold whatever piles up into one UI update.
    private void OnMonitorChanged()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _updateQueued, 0);
            Apply(_monitor.Current);
        });
    }

    private List<ContainerViewModel> OrderActive(IEnumerable<ContainerViewModel> containers)
    {
        var ordered = Sort switch
        {
            SortMode.Cpu => containers.OrderByDescending(c => c.CpuPercent),
            SortMode.Memory => containers.OrderByDescending(c => c.MemoryUsed),
            _ => containers
                .OrderBy(c => c.ComposeProject is null)
                .ThenBy(c => c.ComposeProject, StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Compose projects get a header when sorting by name; other orders interleave them, so headers would lie.
    private void AssignGroupHeaders(List<ContainerViewModel> active)
    {
        var grouped = Sort == SortMode.Name && active.Any(c => c.ComposeProject is not null);
        string? previous = null;
        foreach (var container in active)
        {
            var header = grouped ? container.ComposeProject ?? UngroupedHeader : null;
            container.GroupHeader = header;
            container.ShowGroupHeader = header is not null && header != previous;
            previous = header;
        }
    }

    /// <summary>Makes <paramref name="target"/> match <paramref name="desired"/> with as few changes as possible, so rows keep their state.</summary>
    private static void Sync(ObservableCollection<ContainerViewModel> target, List<ContainerViewModel> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                continue;

            var current = target.IndexOf(desired[i]);
            if (current >= 0)
                target.Move(current, i);
            else
                target.Insert(i, desired[i]);
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
    }
}
