using System.Collections.Concurrent;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class CommanderAssetsReader
{
    private readonly UnrealPropertyReader _properties;
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>>>>
        _worldActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>>> _tableActions =
        new(StringComparer.OrdinalIgnoreCase);

    public CommanderAssetsReader(IGameAssetProvider assets)
    {
        _properties = new UnrealPropertyReader(assets);
    }

    public IReadOnlyList<UnitCommanderAsset> Read(string factionId, LayerReadContext context) =>
        ReadWorldActions(context).GetValueOrDefault(factionId) ?? [];

    private IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>> ReadWorldActions(
        LayerReadContext context)
    {
        return _worldActions.GetOrAdd(
            context.World.GetPathName(),
            _ => new Lazy<IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>>>(
                () =>
                {
                    var table = ResolveTeamCommands(context.WorldSettings);
                    if (table is null) return EmptyActions;
                    lock (_tableActions)
                    {
                        if (_tableActions.TryGetValue(table.GetPathName(), out var cached)) return cached;
                        return _tableActions[table.GetPathName()] = BuildActions(table);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>> BuildActions(UDataTable table)
    {
        var result = new Dictionary<string, List<UnitCommanderAsset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.RowMap.Values)
        {
            var command = _properties.Object(row, "CommandData");
            if (command is null) continue;
            var displayName = _properties.StringInherited(command, string.Empty, "DisplayName");
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            var action = new UnitCommanderAsset(
                (int)Math.Round(
                    _properties.DoubleInherited(command, 0, "CooldownDuration") / 60d,
                    MidpointRounding.AwayFromZero),
                displayName,
                _properties.ObjectInherited(command, "Texture", "Icon")?.Name ?? string.Empty);

            foreach (var factionId in _properties.Array(row, "Team")
                         .Select(UnrealPropertyReader.ToStringValue)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!result.TryGetValue(factionId!, out var actions))
                    result[factionId!] = actions = [];
                actions.Add(action);
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<UnitCommanderAsset>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private UDataTable? ResolveTeamCommands(UObject? worldSettings)
    {
        var gameMode = _properties.ObjectInherited(worldSettings, "DefaultGameMode");
        var teamClass = _properties.ObjectInherited(gameMode, "TeamClass");
        var commanderManager = _properties.ObjectInherited(teamClass, "CommanderManager");
        return _properties.ObjectInherited(commanderManager, "TeamCommands") as UDataTable;
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<UnitCommanderAsset>> EmptyActions =
        new Dictionary<string, IReadOnlyList<UnitCommanderAsset>>(StringComparer.OrdinalIgnoreCase);
}
