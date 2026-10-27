using System.Text.Json;
using System.Text.Json.Serialization;

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
    public Dictionary<string, string> EnvChoice { get; set; } = [];
}

/// <summary>Persists <see cref="Settings"/> as JSON in the user's config directory. Never throws: settings are a convenience.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string? path = null) => _path = path ?? DefaultPath;

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

    public void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
