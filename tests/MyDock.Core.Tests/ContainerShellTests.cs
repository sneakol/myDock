using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

public class ContainerShellTests
{
    [Fact]
    public void The_container_id_is_an_argument_not_part_of_the_script()
    {
        var command = ContainerShell.ShellCommand("abc; rm -rf /");

        Assert.Equal(["sh", "-c"], command.Take(2));
        Assert.Contains("docker exec -it \"$1\"", command[2]);
        Assert.DoesNotContain("abc", command[2]);
        Assert.Equal("abc; rm -rf /", command[^1]);
    }

    [Theory]
    [InlineData("gnome-terminal", new[] { "--" })]
    [InlineData("xfce4-terminal", new[] { "-x" })]
    [InlineData("wezterm", new[] { "start", "--" })]
    [InlineData("kitty", new string[0])]
    [InlineData("xterm", new[] { "-e" })]
    [InlineData("/usr/bin/konsole", new[] { "-e" })]
    public void Launch_uses_the_terminals_own_run_switch(string terminal, string[] prefix)
    {
        var (exe, args) = ContainerShell.Launch(terminal, ["sh", "-c", "x"]);

        Assert.Equal(terminal, exe);
        Assert.Equal([.. prefix, "sh", "-c", "x"], args);
    }

    [Fact]
    public void FindTerminal_prefers_the_environment_then_the_known_order()
    {
        Assert.Equal("mine", ContainerShell.FindTerminal("mine", t => t is "mine" or "xterm"));
        Assert.Equal("konsole", ContainerShell.FindTerminal("missing", t => t is "konsole" or "xterm"));
        Assert.Equal("xterm", ContainerShell.FindTerminal(null, t => t == "xterm"));
        Assert.Null(ContainerShell.FindTerminal(null, _ => false));
        Assert.Equal("chosen", ContainerShell.FindTerminal("mine", t => t is "chosen" or "mine" or "xterm", preferred: "chosen"));
        Assert.Equal("mine", ContainerShell.FindTerminal("mine", t => t is "mine" or "xterm", preferred: "missing"));
    }
}
