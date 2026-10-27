namespace MyDock.Core.Monitoring;

public enum Load
{
    Normal,
    High,
    Critical,
}

public static class Thresholds
{
    /// <summary>How loaded <paramref name="value"/> is against two limits; a limit of 0 or less switches that level off.</summary>
    public static Load Classify(double value, int high, int critical) =>
        critical > 0 && value >= critical ? Load.Critical
        : high > 0 && value >= high ? Load.High
        : Load.Normal;
}
