namespace MyDock.Core.Docker;

public enum ComposeFileKind
{
    Compose,
    Dockerfile,
}

public enum ProjectAction
{
    Start,
    Stop,
    Restart,
    Remove,
}

/// <summary>Recognises Dockerfiles and compose files, finds their sibling .env files and builds the docker CLI calls.</summary>
public static class ComposeFiles
{
    private static readonly string[] ComposeExtensions = [".yml", ".yaml"];

    public static ComposeFileKind? Detect(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase))
            return ComposeFileKind.Dockerfile;

        var isYaml = ComposeExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        var isCompose = name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("compose", StringComparison.OrdinalIgnoreCase);
        return isYaml && isCompose ? ComposeFileKind.Compose : null;
    }

    /// <summary>The .env, .env.dev, ... files next to <paramref name="path"/>, plain <c>.env</c> first.</summary>
    public static IReadOnlyList<string> FindEnvFiles(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is null || !Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, ".env*")
            .OrderBy(f => Path.GetFileName(f) == ".env" ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The docker invocations, in order, that bring <paramref name="path"/> up; <paramref name="recreate"/> rebuilds the compose containers even when nothing changed.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Commands(string path, string? envFile, bool recreate = false)
    {
        path = Path.GetFullPath(path);
        var kind = Detect(path) ?? throw new ArgumentException("Not a Dockerfile or compose file.", nameof(path));

        if (kind == ComposeFileKind.Compose)
        {
            List<string> up = ["compose", "-f", path];
            if (envFile is not null)
                up.AddRange(["--env-file", envFile]);
            up.AddRange(["up", "-d"]);
            if (recreate)
                up.Add("--force-recreate");
            return [up];
        }

        var dir = Path.GetDirectoryName(path)!;
        var tag = "mydock/" + ImageName(dir);
        List<string> run = ["run", "-d"];
        if (envFile is not null)
            run.AddRange(["--env-file", envFile]);
        run.Add(tag);
        return [["build", "-t", tag, "-f", path, dir], run];
    }

    /// <summary>The docker call that applies <paramref name="action"/> to every container of a running compose project.</summary>
    public static IReadOnlyList<string> ProjectCommand(string project, ProjectAction action) => action switch
    {
        ProjectAction.Start => ["compose", "-p", project, "start"],
        ProjectAction.Stop => ["compose", "-p", project, "stop"],
        ProjectAction.Restart => ["compose", "-p", project, "restart"],
        // Not "down": that would also drop the networks. -s stops what is still running.
        _ => ["compose", "-p", project, "rm", "-f", "-s"],
    };

    /// <summary>
    /// The compose project name <paramref name="path"/> runs as, or null for a Dockerfile. Same precedence as compose:
    /// COMPOSE_PROJECT_NAME in the env file, then the top-level <c>name:</c>, then the folder name.
    /// </summary>
    // shortcut: line scan, no YAML/dotenv parser; switch to one if quoting or anchors matter.
    public static string? ProjectName(string path, string? envFile)
    {
        if (Detect(path) != ComposeFileKind.Compose)
            return null;

        if (envFile is not null && File.Exists(envFile))
        {
            var fromEnv = ValueOf(File.ReadLines(envFile), "COMPOSE_PROJECT_NAME", '=');
            if (fromEnv is not null)
                return fromEnv;
        }

        if (File.Exists(path))
        {
            var fromFile = ValueOf(File.ReadLines(path), "name", ':');
            if (fromFile is not null && !fromFile.Contains('$'))
                return fromFile;
        }

        var dir = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)));
        var name = new string((dir ?? "").ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());
        return name.Length == 0 ? null : name;
    }

    // First unindented "key<sep> value" line, unquoted and without trailing comment.
    private static string? ValueOf(IEnumerable<string> lines, string key, char separator)
    {
        foreach (var line in lines)
        {
            var text = line.StartsWith("export ", StringComparison.Ordinal) ? line[7..] : line;
            if (!text.StartsWith(key, StringComparison.Ordinal) || text[key.Length..].TrimStart() is not [var s, ..] || s != separator)
                continue;

            var value = text[(text.IndexOf(separator) + 1)..].Split(" #")[0].Trim().Trim('"', '\'');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    // Image names must be lower case and limited to [a-z0-9._-].
    private static string ImageName(string dir)
    {
        var name = new string(Path.GetFileName(dir).ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray());
        return name.Length == 0 ? "app" : name;
    }
}
