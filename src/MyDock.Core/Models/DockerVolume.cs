namespace MyDock.Core.Models;

/// <param name="Size">Bytes on disk; 0 when the daemon did not work it out.</param>
/// <param name="RefCount">How many containers, running or not, mount the volume; -1 when the daemon did not say.</param>
public sealed record DockerVolume(string Name, string Driver, long Size, long RefCount)
{
    /// <summary>Anonymous volumes get a generated 64-character hex name; named ones are the user's.</summary>
    public bool IsAnonymous => Name.Length == 64 && Name.All(Uri.IsHexDigit);
}
