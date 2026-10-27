namespace MyDock.Core.Docker;

public enum ComposeFileKind
{
    Compose,
    Dockerfile,
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

    /// <summary>The docker invocations, in order, that bring <paramref name="path"/> up.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Commands(string path, string? envFile)
    {
        path = Path.GetFullPath(path);
        var kind = Detect(path) ?? throw new ArgumentException("Not a Dockerfile or compose file.", nameof(path));

        if (kind == ComposeFileKind.Compose)
        {
            List<string> up = ["compose", "-f", path];
            if (envFile is not null)
                up.AddRange(["--env-file", envFile]);
            up.AddRange(["up", "-d"]);
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

    // Image names must be lower case and limited to [a-z0-9._-].
    private static string ImageName(string dir)
    {
        var name = new string(Path.GetFileName(dir).ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray());
        return name.Length == 0 ? "app" : name;
    }
}
