namespace MyDock.Core.Models;

public sealed record DockerNetwork(string Id, string Name, string Driver, string Scope, DateTimeOffset Created)
{
    /// <summary>The three networks the daemon creates itself and refuses to remove.</summary>
    public bool IsBuiltIn => Name is "bridge" or "host" or "none";
}
