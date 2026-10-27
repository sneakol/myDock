using MyDock.Core.Monitoring;

namespace MyDock.Core.Tests;

public class ThresholdsTests
{
    [Theory]
    [InlineData(10, Load.Normal)]
    [InlineData(80, Load.High)]
    [InlineData(94.9, Load.High)]
    [InlineData(95, Load.Critical)]
    [InlineData(300, Load.Critical)]
    public void Classifies_against_both_limits(double value, Load expected) =>
        Assert.Equal(expected, Thresholds.Classify(value, 80, 95));

    [Fact]
    public void A_limit_of_zero_switches_that_level_off()
    {
        Assert.Equal(Load.Normal, Thresholds.Classify(500, 0, 0));
        Assert.Equal(Load.High, Thresholds.Classify(500, 80, 0));
        Assert.Equal(Load.Critical, Thresholds.Classify(500, 0, 95));
    }
}
