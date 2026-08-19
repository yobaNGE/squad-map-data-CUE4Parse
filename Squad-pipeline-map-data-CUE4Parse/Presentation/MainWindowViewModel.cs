using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using Squad_pipeline_map_data_CUE4Parse.Application;
using Squad_pipeline_map_data_CUE4Parse.Configuration;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Presentation;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string AllMaps = "All maps";
    private const string AllGameModes = "All game modes";
    private const string AllSources = "All sources";

    private readonly ProfileStore _profileStore = new();
    private readonly LayerSelectionPresetStore _selectionPresets = new();
    private readonly WorkshopModDiscovery _modDiscovery = new();
    private readonly SdkPluginDiscovery _sdkPluginDiscovery = new();
    private readonly ContentVersionService _versions = new();
    private readonly LayerCacheStore _cache = new();
    private ArchiveProfile _profile = new();
    private SourceAssetProviderPool? _providerPool;
    private readonly ConcurrentDictionary<string, Lazy<Task<ILayerMetadataReader>>> _rawMetadataReaders =
        new(StringComparer.OrdinalIgnoreCase);
    private ILayerMetadataReader? _metadataReader;
    private string _mappingsSignature = string.Empty;
    private IReadOnlyDictionary<string, string> _environmentKeys = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _sourceKeys = new Dictionary<string, string>();
    private CancellationTokenSource? _operationCancellation;
    private bool _suppressSelectionNotifications;
    private ContentLayoutKind _contentLayoutKind = ContentLayoutKind.Cooked;

    private string _squadPath = string.Empty;
    private string _mappingsPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _workshopPath = string.Empty;
    private int _exportParallelism = 2;
    private bool _ignoreMissingFactionPrimaryAssets;
    private bool _skipVehiclesWithoutDataRows;
    private bool _writeExportProfile;
    private string _searchText = string.Empty;
    private string _selectedMap = AllMaps;
    private string _selectedGameMode = AllGameModes;
    private string _selectedSource = AllSources;
    private string _statusMessage = "Configure the Squad installation and mappings file";
    private string _busyText = string.Empty;
    private bool _isBusy;
    private bool _isSettingsOpen;
    private bool _isSettingsDirty;
    private bool _isIndeterminate;
    private double _progressValue;
    private double _progressMaximum = 1;

    public MainWindowViewModel()
    {
        LayersView = CollectionViewSource.GetDefaultView(Layers);
        LayersView.Filter = FilterLayer;

        OpenSettingsCommand = new RelayCommand(OpenSettings, () => !IsBusy);
        CloseSettingsCommand = new RelayCommand(CloseSettings, () => !IsBusy);
        RefreshModsCommand = new RelayCommand(RefreshMods, () => !IsBusy);
        ResetFiltersCommand = new RelayCommand(ResetFilters);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && HasValidProfile);
        ExportCommand = new AsyncRelayCommand(ExportSelectedAsync, () => !IsBusy && SelectedCount > 0);
        CancelCommand = new RelayCommand(() => _operationCancellation?.Cancel(), () => IsBusy);
    }

    public event EventHandler<string>? ErrorOccurred;

    public ObservableCollection<LayerRowViewModel> Layers { get; } = [];
    public ICollectionView LayersView { get; }
    public ObservableCollection<string> MapOptions { get; } = [AllMaps];
    public ObservableCollection<string> GameModeOptions { get; } = [AllGameModes];
    public ObservableCollection<string> SourceOptions { get; } = [AllSources];
    public ObservableCollection<ContentSourceSettingsViewModel> ContentSources { get; } = [];
    public IReadOnlyList<int> ParallelismOptions { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand RefreshModsCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CancelCommand { get; }

    public string SquadPath
    {
        get => _squadPath;
        set
        {
            if (SetProperty(ref _squadPath, value)) MarkSettingsDirty();
        }
    }

    public string MappingsPath
    {
        get => _mappingsPath;
        set
        {
            if (SetProperty(ref _mappingsPath, value)) MarkSettingsDirty();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value)) MarkSettingsDirty();
        }
    }

    public string WorkshopPath
    {
        get => _workshopPath;
        set
        {
            if (SetProperty(ref _workshopPath, value)) MarkSettingsDirty();
        }
    }

    public bool IsEditorSdk => _contentLayoutKind == ContentLayoutKind.EditorSdk;
    public bool UsesWorkshop => !IsEditorSdk;
    public string ContentModeLabel => IsEditorSdk ? "Squad SDK · uncooked assets" : "Squad game · cooked archives";
    public string MappingsLabel => IsEditorSdk ? "Mappings file (.usmap, optional for SDK)" : "Mappings file (.usmap)";
    public string AddonSectionTitle => IsEditorSdk ? "SDK MOD PLUGINS" : "WORKSHOP MODS";
    public string AddonSectionSubtitle => IsEditorSdk
        ? "Detected from Squad/Plugins/Mods"
        : "Detected from the Steam Workshop library";

    public int ExportParallelism
    {
        get => _exportParallelism;
        set
        {
            if (SetProperty(ref _exportParallelism, value)) MarkSettingsDirty();
        }
    }

    public bool IgnoreMissingFactionPrimaryAssets
    {
        get => _ignoreMissingFactionPrimaryAssets;
        set
        {
            if (SetProperty(ref _ignoreMissingFactionPrimaryAssets, value)) MarkSettingsDirty();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            RefreshFilter();
        }
    }

    public string SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (!SetProperty(ref _selectedMap, value)) return;
            RefreshFilter();
        }
    }

    public string SelectedGameMode
    {
        get => _selectedGameMode;
        set
        {
            if (!SetProperty(ref _selectedGameMode, value)) return;
            RefreshFilter();
        }
    }

    public bool SkipVehiclesWithoutDataRows
    {
        get => _skipVehiclesWithoutDataRows;
        set
        {
            if (SetProperty(ref _skipVehiclesWithoutDataRows, value)) MarkSettingsDirty();
        }
    }

    public bool WriteExportProfile
    {
        get => _writeExportProfile;
        set
        {
            if (SetProperty(ref _writeExportProfile, value)) MarkSettingsDirty();
        }
    }

    public string SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetProperty(ref _selectedSource, value)) return;
            RefreshFilter();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsNotBusy));
            OnPropertyChanged(nameof(ScanContentHint));
            RaiseCommandStates();
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, value);
    }

    public int TotalCount => Layers.Count;
    public int VisibleCount => VisibleRows().Count;
    public int SelectedCount => Layers.Count(layer => layer.IsSelected);
    public bool HasLayers => Layers.Count > 0;
    public bool HasNoLayers => !HasLayers;
    public bool HasMods => ContentSources.Any(source => source.IsMod);
    public bool HasNoMods => !HasMods;
    public bool HasValidProfile => Directory.Exists(_profile.SquadPath)
                                   && (ContentLayoutDetector.Detect(_profile.SquadPath).IsEditorSdk
                                       ? string.IsNullOrWhiteSpace(_profile.MappingsPath) || File.Exists(_profile.MappingsPath)
                                       : File.Exists(_profile.MappingsPath));
    public bool IsSettingsDirty => _isSettingsDirty;
    public string CloseSettingsLabel => IsSettingsDirty ? "Discard changes" : "Close";
    public string ScanContentHint => IsBusy
        ? "Wait for the current operation to finish."
        : HasValidProfile
            ? "Scans the layer catalogs of vanilla and every enabled mod."
            : "Save an existing Squad installation and mappings file before scanning.";

    public bool? SelectAllVisible
    {
        get
        {
            var visible = VisibleRows();
            if (visible.Count == 0 || visible.All(layer => !layer.IsSelected)) return false;
            return visible.All(layer => layer.IsSelected) ? true : null;
        }
        set
        {
            if (value is null) return;
            _suppressSelectionNotifications = true;
            try
            {
                foreach (var layer in VisibleRows()) layer.IsSelected = value.Value;
            }
            finally
            {
                _suppressSelectionNotifications = false;
                NotifySelectionChanged();
            }
        }
    }

    public void SaveSelection(string path)
    {
        var selected = Layers.Where(layer => layer.IsSelected).Select(layer => layer.Descriptor).ToArray();
        if (selected.Length == 0)
        {
            ReportError("Select at least one layer before saving a selection.");
            return;
        }

        try
        {
            _selectionPresets.Save(path, new LayerSelectionPreset(
                LayerSelectionPreset.CurrentFormat,
                selected.Select(layer => new LayerSelectionPresetItem(
                    layer.Source.Id,
                    layer.GameplayPackagePath,
                    layer.GameplayObjectName)).ToArray()));
            StatusMessage = $"Saved {selected.Length} selected layers";
        }
        catch (IOException exception)
        {
            ReportError($"Unable to save selection: {exception.Message}");
        }
    }

    public void LoadSelection(string path)
    {
        if (Layers.Count == 0)
        {
            ReportError("Scan enabled content before loading a selection.");
            return;
        }

        try
        {
            var preset = _selectionPresets.Load(path);
            var ids = preset.Layers.Select(SelectionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var restored = 0;
            _suppressSelectionNotifications = true;
            try
            {
                foreach (var layer in Layers)
                {
                    layer.IsSelected = ids.Contains(SelectionId(layer.Descriptor));
                    if (layer.IsSelected) restored++;
                }
            }
            finally
            {
                _suppressSelectionNotifications = false;
                NotifySelectionChanged();
            }

            StatusMessage = $"Restored {restored} selected layers" +
                            (ids.Count == restored ? string.Empty : $" · {ids.Count - restored} missing");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            ReportError($"Unable to load selection: {exception.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        _profile = _profileStore.Load();
        LoadEditor(_profile);
        RefreshModsPreview();

        if (!HasValidProfile)
        {
            IsSettingsOpen = true;
            OnPropertyChanged(nameof(ScanContentHint));
            RaiseCommandStates();
            return;
        }

        _profile = BuildProfile();
        LoadCachedCatalog(_profile);
        OnPropertyChanged(nameof(HasValidProfile));
        OnPropertyChanged(nameof(ScanContentHint));
        RaiseCommandStates();
        await Task.CompletedTask;
    }

    public void SetSquadPath(string path)
    {
        SquadPath = path;
        UpdateContentLayout();
        WorkshopPath = UsesWorkshop ? _modDiscovery.ResolveWorkshopPath(path) : string.Empty;
        RefreshModsPreview();
    }

    public void OpenSettings()
    {
        LoadEditor(_profile);
        ContentSources.Clear();
        RefreshModsPreview();
        IsSettingsOpen = true;
        ClearSettingsDirty();
    }

    private void CloseSettings()
    {
        IsSettingsOpen = false;
        LoadEditor(_profile);
        RebuildContentSources(_profile.Mods, _profile.SdkPlugins);
        ClearSettingsDirty();
    }

    private void LoadEditor(ArchiveProfile profile)
    {
        SquadPath = profile.SquadPath;
        MappingsPath = profile.MappingsPath ?? string.Empty;
        OutputDirectory = profile.OutputDirectory;
        ExportParallelism = Math.Clamp(profile.ExportParallelism, 1, 8);
        IgnoreMissingFactionPrimaryAssets = profile.IgnoreMissingFactionPrimaryAssets;
        SkipVehiclesWithoutDataRows = profile.SkipVehiclesWithoutDataRows;
        WriteExportProfile = profile.WriteExportProfile;
        UpdateContentLayout();
        WorkshopPath = UsesWorkshop
            ? _modDiscovery.ResolveWorkshopPath(profile.SquadPath, profile.WorkshopPath)
            : string.Empty;
    }

    private void RefreshModsPreview()
    {
        UpdateContentLayout();
        if (IsEditorSdk)
        {
            var enabledPlugins = ContentSources.Where(source => source.SdkPlugin is not null)
                .ToDictionary(source => source.Id, source => source.IsEnabled, StringComparer.OrdinalIgnoreCase);
            if (enabledPlugins.Count == 0)
                enabledPlugins = _profile.SdkPlugins.ToDictionary(
                    plugin => plugin.Id,
                    plugin => plugin.Enabled,
                    StringComparer.OrdinalIgnoreCase);

            var sdkPlugins = _sdkPluginDiscovery.Discover(SquadPath)
                .Select(plugin => plugin with { Enabled = enabledPlugins.GetValueOrDefault(plugin.Id, true) })
                .ToArray();
            RebuildContentSources([], sdkPlugins);
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(HasNoMods));
            return;
        }

        if (string.IsNullOrWhiteSpace(WorkshopPath))
            WorkshopPath = _modDiscovery.ResolveWorkshopPath(SquadPath);
        var enabled = ContentSources.Where(source => source.IsMod)
            .ToDictionary(source => source.Id, source => source.IsEnabled, StringComparer.OrdinalIgnoreCase);
        if (enabled.Count == 0)
            enabled = _profile.Mods.ToDictionary(mod => mod.Id, mod => mod.Enabled, StringComparer.OrdinalIgnoreCase);

        var mods = _modDiscovery.Discover(WorkshopPath)
            .Select(mod => mod with { Enabled = enabled.GetValueOrDefault(mod.Id, true) })
            .ToArray();
        RebuildContentSources(mods, []);
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasNoMods));
    }

    private void RefreshMods()
    {
        RefreshModsPreview();
        MarkSettingsDirty();
        StatusMessage = $"Refreshed {ContentSources.Count(source => source.IsMod)} mods. Save settings to apply changes.";
    }

    private Task SaveSettingsAsync()
    {
        var profile = BuildProfile();

        if (!Directory.Exists(profile.SquadPath))
        {
            ReportError("The Squad installation directory was not found.");
            return Task.CompletedTask;
        }
        if (!string.IsNullOrWhiteSpace(profile.MappingsPath)
            && !File.Exists(profile.MappingsPath))
        {
            ReportError("The mappings file was not found.");
            return Task.CompletedTask;
        }

        _profile = profile;
        _profileStore.Save(profile);
        DisposeProvider();
        IsSettingsOpen = false;
        RebuildContentSources(profile.Mods, profile.SdkPlugins);
        LoadCachedCatalog(profile);
        ClearSettingsDirty();
        OnPropertyChanged(nameof(HasValidProfile));
        OnPropertyChanged(nameof(ScanContentHint));
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    private void MarkSettingsDirty()
    {
        if (!IsSettingsOpen || _isSettingsDirty) return;
        _isSettingsDirty = true;
        OnPropertyChanged(nameof(IsSettingsDirty));
        OnPropertyChanged(nameof(CloseSettingsLabel));
    }

    private void ClearSettingsDirty()
    {
        if (!_isSettingsDirty) return;
        _isSettingsDirty = false;
        OnPropertyChanged(nameof(IsSettingsDirty));
        OnPropertyChanged(nameof(CloseSettingsLabel));
    }

    private Task RefreshAsync() => ScanAsync(_profile);

    private async Task ScanAsync(ArchiveProfile profile)
    {
        _profile = profile;
        await RunOperationAsync(
            IsEditorSdk ? "Mounting Squad SDK content…" : "Mounting Squad and Workshop content…",
            true,
            async cancellationToken =>
        {
            DisposeProvider();
            PrepareCacheContext(profile);
            var descriptors = new List<LayerDescriptor>();
            foreach (var source in CurrentSources().Where(source => source.Enabled))
            {
                try
                {
                    BusyText = $"Scanning {source.Name} layers…";
                    var provider = await EnsureProviderPool().GetAsync(source.Id, cancellationToken);
                    var scanned = await new LayerCatalogService(provider, source.Id).ScanAsync(cancellationToken);
                    var sourceLayers = scanned
                        .Where(layer => source.IsVanilla
                            ? layer.Source.IsVanilla
                            : layer.Source.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    _cache.SaveCatalog(source, _sourceKeys[source.Id], sourceLayers);
                    descriptors.AddRange(sourceLayers);
                }
                finally
                {
                    await ReleaseSourceAsync(source.Id);
                }
            }

            ConfigureMetadataReader();

            ShowLayers(descriptors);

            UpdateCacheStates();
        });
    }

    private async Task ExportSelectedAsync()
    {
        if (_metadataReader is null) return;
        var selected = Layers.Where(layer => layer.IsSelected).Select(layer => layer.Descriptor).ToArray();
        if (selected.Length == 0) return;

        await RunOperationAsync("Preparing export…", false, async cancellationToken =>
        {
            ProgressMaximum = selected.Length;
            ProgressValue = 0;
            var progress = new Progress<LayerExportProgress>(state =>
            {
                BusyText = $"Exporting {state.Completed} of {state.Total}: {state.LayerName} · " +
                           $"{FormatBytes(state.WorkingSetBytes)} RAM" +
                           (state.Cached == 0 ? string.Empty : $" · {state.Cached} cached") +
                           (state.Failed == 0 ? string.Empty : $" · {state.Failed} failed");
                ProgressValue = state.Completed;
            });
            var report = await new LayerExporter(_metadataReader, _profile.ExportParallelism).ExportAsync(
                selected,
                _profile.OutputDirectory,
                progress,
                ReleaseSourceAsync,
                cancellationToken,
                _profile.WriteExportProfile);

            if (report.Failures.Count > 0)
            {
                var logPath = Path.Combine(_profile.OutputDirectory, "export-errors.log");
                await File.WriteAllLinesAsync(
                    logPath,
                    report.Failures.Select(failure =>
                        $"[{failure.SourceId}] {failure.LayerName}: {failure.Message}"),
                    cancellationToken);
            }

            StatusMessage = $"Exported {report.Exported} layers" +
                            (report.Cached == 0 ? string.Empty : $" · {report.Cached} cached") +
                            (report.Failed == 0 ? string.Empty : $" · {report.Failed} failed") +
                            $" · {report.Elapsed.ToString(@"mm\:ss")} · peak {FormatBytes(report.PeakWorkingSetBytes)}";
            UpdateCacheStates();
        });
    }

    private ArchiveProfile BuildProfile() => new()
    {
        SquadPath = SquadPath.Trim(),
        MappingsPath = MappingsPath.Trim(),
        OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "output")
            : OutputDirectory.Trim(),
        WorkshopPath = string.IsNullOrWhiteSpace(WorkshopPath) ? null : WorkshopPath.Trim(),
        ExportParallelism = Math.Clamp(ExportParallelism, 1, 8),
        IgnoreMissingFactionPrimaryAssets = IgnoreMissingFactionPrimaryAssets,
        SkipVehiclesWithoutDataRows = SkipVehiclesWithoutDataRows,
        WriteExportProfile = WriteExportProfile,
        Mods = ContentSources.Where(source => source.Mod is not null)
            .Select(source => source.ToModProfile()).ToArray(),
        SdkPlugins = ContentSources.Where(source => source.SdkPlugin is not null)
            .Select(source => source.ToSdkPluginProfile()).ToArray(),
        ModDirectories = [],
        ReadScriptData = false
    };

    private void RebuildContentSources(
        IReadOnlyList<ModArchiveProfile> mods,
        IReadOnlyList<SdkPluginProfile> sdkPlugins)
    {
        var vanilla = _versions.ReadVanilla(SquadPath);
        _mappingsSignature = _versions.ReadMappingsSignature(MappingsPath);
        var sources = new[] { vanilla }
            .Concat(mods.Select(_versions.FromMod))
            .Concat(sdkPlugins.Select(_versions.FromSdkPlugin))
            .ToArray();
        var rows = sources.Select(source =>
        {
            var sourceKey = _cache.BuildSourceKey(source, vanilla, _mappingsSignature);
            var state = _cache.GetState(source, sourceKey);
            var mod = mods.FirstOrDefault(candidate => candidate.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            var sdkPlugin = sdkPlugins.FirstOrDefault(candidate =>
                candidate.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            return new ContentSourceSettingsViewModel(
                source, mod, sdkPlugin, state, RebuildCacheAsync, MarkSettingsDirty);
        });
        Replace(ContentSources, rows);
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasNoMods));
    }

    private IReadOnlyList<InstalledContentSource> CurrentSources() =>
        ContentSources.Select(source => source.Source).ToArray();

    private void PrepareCacheContext(ArchiveProfile profile)
    {
        _mappingsSignature = _versions.ReadMappingsSignature(profile.MappingsPath);
        var vanilla = _versions.ReadVanilla(profile.SquadPath);
        var sources = new[] { vanilla }
            .Concat(profile.Mods.Select(_versions.FromMod))
            .Concat(profile.SdkPlugins.Select(_versions.FromSdkPlugin))
            .ToArray();
        _sourceKeys = sources.ToDictionary(
            source => source.Id,
            source => _cache.BuildSourceKey(source, vanilla, _mappingsSignature),
            StringComparer.OrdinalIgnoreCase);
        var metadataSettings =
            $"{_mappingsSignature}|ignore-missing-faction-assets={profile.IgnoreMissingFactionPrimaryAssets}" +
            $"|skip-vehicles-without-data-rows={profile.SkipVehiclesWithoutDataRows}";
        var enabledSources = sources.Where(source => source.Enabled).ToArray();
        _environmentKeys = sources.ToDictionary(
            source => source.Id,
            source => _cache.BuildEnvironmentKey(
                enabledSources,
                metadataSettings),
            StringComparer.OrdinalIgnoreCase);
    }

    private void LoadCachedCatalog(ArchiveProfile profile)
    {
        PrepareCacheContext(profile);
        var descriptors = new List<LayerDescriptor>();
        foreach (var source in CurrentSources().Where(source => source.Enabled))
            descriptors.AddRange(_cache.LoadCatalog(source, _sourceKeys[source.Id]));

        ConfigureMetadataReader();
        ShowLayers(descriptors);
        UpdateCacheStates();
    }

    private void ConfigureMetadataReader() => _metadataReader = new CachedLayerMetadataReader(
        _cache,
        layer => _environmentKeys[layer.Source.Id],
        EnsureRawMetadataReaderAsync);

    private async Task<ILayerMetadataReader> EnsureRawMetadataReaderAsync(
        LayerDescriptor layer,
        CancellationToken cancellationToken)
    {
        var reader = _rawMetadataReaders.GetOrAdd(
            layer.Source.Id,
            sourceId => new Lazy<Task<ILayerMetadataReader>>(
                async () => new LayerMetadataReader(
                    await EnsureProviderPool().GetAsync(sourceId, CancellationToken.None),
                    _profile.IgnoreMissingFactionPrimaryAssets,
                    _profile.SkipVehiclesWithoutDataRows),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await reader.Value.WaitAsync(cancellationToken);
    }

    private SourceAssetProviderPool EnsureProviderPool() =>
        _providerPool ??= new SourceAssetProviderPool(_profile);

    private async ValueTask ReleaseSourceAsync(string sourceId)
    {
        _rawMetadataReaders.TryRemove(sourceId, out _);
        if (_providerPool is not null) await _providerPool.ReleaseAsync(sourceId);
        if (sourceId.Equals("vanilla", StringComparison.OrdinalIgnoreCase)) return;
        _rawMetadataReaders.TryRemove("vanilla", out _);
        if (_providerPool is not null) await _providerPool.ReleaseAsync("vanilla");
    }

    private static string FormatBytes(long bytes)
    {
        var gigabytes = bytes / 1024d / 1024d / 1024d;
        return gigabytes >= 1 ? $"{gigabytes:0.0} GB" : $"{bytes / 1024d / 1024d:0} MB";
    }

    private void ShowLayers(IEnumerable<LayerDescriptor> descriptors)
    {
        var selectedIds = Layers.Where(layer => layer.IsSelected)
            .Select(layer => SelectionId(layer.Descriptor))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _suppressSelectionNotifications = true;
        try
        {
            Layers.Clear();
            foreach (var descriptor in descriptors.OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase))
            {
                var layer = new LayerRowViewModel(descriptor, NotifySelectionChanged)
                {
                    IsSelected = selectedIds.Contains(SelectionId(descriptor))
                };
                Layers.Add(layer);
            }
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }

        RebuildFilterOptions();
        NormalizeFilters();
        var enabledMods = ContentSources.Count(source => source.IsMod && source.IsEnabled);
        StatusMessage = Layers.Count == 0
            ? "No cached layer catalog. Scan enabled content to begin."
            : $"Vanilla + {enabledMods} mods · {Layers.Count} layers";
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasLayers));
        OnPropertyChanged(nameof(HasNoLayers));
        NotifySelectionChanged();
    }

    private void NormalizeFilters()
    {
        if (!MapOptions.Contains(_selectedMap)) _selectedMap = AllMaps;
        if (!GameModeOptions.Contains(_selectedGameMode)) _selectedGameMode = AllGameModes;
        if (!SourceOptions.Contains(_selectedSource)) _selectedSource = AllSources;
        OnPropertyChanged(nameof(SelectedMap));
        OnPropertyChanged(nameof(SelectedGameMode));
        OnPropertyChanged(nameof(SelectedSource));
        RefreshFilter();
    }

    private void UpdateCacheStates()
    {
        if (ContentSources.Count == 0) return;
        var vanilla = ContentSources.First(source => !source.IsMod).Source;
        foreach (var row in ContentSources)
        {
            var key = _cache.BuildSourceKey(row.Source, vanilla, _mappingsSignature);
            row.UpdateCache(_cache.GetState(row.Source, key));
        }
    }

    public void ClearCache(ContentSourceSettingsViewModel source)
    {
        _cache.Clear(source.Id);
        ShowLayers(Layers.Select(layer => layer.Descriptor)
            .Where(layer => !layer.Source.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray());
        UpdateCacheStates();
        StatusMessage = $"Cleared {source.Name} cache. Rebuild it to restore its layers.";
    }

    private async Task RebuildCacheAsync(ContentSourceSettingsViewModel source)
    {
        if (IsSettingsDirty)
        {
            ReportError("Save or discard settings before rebuilding a cache.");
            return;
        }

        _cache.Invalidate(source.Id);
        UpdateCacheStates();
        if (!source.IsEnabled) return;

        await RunOperationAsync($"Rebuilding {source.Name} cache...", true, async cancellationToken =>
        {
            DisposeProvider();
            PrepareCacheContext(_profile);
            var currentSource = CurrentSources().First(item =>
                item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            LayerDescriptor[] rebuilt;
            try
            {
                var provider = await EnsureProviderPool().GetAsync(currentSource.Id, cancellationToken);
                var scanned = await new LayerCatalogService(provider, currentSource.Id).ScanAsync(cancellationToken);
                rebuilt = scanned.Where(layer => currentSource.IsVanilla
                        ? layer.Source.IsVanilla
                        : layer.Source.Id.Equals(currentSource.Id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                _cache.SaveCatalog(currentSource, _sourceKeys[currentSource.Id], rebuilt);
            }
            finally
            {
                await ReleaseSourceAsync(currentSource.Id);
            }
            ConfigureMetadataReader();
            ShowLayers(Layers.Select(layer => layer.Descriptor)
                .Where(layer => !layer.Source.Id.Equals(currentSource.Id, StringComparison.OrdinalIgnoreCase))
                .Concat(rebuilt));
            UpdateCacheStates();
            StatusMessage = $"Rebuilt {source.Name}: {rebuilt.Length} layers.";
        });
    }

    private void DisposeProvider()
    {
        _rawMetadataReaders.Clear();
        _providerPool?.Dispose();
        _providerPool = null;
    }

    private async Task RunOperationAsync(
        string initialText,
        bool indeterminate,
        Func<CancellationToken, Task> operation)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        IsIndeterminate = indeterminate;
        BusyText = initialText;
        ProgressValue = 0;
        ProgressMaximum = 1;

        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            BusyText = string.Empty;
        }
    }

    private bool FilterLayer(object item)
    {
        if (item is not LayerRowViewModel layer) return false;
        if (SelectedMap != AllMaps && !layer.MapId.Equals(SelectedMap, StringComparison.OrdinalIgnoreCase))
            return false;
        if (SelectedGameMode != AllGameModes &&
            !layer.GameMode.Equals(SelectedGameMode, StringComparison.OrdinalIgnoreCase))
            return false;
        if (SelectedSource != AllSources &&
            !layer.SourceName.Equals(SelectedSource, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return layer.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || layer.MapId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || layer.GameMode.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || layer.SourceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilter()
    {
        LayersView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(SelectAllVisible));
    }

    private void ResetFilters()
    {
        _searchText = string.Empty;
        _selectedMap = AllMaps;
        _selectedGameMode = AllGameModes;
        _selectedSource = AllSources;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedMap));
        OnPropertyChanged(nameof(SelectedGameMode));
        OnPropertyChanged(nameof(SelectedSource));
        RefreshFilter();
    }

    private void RebuildFilterOptions()
    {
        ReplaceOptions(MapOptions, AllMaps, Layers.Select(layer => layer.MapId));
        ReplaceOptions(GameModeOptions, AllGameModes, Layers.Select(layer => layer.GameMode));
        ReplaceOptions(SourceOptions, AllSources, Layers.Select(layer => layer.SourceName));
    }

    private void NotifySelectionChanged()
    {
        if (_suppressSelectionNotifications) return;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectAllVisible));
        (ExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private List<LayerRowViewModel> VisibleRows() => LayersView.Cast<LayerRowViewModel>().ToList();

    private static string SelectionId(LayerSelectionPresetItem item) =>
        $"{item.SourceId}\0{item.GameplayPackagePath}\0{item.GameplayObjectName}";

    private static string SelectionId(LayerDescriptor layer) =>
        $"{layer.Source.Id}\0{layer.GameplayPackagePath}\0{layer.GameplayObjectName}";

    private void RaiseCommandStates()
    {
        foreach (var command in new[]
                 {
                     OpenSettingsCommand, CloseSettingsCommand, RefreshModsCommand, SaveSettingsCommand,
                     RefreshCommand, ExportCommand, CancelCommand
                 })
            switch (command)
            {
                case RelayCommand relay: relay.RaiseCanExecuteChanged(); break;
                case AsyncRelayCommand asyncRelay: asyncRelay.RaiseCanExecuteChanged(); break;
            }
    }

    private void ReportError(string message)
    {
        StatusMessage = message;
        ErrorOccurred?.Invoke(this, message);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void UpdateContentLayout()
    {
        _contentLayoutKind = ContentLayoutDetector.Detect(SquadPath).Kind;
        OnPropertyChanged(nameof(IsEditorSdk));
        OnPropertyChanged(nameof(UsesWorkshop));
        OnPropertyChanged(nameof(ContentModeLabel));
        OnPropertyChanged(nameof(MappingsLabel));
        OnPropertyChanged(nameof(AddonSectionTitle));
        OnPropertyChanged(nameof(AddonSectionSubtitle));
    }

    private static void ReplaceOptions(
        ObservableCollection<string> target,
        string allOption,
        IEnumerable<string> values)
    {
        target.Clear();
        target.Add(allOption);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
            target.Add(value);
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        DisposeProvider();
    }
}
