using System.Diagnostics;
using MyDock.Core.Monitoring;

namespace MyDock.App.Services;

/// <summary>Desktop notifications through libnotify's notify-send. Never throws: it is called from the monitor's event loop.</summary>
public static class Notifier
{
    public static void Show(ContainerAlert alert)
    {
        try
        {
            var info = new ProcessStartInfo("notify-send");
            foreach (var arg in (string[])["-a", "myDock", "-i", "mydock", "--", "myDock", alert.Message])
                info.ArgumentList.Add(arg);

            var process = Process.Start(info);
            _ = process?.WaitForExitAsync().ContinueWith(_ => process.Dispose());
        }
        catch (Exception)
        {
            // shortcut: no fallback when notify-send (libnotify-bin) is missing, add one if that turns out common.
        }
    }
}
