using System.Collections.Concurrent;
using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class FactionDefinitionIndex
{
    private readonly IGameAssetProvider _assets;
    private readonly UnrealPropertyReader _properties;
    private readonly DataTableRowResolver _rows;
    private readonly ConcurrentDictionary<string, Lazy<FactionDefinition>> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    public FactionDefinitionIndex(IGameAssetProvider assets, UnrealPropertyReader properties)
    {
        _assets = assets;
        _properties = properties;
        _rows = new DataTableRowResolver(properties);
    }

    public FactionDefinition Resolve(string factionId) => _definitions.GetOrAdd(
        factionId,
        id => new Lazy<FactionDefinition>(
            () => Read(id),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private FactionDefinition Read(string factionId)
    {
        var faction = _assets.LoadPrimaryAsset("BP_SQFaction_C", factionId)
                      ?? throw new MissingFactionPrimaryAssetException(factionId);
        var data = _rows.Resolve(faction)
                   ?? throw new InvalidDataException($"Faction primary asset '{factionId}' has no resolvable Data row.");
        return new FactionDefinition(ValueStartingWith(data.Row, "DisplayName"));
    }

    private string ValueStartingWith(IPropertyHolder row, string prefix) =>
        UnrealPropertyReader.ToStringValue(_properties.RawStartingWith(row, prefix)) ?? string.Empty;
}

internal sealed record FactionDefinition(string DisplayName);

internal sealed class MissingFactionPrimaryAssetException(string factionId) :
    Exception($"Faction primary asset '{factionId}' was not found.");
