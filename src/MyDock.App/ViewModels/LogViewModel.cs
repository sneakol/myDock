using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.Core.Docker;
using MyDock.Core.Monitoring;

namespace MyDock.App.ViewModels;

/// <summary>The recent log of one container; with <see cref="IsLive"/> it keeps following new output.</summary>
public sealed partial class LogViewModel : ObservableObject, IDisposable
{
    private const int Tail = 200;
    private const int LiveMaxLines = 2000;

    private readonly ContainerMonitor _monitor;
    private readonly string _id;
    private readonly Lock _gate = new();
    private string _pending = "";
    private CancellationTokenSource _cts = new();
    private int _flushQueued;
    private int _maxLines = Tail;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;

    public LogViewModel(ContainerMonitor monitor, ContainerViewModel container)
    {
        _monitor = monitor;
        _id = container.Id;
        Title = container.Name;
        _ = LoadAsync();
    }

    public string Title { get; }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    partial void OnIsLiveChanged(bool value) => _ = LoadAsync();

    public void Dispose() => _cts.Cancel();

    private async Task LoadAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        lock (_gate)
            _pending = "";
        Text = "";
        Error = null;
        IsLoading = true;
        _maxLines = IsLive ? LiveMaxLines : Tail;

        try
        {
            await foreach (var chunk in _monitor.StreamLogsAsync(_id, Tail, IsLive, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                lock (_gate)
                    _pending += chunk;
                QueueFlush();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Dispatcher.UIThread.Post(() => Error = ex.Message);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    // A busy container logs far faster than the UI needs: fold whatever piles up into one text update.
    private void QueueFlush()
    {
        if (Interlocked.Exchange(ref _flushQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _flushQueued, 0);
            string chunk;
            lock (_gate)
            {
                chunk = _pending;
                _pending = "";
            }

            if (chunk.Length == 0)
                return;

            Text = LogText.Append(Text, chunk, _maxLines);
            IsLoading = false;
        });
    }
}
