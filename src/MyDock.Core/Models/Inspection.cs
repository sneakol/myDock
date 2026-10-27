namespace MyDock.Core.Models;

/// <summary>What <c>docker inspect</c> says about an image, volume or network, plus the containers that use it.</summary>
/// <param name="Json">The daemon's answer, pretty-printed.</param>
public sealed record Inspection(string Json, IReadOnlyList<string> UsedBy);
