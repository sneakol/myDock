using System.Runtime.Versioning;
using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

/// <summary>Runs the real runner against a stand-in `docker` script put first on PATH (Linux only, like the app).</summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(ProcessComposeRunnerTests))]
public sealed class ProcessComposeRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mydock-runner").FullName;
    private readonly string _path = Environment.GetEnvironmentVariable("PATH") ?? "";
    private readonly string _file;

    public ProcessComposeRunnerTests()
    {
        _file = Path.Combine(_dir, "docker-compose.yml");
        File.WriteAllText(_file, "services: {}\n");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        Directory.Delete(_dir, recursive: true);
    }

    private void FakeDocker(string body)
    {
        var script = Path.Combine(_dir, "docker");
        File.WriteAllText(script, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", _dir + Path.PathSeparator + _path);
    }

    [Fact]
    public async Task Streams_the_command_and_both_output_pipes_line_by_line()
    {
        FakeDocker("echo built; echo warning >&2");
        var lines = new List<string>();

        await new ProcessComposeRunner().RunAsync(_file, null, CancellationToken.None, output: line =>
        {
            lock (lines)
                lines.Add(line);
        });

        Assert.StartsWith("$ docker compose", lines[0]);
        Assert.Equivalent(new[] { "built\n", "warning\n" }, lines[1..]);
    }

    [Fact]
    public async Task A_failing_command_throws_with_the_end_of_stderr()
    {
        FakeDocker("echo one >&2; echo two >&2; exit 1");

        var error = await Assert.ThrowsAsync<DockerOperationException>(
            () => new ProcessComposeRunner().RunAsync(_file, null, CancellationToken.None));

        Assert.Equal("one\ntwo", error.Message);
    }

    [Fact]
    public async Task Cancelling_stops_the_command()
    {
        FakeDocker("sleep 30");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessComposeRunner().RunAsync(_file, null, cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }
}
