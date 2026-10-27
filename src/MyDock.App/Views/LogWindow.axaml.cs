using Avalonia.Controls;
using MyDock.App.ViewModels;

namespace MyDock.App.Views;

/// <summary>The log view taken out of the panel, so it stays when the panel hides.</summary>
public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        Logs.CloseRequested += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as LogViewModel)?.Dispose();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Title = DataContext is LogViewModel log ? $"{log.Title} · logs" : "myDock logs";
    }
}
