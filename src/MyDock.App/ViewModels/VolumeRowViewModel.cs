using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

/// <summary>One volume in the Disk overlay; deleting is up to the owner, which reloads the list afterwards.</summary>
public sealed class VolumeRowViewModel
{
    public VolumeRowViewModel(DockerVolume volume, Func<VolumeRowViewModel, Task> delete, Func<VolumeRowViewModel, Task> inspect)
    {
        Volume = volume;
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
        InspectCommand = new AsyncRelayCommand(() => inspect(this));
    }

    public DockerVolume Volume { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand InspectCommand { get; }

    public string Title => Volume.IsAnonymous ? Volume.Name[..12] + "…" : Volume.Name;

    public string Subtitle => (Volume.IsAnonymous ? Loc.Get("Resources.Anonymous") : Loc.Get("Resources.Named")) + " · " + Volume.Driver;

    public string Size => Format.Size((ulong)Volume.Size);

    public string UsedBy => Volume.RefCount switch
    {
        > 0 => Loc.Get("Resources.UsedBy", Volume.RefCount),
        0 => Loc.Get("Resources.Unused"),
        _ => "",
    };
}
