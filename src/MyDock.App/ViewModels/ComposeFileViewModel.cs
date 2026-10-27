using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyDock.App.Services;
using MyDock.Core.Docker;

namespace MyDock.App.ViewModels;

/// <summary>A loaded Dockerfile or compose file, with the .env files found next to it.</summary>
public sealed partial class ComposeFileViewModel : ObservableObject
{
    private readonly IComposeRunner _runner;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly Action<ComposeFileViewModel> _remove;

    [ObservableProperty] private IReadOnlyList<string> _envFiles = [];
    [ObservableProperty] private string? _selectedEnv;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private string? _projectName;
    [ObservableProperty] private bool _isProjectRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public ComposeFileViewModel(string path, IComposeRunner runner, Settings settings, SettingsStore store, Action<ComposeFileViewModel> remove)
    {
        Path = path;
        _runner = runner;
        _settings = settings;
        _store = store;
        _remove = remove;
        Refresh();
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)) + "/" + System.IO.Path.GetFileName(Path);

    public bool HasProject => ProjectName is not null;

    private string? EnvPath => SelectedEnv is null ? null : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, SelectedEnv);

    public bool IsCompose => ComposeFiles.Detect(Path) == ComposeFileKind.Compose;

    public bool HasEnvFiles => EnvFiles.Count > 0;

    /// <summary>Rescans the folder, keeping the remembered choice if that file still exists.</summary>
    public void Refresh()
    {
        EnvFiles = ComposeFiles.FindEnvFiles(Path).Select(System.IO.Path.GetFileName).ToList()!;
        OnPropertyChanged(nameof(HasEnvFiles));
        SelectedEnv = _settings.EnvChoice.TryGetValue(Path, out var saved) && EnvFiles.Contains(saved) ? saved : EnvFiles.FirstOrDefault();
        ProjectName = ComposeFiles.ProjectName(Path, EnvPath);
    }

    /// <summary>Running when at least one container of the project is active, so a mixed project offers "stop" too.</summary>
    public void Track(IEnumerable<ContainerViewModel> containers) =>
        IsProjectRunning = ProjectName is { } project && containers.Any(c => c.IsActive && c.ComposeProject == project);

    partial void OnSelectedEnvChanged(string? value)
    {
        ProjectName = ComposeFiles.ProjectName(Path, EnvPath);
        if (value is null || _settings.EnvChoice.GetValueOrDefault(Path) == value)
            return;

        _settings.EnvChoice[Path] = value;
        _store.Save(_settings);
    }

    [RelayCommand]
    private Task RunAsync() => GuardAsync(() => _runner.RunAsync(Path, EnvPath, CancellationToken.None));

    [RelayCommand]
    private Task RecreateAsync() => GuardAsync(() => _runner.RunAsync(Path, EnvPath, CancellationToken.None, recreate: true));

    [RelayCommand]
    private Task StopAsync() => GuardAsync(() => _runner.RunProjectAsync(ProjectName!, ProjectAction.Stop, CancellationToken.None));

    private async Task GuardAsync(Func<Task> action)
    {
        Error = null;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Remove() => _remove(this);
}
