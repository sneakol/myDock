using System.Text.Json;
using System.Text.Json.Serialization;
using MyDock.Core.Monitoring;

namespace MyDock.App.Services;

public enum SortMode
{
    Name,
    Cpu,
    Memory,
}

public sealed class Settings
{
    public bool Pinned { get; set; }
    public bool StoppedExpanded { get; set; }
    public SortMode Sort { get; set; }
    public List<string> ComposeFiles { get; set; } = [];
    public List<string> CollapsedProjects { get; set; } = [];
    public Dictionary<string, string> EnvChoice { get; set; } = [];

    public bool NotificationsEnabled { get; set; } = true;
    public bool NotifyCrashes { get; set; } = true;
    public bool NotifyUnhealthy { get; set; } = true;
    public bool NotifyRestartLoops { get; set; } = true;

    /// <summary>Percent of one core (docker stats convention) from which a running container shows as warning / bad; 0 switches it off.</summary>
    public int CpuWarn { get; set; }
    public int CpuBad { get; set; }

    /// <summary>Percent of the memory limit, same rules as the CPU ones.</summary>
    public int MemoryWarn { get; set; } = 80;
    public int MemoryBad { get; set; } = 95;

    /// <summary>Terminal for the Shell button; empty uses $TERMINAL or the first known one.</summary>
    public string? Terminal { get; set; }

    /// <summary>"en" or "it"; null follows the system language. Read once at startup.</summary>
    public string? Language { get; set; }

    public bool Allows(AlertKind kind) => NotificationsEnabled && kind switch
    {
        AlertKind.Crashed or AlertKind.OutOfMemory => NotifyCrashes,
        AlertKind.Unhealthy => NotifyUnhealthy,
        _ => NotifyRestartLoops,
    };
}

/// <summary>
/// Persists <see cref="Settings"/> as JSON in the user's config directory. Never throws: settings are a convenience.
/// Writes are debounced off the calling thread; call <see cref="Flush"/> before exit.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private string? _pending;

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _timer = new Timer(_ => Flush());
    }

    private static string DefaultPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "mydock",
        "settings.json");

    public Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    /// <summary>Serializes now (the caller may keep mutating <paramref name="settings"/>) and writes after a short delay.</summary>
    public void Save(Settings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        lock (_gate)
            _pending = json;
        _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes any pending save now.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_pending is null)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, _pending);
            }
            catch (Exception)
            {
                // Runs on a timer thread: anything escaping here would take the process down.
            }

            _pending = null;
        }
    }
}
