using System.Collections.Concurrent;
using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LevelBiomeResolver
{
    private readonly IGameAssetProvider _assets;
    private readonly UnrealPropertyReader _properties;
    private readonly UnrealEnumDisplayNameIndex _biomeNames;
    private readonly ConcurrentDictionary<string, Lazy<string>> _biomes =
        new(StringComparer.OrdinalIgnoreCase);

    public LevelBiomeResolver(IGameAssetProvider assets, UnrealPropertyReader properties)
    {
        _assets = assets;
        _properties = properties;
        _biomeNames = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Maps/ESQBiome.ESQBiome");
    }

    public string Resolve(UObject layer)
    {
        var levelId = _properties.StringInherited(layer, string.Empty, "LevelId");
        var level = _properties.ObjectInherited(layer, "OuterLevel")
                    ?? _assets.LoadPrimaryAsset("BP_SQLevel_C", levelId);
        return level is null ? string.Empty : ResolveLevel(level);
    }

    public string ResolveLevel(UObject level) => _biomes.GetOrAdd(
        level.GetPathName(),
        _ => new Lazy<string>(
            // Unreal omits properties equal to the enum's zero value from cooked assets.
            () => _properties.StringInherited(level, _biomeNames.DefaultValue, "Biome"),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
