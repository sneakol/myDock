using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

public class ComposeGroupingTests
{
    private static readonly (string? Project, string Name, double Cpu)[] Items =
    [
        ("shop", "web", 5),
        (null, "solo", 90),
        ("blog", "db", 1),
        ("shop", "api", 20),
        ("blog", "app", 2),
    ];

    private static string[] Names(IEnumerable<(string? Project, string Name, double Cpu)> items) =>
        items.Select(i => i.Name).ToArray();

    [Fact]
    public void Orders_groups_and_members_by_name_with_ungrouped_last()
    {
        var result = ComposeGrouping.Order(Items, i => i.Project, i => i.Name);

        Assert.Equal(["app", "db", "api", "web", "solo"], Names(result));
    }

    [Fact]
    public void Orders_groups_by_their_highest_metric_and_keeps_groups_contiguous()
    {
        var result = ComposeGrouping.Order(Items, i => i.Project, i => i.Name, i => i.Cpu);

        Assert.Equal(["api", "web", "app", "db", "solo"], Names(result));
    }
}
