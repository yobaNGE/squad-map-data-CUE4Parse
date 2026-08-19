using System.IO;
using System.Diagnostics;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

public interface ILayerMetadataReader
{
    Task<LayerMetadata> ReadAsync(LayerDescriptor layer, CancellationToken cancellationToken = default);
}

public sealed record LayerMetadataReadResult(
    LayerMetadata Metadata,
    IReadOnlyDictionary<string, long> Phases);

public sealed partial class LayerMetadataReader(
    IGameAssetProvider assets,
    bool ignoreMissingFactionPrimaryAssets = false,
    bool skipVehiclesWithoutDataRows = false) : ILayerMetadataReader
{
    private readonly UnrealPropertyReader _properties = new(assets);
    private readonly LevelDisplayNameIndex _levelNames = new(assets);
    private readonly WorldGeometryReader _worldGeometry = new(assets);
    private readonly LayerAssetsReader _layerAssets = new(assets);
    private readonly CapturePointsReader _capturePoints = new(new UnrealPropertyReader(assets));
    private readonly ObjectivesReader _objectives = new(new UnrealPropertyReader(assets));
    private readonly MapAssetsReader _mapAssets = new(new UnrealPropertyReader(assets));
    private readonly LayerFactionSelectionReader _factionSelections = new(assets);
    private readonly TeamConfigsReader _teamConfigs = new(assets);
    private readonly LayerAvailabilityReader _availability = new(new UnrealPropertyReader(assets));
    private readonly UnitsReader _units = new(
        assets,
        ignoreMissingFactionPrimaryAssets,
        skipVehiclesWithoutDataRows);

    public async Task<LayerMetadata> ReadAsync(
        LayerDescriptor descriptor,
        CancellationToken cancellationToken = default) =>
        (await ReadProfiledAsync(descriptor, cancellationToken)).Metadata;

    public Task<LayerMetadataReadResult> ReadProfiledAsync(
        LayerDescriptor descriptor,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(descriptor, cancellationToken), cancellationToken);

    private LayerMetadataReadResult Read(LayerDescriptor descriptor, CancellationToken cancellationToken)
    {
        var profiler = new PhaseProfiler();
        cancellationToken.ThrowIfCancellationRequested();
        var layer = profiler.Read("loadLayer", () => assets.LoadPackageExports(descriptor.GameplayPackagePath).First(export =>
            export.Name.Equals(descriptor.GameplayObjectName, StringComparison.OrdinalIgnoreCase)));

        var rawName = layer.Name;
        var data = _properties.Struct(layer, "Data")
                   ?? throw new InvalidDataException($"Layer '{rawName}' does not have a Data row handle.");
        var layerTable = _properties.Object(data, "DataTable") as UDataTable
                         ?? throw new InvalidDataException($"Layer '{rawName}' does not reference a layer DataTable.");
        var layerRowName = _properties.String(data, string.Empty, "RowName");
        if (!layerTable.TryGetDataTableRow(layerRowName, StringComparison.OrdinalIgnoreCase, out var layerRow))
            throw new InvalidDataException($"Row '{layerRowName}' was not found in '{layerTable.Name}'.");

        var name = LevelDisplayNameIndex.ReadDisplayName(layerRow)
                   ?? throw new InvalidDataException($"Row '{layerRowName}' does not have a DisplayName.");
        var mapId = _properties.String(layer, string.Empty, "LevelId");
        var mapName = _levelNames.GetDisplayName(mapId);
        var gameMode = _properties.Struct(layer, "GameMode")
                       ?? throw new InvalidDataException($"Layer '{rawName}' does not have a GameMode row handle.");
        var gamemode = _properties.String(gameMode, string.Empty, "RowName");
        var world = profiler.Read("world", () => ReadFirstWorld(layer));
        var context = new LayerReadContext(world, _properties);
        var seaLevel = profiler.Read("seaLevel", () => ReadSeaLevel(context));
        var geometry = profiler.Read("geometry", () => _worldGeometry.Read(context));
        var layerAssets = profiler.Read("layerAssets", () => _layerAssets.Read(context));
        var capturePoints = profiler.Read("capturePoints", () => _capturePoints.Read(context, gamemode));
        var objectives = profiler.Read("objectives", () => _objectives.Read(context, gamemode, capturePoints));
        var mapAssets = profiler.Read("mapAssets", () => _mapAssets.Read(context));
        var factionSelections = profiler.Read("factionSelections", () => _factionSelections.Read(layer));
        var teamConfigs = profiler.Read("teamConfigs", () => _teamConfigs.Read(layer, factionSelections));
        var availability = profiler.Read("availability", () => _availability.Read(layer, layerAssets));
        var units = profiler.Read("units", () => _units.Read(factionSelections, layer, context));

        return new LayerMetadataReadResult(new LayerMetadata(
            name,
            layerRowName,
            mapId,
            mapName,
            gamemode,
            descriptor.Version,
            seaLevel,
            geometry.Camera,
            geometry.Border,
            geometry.MapSize,
            geometry.TextureCorners,
            layerAssets,
            capturePoints,
            objectives,
            mapAssets,
            teamConfigs,
            availability.HelicoptersAvailable,
            availability.BoatsAvailable,
            availability.TanksAvailable,
            availability.CommanderDisabled,
            availability.Team1Boats,
            availability.Team2Boats,
            availability.Team1Helicopters,
            availability.Team2Helicopters,
            units), profiler.Phases);
    }

    private UObject ReadFirstWorld(UObject layer)
    {
        foreach (var reference in _properties.Array(layer, "Worlds"))
        {
            if (_properties.ResolveObject(reference) is { } world) return world;
        }
        throw new InvalidDataException($"Layer '{layer.Name}' does not reference a loadable world.");
    }

    private int ReadSeaLevel(LayerReadContext context) =>
        _properties.Int(context.WorldSettings, 0, "SeaLevel");

    private sealed class PhaseProfiler
    {
        private readonly Dictionary<string, long> _phases = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, long> Phases => _phases;

        public T Read<T>(string name, Func<T> read)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return read();
            }
            finally
            {
                _phases[name] = stopwatch.ElapsedMilliseconds;
            }
        }
    }

}
