namespace MyDock.Core.Docker;

public static class ComposeGrouping
{
    /// <summary>
    /// Orders items so every compose project is contiguous, items without a project last.
    /// Without a <paramref name="metric"/> groups and members go by name; with one, by metric descending (groups by their highest member).
    /// </summary>
    public static List<T> Order<T>(IEnumerable<T> items, Func<T, string?> project, Func<T, string> name, Func<T, double>? metric = null)
    {
        var text = StringComparer.OrdinalIgnoreCase;
        var groups = items.GroupBy(project).OrderBy(g => g.Key is null);
        var ordered = metric is null
            ? groups.ThenBy(g => g.Key, text)
            : groups.ThenByDescending(g => g.Max(metric)).ThenBy(g => g.Key, text);

        return ordered
            .SelectMany(g => metric is null
                ? g.OrderBy(name, text)
                : g.OrderByDescending(metric).ThenBy(name, text))
            .ToList();
    }
}
