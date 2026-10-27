using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDock.App.Services;

namespace MyDock.App.ViewModels;

/// <summary>Editable view of <see cref="Settings"/>: every change is written back, saved and announced.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly Action _changed;

    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notifyCrashes;
    [ObservableProperty] private bool _notifyUnhealthy;
    [ObservableProperty] private bool _notifyRestartLoops;
    [ObservableProperty] private decimal? _cpuWarn;
    [ObservableProperty] private decimal? _cpuBad;
    [ObservableProperty] private decimal? _memoryWarn;
    [ObservableProperty] private decimal? _memoryBad;
    [ObservableProperty] private string? _terminal;

    public SettingsViewModel(Settings settings, SettingsStore store, Action changed)
    {
        _settings = settings;
        _store = store;
        _changed = changed;
        _notificationsEnabled = settings.NotificationsEnabled;
        _notifyCrashes = settings.NotifyCrashes;
        _notifyUnhealthy = settings.NotifyUnhealthy;
        _notifyRestartLoops = settings.NotifyRestartLoops;
        _cpuWarn = settings.CpuWarn;
        _cpuBad = settings.CpuBad;
        _memoryWarn = settings.MemoryWarn;
        _memoryBad = settings.MemoryBad;
        _terminal = settings.Terminal;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        _settings.NotificationsEnabled = NotificationsEnabled;
        _settings.NotifyCrashes = NotifyCrashes;
        _settings.NotifyUnhealthy = NotifyUnhealthy;
        _settings.NotifyRestartLoops = NotifyRestartLoops;
        // A cleared box reads as null: that means off.
        _settings.CpuWarn = (int)(CpuWarn ?? 0);
        _settings.CpuBad = (int)(CpuBad ?? 0);
        _settings.MemoryWarn = (int)(MemoryWarn ?? 0);
        _settings.MemoryBad = (int)(MemoryBad ?? 0);
        _settings.Terminal = string.IsNullOrWhiteSpace(Terminal) ? null : Terminal.Trim();
        _store.Save(_settings);

        // The terminal is only read when the Shell button is pressed: no need to rebuild the list per keystroke.
        if (e.PropertyName != nameof(Terminal))
            _changed();
    }
}
