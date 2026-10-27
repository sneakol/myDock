using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MyDock.App.Services;
using MyDock.App.ViewModels;
using MyDock.App.Views;
using MyDock.Core.Docker;
using MyDock.Core.Models;
using MyDock.Core.Monitoring;

namespace MyDock.App;

public partial class App : Application
{
    private readonly SingleInstance? _instance;
    private readonly bool _startHidden;

    private DockerService? _docker;
    private ContainerMonitor? _monitor;
    private PanelViewModel? _viewModel;
    private PanelWindow? _panel;
    private TrayIcon? _tray;
    private NativeMenuItem? _toggleItem;
    private int _trayUpdateQueued;

    public App()
        : this(null, startHidden: false)
    {
    }

    public App(SingleInstance? instance, bool startHidden)
    {
        _instance = instance;
        _startHidden = startHidden;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            _docker = new DockerService();
            _monitor = new ContainerMonitor(_docker);
            _viewModel = new PanelViewModel(_monitor, new SettingsStore(), _docker.Endpoint.ToString());
            _panel = new PanelWindow { DataContext = _viewModel };
            _panel.PropertyChanged += OnPanelPropertyChanged;

            CreateTray();
            _monitor.Changed += OnMonitorChanged;
            _monitor.Start();

            if (_instance is not null)
                _instance.CommandReceived += command => Dispatcher.UIThread.Post(() => OnCommand(command));

            desktop.Exit += (_, _) => Cleanup();

            if (!_startHidden)
                _panel.Reveal();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        _toggleItem = new NativeMenuItem("Show panel");
        _toggleItem.Click += (_, _) => _panel?.Toggle();

        var autostart = new NativeMenuItem("Start at login")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled,
        };
        autostart.Click += (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled);
            autostart.IsChecked = Autostart.IsEnabled;
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();

        // On GNOME a click on the icon opens this menu rather than activating the icon, so showing the panel comes first.
        _tray = new TrayIcon
        {
            Icon = TrayIconRenderer.Render(0, error: false),
            ToolTipText = "myDock",
            Menu = [_toggleItem, new NativeMenuItemSeparator(), autostart, new NativeMenuItemSeparator(), quit],
        };
        _tray.Clicked += (_, _) => _panel?.Toggle();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void OnCommand(string command)
    {
        switch (command)
        {
            case SingleInstance.Toggle:
                _panel?.Toggle();
                break;
            case SingleInstance.Quit:
                Quit();
                break;
            default:
                _panel?.Reveal();
                break;
        }
    }

    private void Quit()
    {
        _panel?.Shutdown();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // Sampling costs the daemon CPU, so it only runs while somebody is looking.
    private void OnPanelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || _monitor is null)
            return;

        var visible = e.GetNewValue<bool>();
        if (visible)
            _viewModel?.ResetHistory();
        _monitor.StatsEnabled = visible;
        _toggleItem?.Header = visible ? "Hide panel" : "Show panel";
    }

    private void OnMonitorChanged()
    {
        if (Interlocked.Exchange(ref _trayUpdateQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _trayUpdateQueued, 0);
            UpdateTray();
        });
    }

    private void UpdateTray()
    {
        if (_tray is null || _monitor is null)
            return;

        var snapshot = _monitor.Current;
        var error = snapshot.Connection is ConnectionState.DaemonUnavailable or ConnectionState.PermissionDenied;
        var icon = TrayIconRenderer.Render(snapshot.ActiveCount, error);
        if (!ReferenceEquals(_tray.Icon, icon))
            _tray.Icon = icon;

        _tray.ToolTipText = snapshot.Connection switch
        {
            ConnectionState.PermissionDenied => "myDock: permission denied on the Docker socket",
            ConnectionState.DaemonUnavailable => "myDock: Docker unreachable",
            ConnectionState.Connecting => "myDock",
            _ => snapshot.ActiveCount == 1 ? "myDock: 1 active container" : $"myDock: {snapshot.ActiveCount} active containers",
        };
    }

    private void Cleanup()
    {
        _tray?.Dispose();
        if (_monitor is not null)
        {
            _monitor.Changed -= OnMonitorChanged;
            _monitor.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        _docker?.Dispose();
    }
}
