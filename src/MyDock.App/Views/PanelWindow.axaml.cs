using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MyDock.App.ViewModels;

namespace MyDock.App.Views;

public partial class PanelWindow : Window
{
    /// <summary>Gap between the panel and the edges of the screen's working area, in device-independent pixels.</summary>
    private const int ScreenGap = 4;

    // Opening from the tray menu makes focus bounce for a moment: a blur that soon after showing is not the user leaving.
    private static readonly TimeSpan BlurGrace = TimeSpan.FromMilliseconds(400);

    private PanelViewModel? _viewModel;
    private DateTime _shownAt;
    private bool _closing;

    public PanelWindow()
    {
        InitializeComponent();
        Header.PointerPressed += OnHeaderPointerPressed;
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Reveal();
    }

    public void Reveal()
    {
        _shownAt = DateTime.UtcNow;
        PlaceOnScreen();
        Show();
        Activate();
    }

    /// <summary>Closes the window for good; a plain close request only hides it.</summary>
    public void Shutdown()
    {
        _closing = true;
        Close();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.HideRequested -= Hide;
        _viewModel = DataContext as PanelViewModel;
        if (_viewModel is not null)
            _viewModel.HideRequested += Hide;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape)
            return;

        if (_viewModel is { IsDeleting: true })
            _viewModel.CancelDeleteCommand.Execute(null);
        else
            Hide();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty
            && !change.GetNewValue<bool>()
            && IsVisible
            && _viewModel is { IsPinned: false }
            && DateTime.UtcNow - _shownAt > BlurGrace)
        {
            Hide();
        }
    }

    /// <summary>Top-right corner of the primary screen, just below the top bar, and never taller than the screen.</summary>
    private void PlaceOnScreen()
    {
        if ((Screens.Primary ?? Screens.All.FirstOrDefault()) is not { } screen)
            return;

        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        MaxHeight = area.Height / scaling - 2 * ScreenGap;
        Position = new PixelPoint(
            area.Right - (int)Math.Ceiling((Width + ScreenGap) * scaling),
            area.Y + (int)(ScreenGap * scaling));
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void OnCopyFixCommand(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && _viewModel is { } viewModel)
            await clipboard.SetTextAsync(viewModel.FixCommand);
    }
}
