using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyDock.App.ViewModels;

namespace MyDock.App.Views;

public partial class OutputView : UserControl
{
    private OutputViewModel? _viewModel;

    public OutputView() => InitializeComponent();

    public event Action? CloseRequested;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as OutputViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // Follows the end like the logs do: a build is read as it goes.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputViewModel.Text))
            Dispatcher.UIThread.Post(Scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && _viewModel is { } viewModel)
                await clipboard.SetTextAsync(viewModel.Text);
        }
        catch (Exception)
        {
            // async void: a clipboard failure must not take the app down.
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
