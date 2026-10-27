using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MyDock.App.ViewModels;

namespace MyDock.App.Views;

public partial class ContainerRow : UserControl
{
    public ContainerRow() => InitializeComponent();

    private async void OnCopyName(object? sender, RoutedEventArgs e) => await CopyAsync(vm => vm.Name);

    private async void OnCopyId(object? sender, RoutedEventArgs e) => await CopyAsync(vm => vm.ShortId);

    private void OnOpenPort(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ContainerViewModel vm && (sender as Button)?.Tag is int port)
            vm.OpenPort(port);
    }

    private async Task CopyAsync(Func<ContainerViewModel, string> text)
    {
        try
        {
            if (DataContext is ContainerViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text(vm));
        }
        catch (Exception)
        {
            // async void callers: a clipboard failure (common under Wayland) must not take the app down.
        }
    }
}
