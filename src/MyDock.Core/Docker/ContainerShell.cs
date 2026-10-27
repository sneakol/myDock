using System.Diagnostics;

namespace MyDock.Core.Docker;

/// <summary>Opens a shell inside a container in the user's terminal emulator, through the docker CLI.</summary>
public static class ContainerShell
{
    // bash when the image has it, else sh; on failure (e.g. an image without a shell) the terminal stays open to show why.
    private const string Script =
        """docker exec -it "$1" sh -c 'command -v bash >/dev/null 2>&1 && exec bash || exec sh' || { printf '\nShell exited with an error. Press Enter to close. '; read _; }""";

    private static readonly string[] Terminals =
    [
        "x-terminal-emulator", "gnome-terminal", "ptyxis", "konsole", "xfce4-terminal",
        "kitty", "alacritty", "wezterm", "foot", "tilix", "xterm",
    ];

    /// <summary>The command a terminal runs; the id is a positional argument so it is never parsed as shell code.</summary>
    public static IReadOnlyList<string> ShellCommand(string containerId) => ["sh", "-c", Script, "mydock", containerId];

    /// <summary>The terminal executable and arguments that run <paramref name="command"/>, using that terminal's own "run this" switch.</summary>
    public static (string Exe, IReadOnlyList<string> Args) Launch(string terminal, IReadOnlyList<string> command)
    {
        IEnumerable<string> prefix = Path.GetFileName(terminal) switch
        {
            "gnome-terminal" or "ptyxis" => ["--"],
            "wezterm" => ["start", "--"],
            "xfce4-terminal" => ["-x"],
            "kitty" or "foot" => [],
            _ => ["-e"],
        };
        return (terminal, [.. prefix, .. command]);
    }

    /// <summary>The terminal named by <paramref name="terminalEnv"/> ($TERMINAL), else the first known one that <paramref name="exists"/>.</summary>
    // shortcut: fixed list plus $TERMINAL (run with -e); an unusual terminal needs $TERMINAL.
    public static string? FindTerminal(string? terminalEnv, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(terminalEnv) && exists(terminalEnv))
            return terminalEnv;

        return Terminals.FirstOrDefault(exists);
    }

    public static void Open(string containerId)
    {
        var terminal = FindTerminal(Environment.GetEnvironmentVariable("TERMINAL"), OnPath)
            ?? throw new InvalidOperationException("No terminal emulator found. Set $TERMINAL.");

        var (exe, args) = Launch(terminal, ShellCommand(containerId));
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        Process.Start(info);
    }

    private static bool OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, name)));
}
