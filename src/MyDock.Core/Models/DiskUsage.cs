namespace MyDock.Core.Models;

/// <summary>One row of <c>docker system df</c>: how many items, their size and how much of it nothing uses; all in bytes.</summary>
public sealed record UsageLine(long Count, long Size, long Reclaimable);

public sealed record DiskUsage(UsageLine Images, UsageLine Containers, UsageLine Volumes, UsageLine BuildCache);
