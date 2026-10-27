using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

public class LogTextTests
{
    [Fact]
    public void Strips_color_codes() =>
        Assert.Equal("ok done\n", LogText.Append("", "\u001b[32mok\u001b[0m done\n", 10));

    [Fact]
    public void Joins_a_line_split_across_chunks()
    {
        var text = LogText.Append("", "hel", 10);
        text = LogText.Append(text, "lo\nwor", 10);

        Assert.Equal("hello\nwor", text);
    }

    [Theory]
    [InlineData("a\nb\nc\nd\n", "c\nd\n")]
    [InlineData("a\nb\nc\nd", "c\nd")]
    [InlineData("a\nb\n", "a\nb\n")]
    public void Keeps_only_the_last_lines(string input, string expected) =>
        Assert.Equal(expected, LogText.Append("", input, 2));
}
