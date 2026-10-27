using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.App.Services;
using MyDock.Core.Docker;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App.ViewModels;

public sealed partial class PanelViewModel : ObservableObject
{
    private const string UngroupedHeader = "Other";

    private readonly ContainerMonitor _monitor;
    private readonly SettingsStore _store;
    private readonly Settings _settings;
    private readonly IComposeRunner _runner;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStopped))]
    private bool _isStoppedExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering), nameof(ShowStopped))]
    private string _filterText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering), nameof(ShowStopped))]
    private bool _onlyUnhealthy;

    [ObservableProperty] private bool _noMatches;
    [ObservableProperty] private bool _isShowingSettings;
    [ObservableProperty] private bool _startAtLogin = Autostart.IsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleting))]
    private ContainerViewModel? _pendingDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingLogs))]
    private LogViewModel? _logs;

    [ObservableProperty] private bool _imageShared;
    [ObservableProperty] private bool _alsoDeleteImage;
    [ObservableProperty] private bool _isDeleteBusy;
    [ObservableProperty] private string? _deleteError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    private SortMode _sort;

    public PanelViewModel(ContainerMonitor monitor, SettingsStore store, IComposeRunner runner, string endpoint)
    {
        _monitor = monitor;
        _store = store;
        _runner = runner;
        _settings = store.Load();
        _isPinned = _settings.Pinned;
        _isStoppedExpanded = _settings.StoppedExpanded;
        _sort = _settings.Sort;
        Endpoint = endpoint;
        Preferences = new SettingsViewModel(_settings, _store, () => Apply(_monitor.Current));

        foreach (var path in _settings.ComposeFiles.Where(File.Exists))
            Files.Add(new ComposeFileViewModel(path, _runner, _settings, _store, RemoveFile));

        monitor.Changed += OnMonitorChanged;
        Apply(monitor.Current);
    }

    public event Action? HideRequested;

    public event Action? QuitRequested;

    public event Action<LogViewModel>? LogsPoppedOut;

    public ObservableCollection<ContainerViewModel> Active { get; } = [];

    public ObservableCollection<ContainerViewModel> Stopped { get; } = [];

    public ObservableCollection<ComposeFileViewModel> Files { get; } = [];

    public string Endpoint { get; }

    public Settings Settings => _settings;

    public SettingsViewModel Preferences { get; }

    public string FixCommand => "sudo usermod -aG docker $USER";

    public bool IsConnecting => Connection == ConnectionState.Connecting;

    public bool IsConnected => Connection == ConnectionState.Connected;

    public bool IsUnavailable => Connection == ConnectionState.DaemonUnavailable;

    public bool IsPermissionDenied => Connection == ConnectionState.PermissionDenied;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText) || OnlyUnhealthy;

    // Matches among stopped containers must not hide behind the collapsed section.
    public bool ShowStopped => IsStoppedExpanded || IsFiltering;

    public bool IsDeleting => PendingDelete is not null;

    public bool IsShowingLogs => Logs is not null;

    public string SortLabel => Sort switch
    {
        SortMode.Cpu => "CPU",
        SortMode.Memory => "RAM",
        _ => "Name",
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
                _byId[container.Info.Id] = new ContainerViewModel(_monitor, container, _settings, RequestDelete, ToggleGroup, RunGroupAsync, RequestLogs);
        }

        foreach (var id in _byId.Keys.Where(id => !seen.Contains(id)).ToList())
            _byId.Remove(id);

        var visible = _byId.Values.Where(c =>
            ContainerFilter.Matches(FilterText, c.Name, c.Image, c.ComposeProject) && (!OnlyUnhealthy || c.IsUnhealthy)).ToList();
        var active = OrderActive(visible.Where(c => c.IsActive));
        var stopped = ComposeGrouping.Order(visible.Where(c => !c.IsActive), c => c.ComposeProject, c => c.Name);

        AssignGroups(active);
        AssignGroups(stopped);
        foreach (var file in Files)
            file.Track(_byId.Values);

        Sync(Active, active);
        Sync(Stopped, stopped);

        var activeCount = _byId.Values.Count(c => c.IsActive);
        IsEmpty = IsConnected && active.Count == 0 && (!IsFiltering || _byId.Count == 0);
        NoMatches = IsConnected && IsFiltering && visible.Count == 0 && _byId.Count > 0;
        HasStopped = stopped.Count > 0;
        StoppedHeader = $"Stopped ({stopped.Count})";
        Summary = !IsConnected ? ""
            : activeCount switch
            {
                0 => "",
                var n => $"{n} active",
            };

        // After a failed delete the container may already be gone, and the message is all the user has left.
        if (PendingDelete is { } pending)
        {
            if (!IsDeleteBusy && DeleteError is null && (!_byId.ContainsKey(pending.Id) || pending.IsActive || !IsConnected))
                CancelDelete();
            else
                ImageShared = _monitor.IsImageShared(pending.ImageId, pending.Id);
        }
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

    partial void OnFilterTextChanged(string value) => Apply(_monitor.Current);

    partial void OnOnlyUnhealthyChanged(bool value) => Apply(_monitor.Current);

    partial void OnIsStoppedExpandedChanged(bool value)
    {
        _settings.StoppedExpanded = value;
        _store.Save(_settings);
    }

    /// <summary>Adds a Dockerfile or compose file; returns an error message when it is not one.</summary>
    public string? AddFile(string path)
    {
        if (ComposeFiles.Detect(path) is null)
            return "Not a Dockerfile or docker-compose file.";

        if (Files.Any(f => f.Path == path))
            return null;

        var file = new ComposeFileViewModel(path, _runner, _settings, _store, RemoveFile);
        file.Track(_byId.Values);
        Files.Add(file);
        _settings.ComposeFiles = Files.Select(f => f.Path).ToList();
        _store.Save(_settings);
        return null;
    }

    public void RefreshFiles()
    {
        foreach (var file in Files)
            file.Refresh();
    }

    private void RemoveFile(ComposeFileViewModel file)
    {
        Files.Remove(file);
        _settings.ComposeFiles = Files.Select(f => f.Path).ToList();
        _settings.EnvChoice.Remove(file.Path);
        _store.Save(_settings);
    }

    partial void OnStartAtLoginChanged(bool value)
    {
        if (!Autostart.Set(value))
            StartAtLogin = Autostart.IsEnabled;
    }

    partial void OnImageSharedChanged(bool value)
    {
        if (value)
            AlsoDeleteImage = false;
    }

    private void RequestDelete(ContainerViewModel container)
    {
        DeleteError = null;
        AlsoDeleteImage = false;
        ImageShared = _monitor.IsImageShared(container.ImageId, container.Id);
        PendingDelete = container;
    }

    private void RequestLogs(ContainerViewModel container)
    {
        Logs?.Dispose();
        Logs = new LogViewModel(_monitor, container);
    }

    [RelayCommand]
    public void CloseLogs()
    {
        Logs?.Dispose();
        Logs = null;
    }

    // The window takes over the view model, so it must not be disposed here.
    [RelayCommand]
    private void PopOutLogs()
    {
        if (Logs is not { } logs)
            return;

        Logs = null;
        LogsPoppedOut?.Invoke(logs);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        PendingDelete = null;
        DeleteError = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (PendingDelete is not { } container)
            return;

        DeleteError = null;
        IsDeleteBusy = true;
        try
        {
            await _monitor.DeleteContainerAsync(container.Id, AlsoDeleteImage && !ImageShared);
            CancelDelete();
        }
        catch (Exception ex)
        {
            DeleteError = ex.Message;
        }
        finally
        {
            IsDeleteBusy = false;
        }
    }

    partial void OnSortChanged(SortMode value)
    {
        _settings.Sort = value;
        _store.Save(_settings);
        Apply(_monitor.Current);
    }

    [RelayCommand]
    private void ShowSettings() => IsShowingSettings = true;

    [RelayCommand]
    public void CloseSettings() => IsShowingSettings = false;

    [RelayCommand]
    private void Hide() => HideRequested?.Invoke();

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke();

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

    private List<ContainerViewModel> OrderActive(IEnumerable<ContainerViewModel> containers) =>
        ComposeGrouping.Order(containers, c => c.ComposeProject, c => c.Name, Sort switch
        {
            SortMode.Cpu => c => c.CpuPercent,
            SortMode.Memory => c => c.MemoryUsed,
            _ => null,
        });

    // The list is already grouped: the first row of each project carries the header, and every row knows if its group is collapsed.
    private void AssignGroups(List<ContainerViewModel> list)
    {
        var grouped = list.Any(c => c.ComposeProject is not null);
        var counts = list.GroupBy(c => c.ComposeProject ?? "").ToDictionary(g => g.Key, g => g.Count());
        string? previous = null;
        foreach (var container in list)
        {
            var header = grouped ? container.ComposeProject ?? UngroupedHeader : null;
            container.GroupHeader = header;
            container.ShowGroupHeader = header is not null && header != previous;
            container.GroupCount = counts[container.ComposeProject ?? ""];
            container.IsGroupCollapsed = container.ComposeProject is { } p && _settings.CollapsedProjects.Contains(p);
            previous = header;
        }
    }

    private void ToggleGroup(string project)
    {
        if (!_settings.CollapsedProjects.Remove(project))
            _settings.CollapsedProjects.Add(project);
        _store.Save(_settings);
        Apply(_monitor.Current);
    }

    private Task RunGroupAsync(string project, ProjectAction action) => _runner.RunProjectAsync(project, action, CancellationToken.None);

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
