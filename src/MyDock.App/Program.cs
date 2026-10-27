using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using MyDock.App.Services;

namespace MyDock.App;

internal static class Program
{
    private const string Usage = """
        myDock: running Docker containers and their resource usage.

        Usage: mydock [option]
          (none)      start and show the panel; if already running, show it
          --hidden    start with the tray icon only, without opening the panel
          --toggle    open or close the panel of the running instance
          --quit      quit the running instance
          --dump      print the containers and their resources to the terminal and exit
          --help      show this message
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        // The UI is English-only, so numbers must not follow the OS locale ("2,1%").
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args.Contains("--dump"))
            return DumpCommand.RunAsync().GetAwaiter().GetResult();

        if (args.Contains("--quit"))
        {
            SingleInstance.TrySend(SingleInstance.Quit);
            return 0;
        }

        var startHidden = args.Contains("--hidden");
        var command = args.Contains("--toggle") ? SingleInstance.Toggle : SingleInstance.Show;
        if (SingleInstance.TrySend(command))
            return 0;

        using var instance = SingleInstance.Listen();
        return BuildAvaloniaApp(() => new App(instance, startHidden))
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the XAML previewer, which needs a parameterless entry point.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new App());

    private static AppBuilder BuildAvaloniaApp(Func<App> factory) =>
        AppBuilder.Configure(factory)
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "mydock" })
            .WithInterFont();
}
