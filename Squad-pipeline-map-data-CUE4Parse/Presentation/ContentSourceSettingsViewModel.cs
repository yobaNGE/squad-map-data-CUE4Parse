using System.Windows.Input;
using Squad_pipeline_map_data_CUE4Parse.Configuration;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Presentation;

public sealed class ContentSourceSettingsViewModel : ObservableObject
{
    private bool _isEnabled;
    private SourceCacheState _cacheState;

    public ContentSourceSettingsViewModel(
        InstalledContentSource source,
        ModArchiveProfile? mod,
        SourceCacheState cacheState,
        Action<ContentSourceSettingsViewModel> clear,
        Func<ContentSourceSettingsViewModel, Task> rebuild)
    {
        Source = source;
        Mod = mod;
        _isEnabled = source.Enabled;
        _cacheState = cacheState;
        ClearCacheCommand = new RelayCommand(() => clear(this));
        RebuildCacheCommand = new AsyncRelayCommand(() => rebuild(this), () => IsEnabled);
    }

    public InstalledContentSource Source { get; private set; }
    public ModArchiveProfile? Mod { get; }
    public string Id => Source.Id;
    public string Name => Source.Name;
    public bool IsMod => !Source.IsVanilla;
    public string Version => string.IsNullOrWhiteSpace(Source.Version) ? "Unknown" : Source.Version;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!IsMod || !SetProperty(ref _isEnabled, value)) return;
            Source = Source with { Enabled = value };
            OnPropertyChanged(nameof(CacheStatus));
            (RebuildCacheCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string CacheStatus => !IsEnabled && IsMod ? "Disabled" : _cacheState.Status.ToString();
    public string CachedVersion => string.IsNullOrWhiteSpace(_cacheState.CachedVersion)
        ? "No cached version"
        : _cacheState.CachedVersion;
    public string CacheSize => FormatSize(_cacheState.Size);
    public string CacheLayers => _cacheState.LayerCount == 0
        ? "No cached layers"
        : $"{_cacheState.MaterializedLayerCount} of {_cacheState.LayerCount} layers ready";

    public ICommand ClearCacheCommand { get; }
    public ICommand RebuildCacheCommand { get; }

    public void UpdateCache(SourceCacheState state)
    {
        _cacheState = state;
        OnPropertyChanged(nameof(CacheStatus));
        OnPropertyChanged(nameof(CachedVersion));
        OnPropertyChanged(nameof(CacheSize));
        OnPropertyChanged(nameof(CacheLayers));
    }

    public ModArchiveProfile ToProfile() => (Mod ?? throw new InvalidOperationException()) with
    {
        Enabled = IsEnabled,
        Version = Source.Version,
        ContentRevision = Source.Revision,
        InstalledSize = Source.InstalledSize
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
