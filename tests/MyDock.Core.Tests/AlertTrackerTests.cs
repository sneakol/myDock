using MyDock.Core.Docker;
using MyDock.Core.Monitoring;

namespace MyDock.Core.Tests;

public class AlertTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly AlertTracker _tracker = new();

    private ContainerAlert? Die(int code, int seconds = 0, string id = "a") =>
        _tracker.Process(new ContainerEvent("die", id, "web", code), T0.AddSeconds(seconds));

    [Fact]
    public void Failed_exit_is_a_crash_with_its_code()
    {
        var alert = Die(1);

        Assert.Equal(new ContainerAlert(AlertKind.Crashed, "web", 1), alert);
        Assert.Equal("web exited with code 1", alert!.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(137)]
    [InlineData(143)]
    public void Clean_exit_and_stop_signals_are_silent(int code) => Assert.Null(Die(code));

    [Fact]
    public void Oom_followed_by_die_is_reported_as_out_of_memory_even_with_137()
    {
        _tracker.Process(new ContainerEvent("oom", "a", "web"), T0);

        Assert.Equal(AlertKind.OutOfMemory, Die(137, 1)!.Kind);
        Assert.Null(Die(137, 2));
    }

    [Fact]
    public void Start_forgets_an_oom_that_was_not_followed_by_a_die()
    {
        _tracker.Process(new ContainerEvent("oom", "a", "web"), T0);
        _tracker.Process(new ContainerEvent("start", "a", "web"), T0);

        Assert.Null(Die(137, 1));
    }

    [Fact]
    public void Unhealthy_transition_is_reported_and_healthy_is_not()
    {
        Assert.Equal(AlertKind.Unhealthy, _tracker.Process(new ContainerEvent("health_status: unhealthy", "a", "web"), T0)!.Kind);
        Assert.Null(_tracker.Process(new ContainerEvent("health_status: healthy", "a", "web"), T0));
    }

    [Fact]
    public void Repeated_failures_become_one_restart_loop_alert()
    {
        Assert.Equal(AlertKind.Crashed, Die(1, 0)!.Kind);
        Assert.Equal(AlertKind.Crashed, Die(1, 10)!.Kind);
        Assert.Equal(AlertKind.RestartLoop, Die(1, 20)!.Kind);
        Assert.Null(Die(1, 30));
    }

    [Fact]
    public void Failures_spread_beyond_the_window_do_not_form_a_loop()
    {
        var window = (int)AlertTracker.LoopWindow.TotalSeconds + 1;

        Assert.Equal(AlertKind.Crashed, Die(1, 0)!.Kind);
        Assert.Equal(AlertKind.Crashed, Die(1, window)!.Kind);
        Assert.Equal(AlertKind.Crashed, Die(1, 2 * window)!.Kind);
    }

    [Fact]
    public void Loop_can_be_announced_again_once_it_calmed_down()
    {
        Die(1, 0);
        Die(1, 10);
        Assert.Equal(AlertKind.RestartLoop, Die(1, 20)!.Kind);

        var later = (int)AlertTracker.LoopWindow.TotalSeconds + 100;
        Assert.Equal(AlertKind.Crashed, Die(1, later)!.Kind);
        Die(1, later + 10);
        Assert.Equal(AlertKind.RestartLoop, Die(1, later + 20)!.Kind);
    }

    [Fact]
    public void Containers_are_tracked_separately() =>
        Assert.Equal(AlertKind.Crashed, Die(1, 0, id: "b")!.Kind);

    [Fact]
    public void Name_falls_back_to_the_short_id()
    {
        var alert = _tracker.Process(new ContainerEvent("health_status: unhealthy", "0123456789abcdef"), T0);

        Assert.Equal("0123456789ab", alert!.Name);
    }
}
