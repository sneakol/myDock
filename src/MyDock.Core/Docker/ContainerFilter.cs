namespace MyDock.Core.Docker;

public static class ContainerFilter
{
    /// <summary>True when every word of <paramref name="query"/> occurs, ignoring case, in the name, image or compose project. A blank query matches everything.</summary>
    public static bool Matches(string? query, string name, string image, string? project) =>
        (query ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => Contains(name, word) || Contains(image, word) || (project is not null && Contains(project, word)));

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
