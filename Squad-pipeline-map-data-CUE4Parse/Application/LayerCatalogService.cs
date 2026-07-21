using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.UObject;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

public sealed record LayerDescriptor(
    string Name,
    string GameplayPackagePath,
    string GameplayObjectName,
    string WorldObjectPath,
    string Version,
    string MapId,
    string GameMode,
    ContentSource Source)
{
    public string Id => $"{GameplayPackagePath}:{GameplayObjectName}";
}

public interface ILayerCatalogService
{
    Task<IReadOnlyList<LayerDescriptor>> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed partial class LayerCatalogService : ILayerCatalogService
{
    private readonly IGameAssetProvider _assets;
    private readonly string? _sourceId;
    private readonly UnrealPropertyReader _properties;

    public LayerCatalogService(IGameAssetProvider assets, string? sourceId = null)
    {
        _assets = assets;
        _sourceId = sourceId;
        _properties = new UnrealPropertyReader(assets);
    }

    public Task<IReadOnlyList<LayerDescriptor>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<LayerDescriptor>>(() =>
        {
            var result = new Dictionary<string, LayerDescriptor>(StringComparer.OrdinalIgnoreCase);

            foreach (var primaryAsset in _assets.GetPrimaryAssets("BP_SQLayer_C"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var packagePath = _assets.ResolvePackagePath(primaryAsset.ObjectPath);
                    if (packagePath is null || !IsExpectedSource(packagePath)) continue;
                    var export = _assets.LoadObject(primaryAsset.ObjectPath);
                    Add(export, packagePath);
                }
                catch
                {
                    // A stale registry record is not a playable layer.
                }
            }

            var fallbackPackages = _assets.PackagePaths
                .Where(IsGameplayDataPackage)
                .Where(IsExpectedSource)
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (var packagePath in fallbackPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<UObject> exports;
                try { exports = _assets.LoadPackageExports(packagePath); }
                catch { continue; }

                foreach (var export in exports.Where(IsLayerExport))
                    Add(export, packagePath);
            }

            return result.Values.OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            void Add(UObject? export, string? packagePath)
            {
                if (export is null || packagePath is null || !IsLayerExport(export)) return;

                var data = _properties.Struct(export, "Data");
                var rowName = _properties.String(data, string.Empty, "RowName");
                var layerTable = _properties.Object(data, "DataTable") as UDataTable;
                if (layerTable is null
                    || string.IsNullOrWhiteSpace(rowName)
                    || rowName.Equals("None", StringComparison.OrdinalIgnoreCase)
                    || !layerTable.TryGetDataTableRow(rowName, StringComparison.OrdinalIgnoreCase, out _))
                    return;

                var worldPath = ReadFirstWorldPath(export);
                if (string.IsNullOrWhiteSpace(worldPath)) return;

                var gameMode = UnrealPropertyReader.Unwrap(_properties.RawInherited(export, "GameMode"))
                    as IPropertyHolder;
                var descriptor = new LayerDescriptor(
                    rowName,
                    packagePath,
                    export.Name,
                    worldPath,
                    ExtractVersion(rowName),
                    _properties.StringInherited(export, string.Empty, "LevelId"),
                    _properties.String(gameMode, string.Empty, "RowName"),
                    _assets.GetContentSource(packagePath));
                result[descriptor.Id] = descriptor;
            }
        }, cancellationToken);

    private bool IsExpectedSource(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(_sourceId)) return true;
        var source = _assets.GetContentSource(packagePath);
        return _sourceId.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
            ? source.IsVanilla
            : source.Id.Equals(_sourceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGameplayDataPackage(string path) =>
        path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
        && (path.Contains("/Gameplay_Layer_Data/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Gameplay_Data/Layer/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/GameplayData/Layer/", StringComparison.OrdinalIgnoreCase));

    private bool IsLayerExport(UObject export) =>
        export.ExportType.Equals("BP_SQLayer_C", StringComparison.OrdinalIgnoreCase)
        || export.ExportType.EndsWith("SQLayer_C", StringComparison.OrdinalIgnoreCase)
        || _properties.Raw(export, "Worlds") is not null;

    private string ReadFirstWorldPath(UObject layer)
    {
        foreach (var item in _properties.Array(layer, "Worlds"))
        {
            var text = item switch
            {
                FSoftObjectPath path => path.AssetPathName.Text,
                _ => UnrealPropertyReader.ToStringValue(item)
            };
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    private static string ExtractVersion(string name)
    {
        var matches = VersionRegex().Matches(name);
        return matches.Count == 0 ? string.Empty : matches[^1].Value.ToLowerInvariant();
    }

    [GeneratedRegex(@"v\d+(?:[._-]?\d+)*", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();
}
