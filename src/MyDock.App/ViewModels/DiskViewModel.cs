using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

public enum DiskTab
{
    Overview,
    Images,
    Volumes,
    Networks,
}

public sealed record DiskRow(string Name, string Count, string Size, string Reclaimable);

/// <summary>What <c>docker system df</c> reports, with the prunes that are safe to offer.</summary>
public sealed partial class DiskViewModel : ObservableObject
{
    private readonly ContainerMonitor _monitor;

    [ObservableProperty] private IReadOnlyList<DiskRow> _rows = [];
    [ObservableProperty] private IReadOnlyList<ImageRowViewModel> _images = [];
    [ObservableProperty] private IReadOnlyList<VolumeRowViewModel> _volumes = [];
    [ObservableProperty] private IReadOnlyList<NetworkRowViewModel> _networks = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsImages), nameof(IsVolumes), nameof(IsNetworks))]
    private DiskTab _tab;

    // While a detail is open it takes the place of whichever tab is selected.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(IsOverview), nameof(IsImages), nameof(IsVolumes), nameof(IsNetworks))]
    private InspectViewModel? _detail;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _result;

    public DiskViewModel(ContainerMonitor monitor)
    {
        _monitor = monitor;
        _ = RefreshAsync();
    }

    public bool HasDetail => Detail is not null;

    public bool IsOverview => Detail is null && Tab == DiskTab.Overview;

    public bool IsImages => Detail is null && Tab == DiskTab.Images;

    public bool IsVolumes => Detail is null && Tab == DiskTab.Volumes;

    public bool IsNetworks => Detail is null && Tab == DiskTab.Networks;

    [RelayCommand]
    private Task RefreshAsync() => GuardAsync(Tab switch
    {
        DiskTab.Images => LoadImagesAsync,
        DiskTab.Volumes => LoadVolumesAsync,
        DiskTab.Networks => LoadNetworksAsync,
        _ => LoadAsync,
    });

    [RelayCommand]
    private Task SelectTabAsync(DiskTab tab)
    {
        Detail = null;
        Tab = tab;
        return RefreshAsync();
    }

    [RelayCommand]
    public void CloseDetail() => Detail = null;

    private Task InspectImageAsync(ImageRowViewModel row) => ShowDetailAsync(row.Title, () => _monitor.InspectImageAsync(row.Image));

    private Task InspectVolumeAsync(VolumeRowViewModel row) => ShowDetailAsync(row.Volume.Name, () => _monitor.InspectVolumeAsync(row.Volume));

    private Task InspectNetworkAsync(NetworkRowViewModel row) => ShowDetailAsync(row.Title, () => _monitor.InspectNetworkAsync(row.Network));

    private Task ShowDetailAsync(string title, Func<Task<Inspection>> load) => GuardAsync(async () =>
    {
        var tab = Tab;
        var inspection = await load();

        // The user may have moved to another tab while the daemon answered.
        if (Tab == tab)
            Detail = new InspectViewModel(title, inspection);
    });

    // Deleting a volume deletes its data, and the daemon refuses one that a container still mounts.
    private Task DeleteVolumeAsync(VolumeRowViewModel row) => GuardAsync(async () =>
    {
        await _monitor.DeleteVolumeAsync(row.Volume);
        await LoadVolumesAsync();
    });

    // The daemon refuses a network that containers are still attached to, and the built-in ones always.
    private Task DeleteNetworkAsync(NetworkRowViewModel row) => GuardAsync(async () =>
    {
        await _monitor.DeleteNetworkAsync(row.Network);
        await LoadNetworksAsync();
    });

    private async Task LoadNetworksAsync() =>
        Networks = (await _monitor.ListNetworksAsync())
            .OrderBy(n => n.IsBuiltIn)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => new NetworkRowViewModel(n, _monitor.ContainersOnNetwork(n.Name).Count, DeleteNetworkAsync, InspectNetworkAsync))
            .ToList();

    private async Task LoadVolumesAsync() =>
        Volumes = (await _monitor.ListVolumesAsync())
            .OrderByDescending(v => v.Size)
            .Select(v => new VolumeRowViewModel(v, DeleteVolumeAsync, InspectVolumeAsync))
            .ToList();

    private Task DeleteImageAsync(ImageRowViewModel row) => GuardAsync(async () =>
    {
        try
        {
            await _monitor.DeleteImageAsync(row.Image);
        }
        catch
        {
            // The list may have changed even though the delete failed; keep the delete error, not a reload one.
            try
            {
                await LoadImagesAsync();
            }
            catch (Exception)
            {
            }

            throw;
        }

        await LoadImagesAsync();
    });

    private async Task LoadImagesAsync() =>
        Images = (await _monitor.ListImagesAsync())
            .OrderByDescending(i => i.Size)
            .Select(i => new ImageRowViewModel(i, DeleteImageAsync, InspectImageAsync))
            .ToList();

    [RelayCommand]
    private Task PruneImagesAsync() => GuardAsync(async () =>
    {
        Result = Loc.Get("Resources.FreedImages", Format.Size((ulong)await _monitor.PruneImagesAsync()));
        await LoadAsync();
    });

    [RelayCommand]
    private Task PruneVolumesAsync() => GuardAsync(async () =>
    {
        Result = Loc.Get("Resources.FreedVolumes", Format.Size((ulong)await _monitor.PruneVolumesAsync()));
        await LoadAsync();
    });

    private async Task LoadAsync()
    {
        var usage = await _monitor.GetDiskUsageAsync();
        Rows =
        [
            Row(Loc.Get("Resources.Images"), usage.Images),
            Row(Loc.Get("Resources.Containers"), usage.Containers),
            Row(Loc.Get("Resources.Volumes"), usage.Volumes),
            Row(Loc.Get("Resources.BuildCache"), usage.BuildCache),
        ];
    }

    private static DiskRow Row(string name, UsageLine line) =>
        new(name, line.Count.ToString(), Format.Size((ulong)line.Size), Format.Size((ulong)line.Reclaimable));

    private async Task GuardAsync(Func<Task> action)
    {
        Error = null;
        Result = null;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
