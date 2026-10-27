using Avalonia;
using Avalonia.Controls;
using MyDock.App.Services;

namespace MyDock.App;

internal static class Program
{
    private const string Usage = """
        myDock: container Docker attivi e risorse utilizzate.

        Uso: mydock [opzione]
          (nessuna)   avvia e mostra il pannello; se è già in esecuzione, lo mostra
          --hidden    avvia solo l'icona nella barra, senza aprire il pannello
          --toggle    apre o chiude il pannello dell'istanza in esecuzione
          --quit      chiude l'istanza in esecuzione
          --dump      stampa i container e le loro risorse sul terminale ed esce
          --help      mostra questo messaggio
        """;

    [STAThread]
    public static int Main(string[] args)
    {
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
