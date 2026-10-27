using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyDock.App.Services;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

public sealed record LanguageOption(string? Code, string Label);

/// <summary>Editable view of <see cref="Settings"/>: every change is written back, saved and announced.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly Action _changed;
    private readonly string? _startupLanguage;

    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notifyCrashes;
    [ObservableProperty] private bool _notifyUnhealthy;
    [ObservableProperty] private bool _notifyRestartLoops;
    [ObservableProperty] private decimal? _cpuWarn;
    [ObservableProperty] private decimal? _cpuBad;
    [ObservableProperty] private decimal? _memoryWarn;
    [ObservableProperty] private decimal? _memoryBad;
    [ObservableProperty] private string? _terminal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartNeeded))]
    private LanguageOption? _language;

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
        _startupLanguage = settings.Language;
        _language = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(null, Loc.Get("Settings.LanguageSystem")),
        new("en", "English"),
        new("it", "Italiano"),
    ];

    /// <summary>The language is read once at startup.</summary>
    public bool RestartNeeded => Language?.Code != _startupLanguage;

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
        _settings.Language = Language?.Code;
        _store.Save(_settings);

        // The terminal is only read when the Shell button is pressed and the language at startup: no need to rebuild the list for them.
        if (e.PropertyName is not (nameof(Terminal) or nameof(Language) or nameof(RestartNeeded)))
            _changed();
    }
}
