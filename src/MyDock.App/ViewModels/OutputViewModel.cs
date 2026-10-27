using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Docker;
using MyDock.Core.Localization;

namespace MyDock.App.ViewModels;

/// <summary>The live output of one long-running docker command, with a way to cancel it.</summary>
public sealed partial class OutputViewModel : ObservableObject
{
    private const int MaxLines = 2000;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private string _pending = "";
    private int _flushQueued;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    private bool _isRunning = true;

    public OutputViewModel(string title) => Title = title;

    public string Title { get; }

    public bool IsFinished => !IsRunning;

    [RelayCommand]
    private void Cancel() => _cts.Cancel();

    /// <summary>Runs <paramref name="run"/> and shows what it prints. False when cancelled; failures are shown here and thrown on.</summary>
    public async Task<bool> RunAsync(Func<Action<string>, CancellationToken, Task> run)
    {
        try
        {
            await run(Append, _cts.Token);
            Status = Loc.Get("Output.Done");
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = Loc.Get("Output.Cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Status = Loc.Get("Output.Failed", ex.Message);
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }

    // Called from the process pipe threads; a chatty build produces lines faster than the UI needs, so fold them into one update.
    private void Append(string chunk)
    {
        lock (_gate)
            _pending += chunk;

        if (Interlocked.Exchange(ref _flushQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _flushQueued, 0);
            string pending;
            lock (_gate)
            {
                pending = _pending;
                _pending = "";
            }

            if (pending.Length > 0)
                Text = LogText.Append(Text, pending, MaxLines);
        });
    }
}
