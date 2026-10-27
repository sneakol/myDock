using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Models;

namespace MyDock.App.ViewModels;

/// <summary>One network in the Disk overlay; deleting is up to the owner, which reloads the list afterwards.</summary>
public sealed class NetworkRowViewModel
{
    private readonly int _containers;

    public NetworkRowViewModel(DockerNetwork network, int containers, Func<NetworkRowViewModel, Task> delete, Func<NetworkRowViewModel, Task> inspect)
    {
        Network = network;
        _containers = containers;
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
        InspectCommand = new AsyncRelayCommand(() => inspect(this));
    }

    public DockerNetwork Network { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand InspectCommand { get; }

    public bool CanDelete => !Network.IsBuiltIn;

    public string Title => Network.Name;

    public string Subtitle => $"{Network.Driver} · {Network.Scope} · {Network.Created.LocalDateTime:yyyy-MM-dd}";

    public string UsedBy => Network.IsBuiltIn ? "built-in" : _containers > 0 ? $"{_containers} on it" : "unused";
}
