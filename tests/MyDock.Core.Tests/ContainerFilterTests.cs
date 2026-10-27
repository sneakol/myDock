using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

public class ContainerFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_query_matches_everything(string? query) =>
        Assert.True(ContainerFilter.Matches(query, "web", "nginx:1", null));

    [Theory]
    [InlineData("WEB")]
    [InlineData("nginx")]
    [InlineData("shop")]
    public void Matches_name_image_or_project_ignoring_case(string query) =>
        Assert.True(ContainerFilter.Matches(query, "web-1", "nginx:1", "Shop"));

    [Fact]
    public void Every_word_must_match_somewhere() =>
        Assert.True(ContainerFilter.Matches("shop nginx", "web-1", "nginx:1", "shop"));

    [Fact]
    public void Fails_when_one_word_is_missing() =>
        Assert.False(ContainerFilter.Matches("shop redis", "web-1", "nginx:1", "shop"));

    [Fact]
    public void Container_without_project_only_matches_name_or_image() =>
        Assert.False(ContainerFilter.Matches("shop", "web-1", "nginx:1", null));
}
