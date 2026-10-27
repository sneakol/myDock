namespace MyDock.Core.Models;

/// <param name="Tags">Repository tags such as "nginx:1.27"; empty for a dangling image.</param>
/// <param name="Containers">How many containers use the image; -1 when the daemon did not say.</param>
public sealed record ImageInfo(string Id, IReadOnlyList<string> Tags, long Size, DateTimeOffset Created, long Containers);
