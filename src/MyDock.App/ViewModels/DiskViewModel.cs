using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Formatting;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App.ViewModels;

public sealed record DiskRow(string Name, string Count, string Size, string Reclaimable);

/// <summary>What <c>docker system df</c> reports, with the prunes that are safe to offer.</summary>
public sealed partial class DiskViewModel : ObservableObject
{
    private readonly ContainerMonitor _monitor;

    [ObservableProperty] private IReadOnlyList<DiskRow> _rows = [];
    [ObservableProperty] private IReadOnlyList<ImageRowViewModel> _images = [];
    [ObservableProperty] private bool _showImages;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _result;

    public DiskViewModel(ContainerMonitor monitor)
    {
        _monitor = monitor;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => GuardAsync(ShowImages ? LoadImagesAsync : LoadAsync);

    [RelayCommand]
    private Task ShowOverviewAsync()
    {
        ShowImages = false;
        return RefreshAsync();
    }

    [RelayCommand]
    private Task ShowImagesTabAsync()
    {
        ShowImages = true;
        return RefreshAsync();
    }

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
            .Select(i => new ImageRowViewModel(i, DeleteImageAsync))
            .ToList();

    [RelayCommand]
    private Task PruneImagesAsync() => GuardAsync(async () =>
    {
        Result = "Images: freed " + Format.Size((ulong)await _monitor.PruneImagesAsync());
        await LoadAsync();
    });

    [RelayCommand]
    private Task PruneVolumesAsync() => GuardAsync(async () =>
    {
        Result = "Volumes: freed " + Format.Size((ulong)await _monitor.PruneVolumesAsync());
        await LoadAsync();
    });

    private async Task LoadAsync()
    {
        var usage = await _monitor.GetDiskUsageAsync();
        Rows =
        [
            Row("Images", usage.Images),
            Row("Containers", usage.Containers),
            Row("Volumes", usage.Volumes),
            Row("Build cache", usage.BuildCache),
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
