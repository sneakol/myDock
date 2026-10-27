using System.Globalization;
using MyDock.Core.Docker;
using MyDock.Core.Formatting;

namespace MyDock.Core.Tests;

public sealed class FormatTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0ul, "0 B")]
    [InlineData(999ul, "999 B")]
    [InlineData(4630493827ul, "4.63 GB")]
    [InlineData(40960ul, "41 kB")]
    public void Size_uses_decimal_units_like_docker_system_df(ulong bytes, string expected) =>
        Assert.Equal(expected, Format.Size(bytes, Invariant));

    [Theory]
    [InlineData(0ul, "0 B")]
    [InlineData(1023ul, "1023 B")]
    [InlineData(1024ul, "1 KiB")]
    [InlineData(1536ul, "1.5 KiB")]
    [InlineData(188_743_680ul, "180 MiB")]
    [InlineData(12_897_484ul, "12.3 MiB")]
    [InlineData(1_288_490_189ul, "1.2 GiB")]
    [InlineData(33_285_996_544ul, "31 GiB")]
    public void Memory_uses_binary_units(ulong bytes, string expected) =>
        Assert.Equal(expected, Format.Memory(bytes, Invariant));

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(-5, "0 B/s")]
    [InlineData(999.4, "999 B/s")]
    [InlineData(1200, "1.2 kB/s")]
    [InlineData(45_600, "45.6 kB/s")]
    [InlineData(3_400_000, "3.4 MB/s")]
    [InlineData(250_000_000, "250 MB/s")]
    public void Rate_uses_decimal_units_per_second(double rate, string expected) =>
        Assert.Equal(expected, Format.Rate(rate, Invariant));

    [Theory]
    [InlineData(0, "0.0%")]
    [InlineData(2.14, "2.1%")]
    [InlineData(99.94, "99.9%")]
    [InlineData(100, "100%")]
    [InlineData(253.7, "254%")]
    public void Percent_drops_decimals_from_100_up(double value, string expected) =>
        Assert.Equal(expected, Format.Percent(value, Invariant));

    [Fact]
    public void Honours_the_given_culture()
    {
        var italian = CultureInfo.GetCultureInfo("it-IT");

        Assert.Equal("2,1%", Format.Percent(2.14, italian));
        Assert.Equal("1,5 KiB", Format.Memory(1536, italian));
    }

    [Theory]
    [InlineData("{\"message\":\"No such container: web\"}", "No such container: web")]
    [InlineData("  plain text  ", "plain text")]
    [InlineData("{\"other\":1}", "{\"other\":1}")]
    [InlineData("", "fallback")]
    [InlineData(null, "fallback")]
    public void Describes_daemon_error_bodies(string? body, string expected) =>
        Assert.Equal(expected, DockerOperationException.Describe(body, "fallback"));
}
