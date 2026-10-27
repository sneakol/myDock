namespace MyDock.App.Services;

/// <summary>Starts myDock at login through an XDG autostart entry.</summary>
public static class Autostart
{
    private static string EntryPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart",
        "mydock.desktop");

    public static bool IsEnabled => File.Exists(EntryPath);

    /// <summary>Returns false when the entry could not be written or removed.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(EntryPath);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            File.WriteAllText(EntryPath, $"""
            [Desktop Entry]
            Type=Application
            Name=myDock
            Comment=Running Docker containers and their resource usage
            Exec={LaunchCommand()} --hidden
            Icon=mydock
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string LaunchCommand()
    {
        var process = Environment.ProcessPath ?? "mydock";

        // Started as "dotnet mydock.dll" rather than through the native launcher.
        if (Path.GetFileNameWithoutExtension(process) == "dotnet")
            return $"\"{process}\" \"{Path.Combine(AppContext.BaseDirectory, "mydock.dll")}\"";

        return $"\"{process}\"";
    }
}
