using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyDock.App.ViewModels;

namespace MyDock.App.Views;

public partial class LogView : UserControl
{
    public static readonly StyledProperty<bool> ShowPopOutProperty = AvaloniaProperty.Register<LogView, bool>(nameof(ShowPopOut), true);

    private LogViewModel? _viewModel;

    public LogView()
    {
        InitializeComponent();
    }

    public event Action? CloseRequested;

    public event Action? PopOutRequested;

    /// <summary>False inside the dedicated window, which is already the expanded view.</summary>
    public bool ShowPopOut
    {
        get => GetValue(ShowPopOutProperty);
        set => SetValue(ShowPopOutProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as LogViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // shortcut: always follows the end, even if the reader scrolled up; add a "near the bottom" check if that annoys.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewModel.Text))
            Dispatcher.UIThread.Post(Scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && _viewModel is { } viewModel)
            await clipboard.SetTextAsync(viewModel.Text);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnPopOut(object? sender, RoutedEventArgs e) => PopOutRequested?.Invoke();
}
