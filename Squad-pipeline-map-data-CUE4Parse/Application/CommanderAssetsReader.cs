using System.Collections.Concurrent;
using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class CommanderAssetsReader
{
    private readonly UnrealPropertyReader _properties;
    private readonly DataTableRowResolver _rows;
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<AvailableAction>>> _unitActions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>>>> _worldTeamActions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>>> _teamActions =
        new(StringComparer.OrdinalIgnoreCase);

    public CommanderAssetsReader(IGameAssetProvider assets)
    {
        _properties = new UnrealPropertyReader(assets);
        _rows = new DataTableRowResolver(_properties);
    }

    public IReadOnlyList<UnitCommanderAsset> Read(
        string unitObjectPath,
        string factionId,
        string biome,
        LayerReadContext context)
    {
        var normalizedBiome = VehicleTicketRulesReader.EnumMember(biome) ?? biome;
        var unitActions = _unitActions.GetOrAdd(
            $"{unitObjectPath}|{normalizedBiome}",
            _ => new Lazy<IReadOnlyList<AvailableAction>>(
                () => ReadUnitActions(
                    _properties.ResolveObject(unitObjectPath)
                    ?? throw new InvalidDataException($"Unable to load unit '{unitObjectPath}'."),
                    normalizedBiome),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        var teamActions = ReadTeamActions(context).GetValueOrDefault(factionId) ?? [];
        if (unitActions.Count == 0) return teamActions.Select(action => action.Asset).ToArray();

        var result = new List<UnitCommanderAsset>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var available in unitActions)
        {
            var canonical = FindCanonicalCommand(available.ActionActorHierarchy, teamActions);
            if (canonical is not null)
            {
                if (added.Add($"command:{canonical.CommandDataPath}"))
                    result.Add(canonical.Asset);
                continue;
            }

            if (added.Add($"setting:{available.SettingPath}"))
                result.Add(available.Fallback);
        }

        foreach (var command in teamActions)
        {
            if (result.Count >= teamActions.Count) break;
            if (added.Add($"command:{command.CommandDataPath}"))
                result.Add(command.Asset);
        }
        return result;
    }

    private IReadOnlyList<AvailableAction> ReadUnitActions(UObject unit, string biome)
    {
        var result = new List<AvailableAction>();
        foreach (var reference in _properties.ArrayInherited(unit, "Actions"))
        {
            var availability = _properties.ResolveObject(reference);
            var setting = _properties.ObjectInherited(availability, "Setting");
            var data = _rows.Resolve(setting);
            if (setting is null || data is null) continue;

            var displayName = UnrealPropertyReader.ToStringValue(
                _properties.RawStartingWith(data.Row, "DisplayName"));
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            var icon = _properties.ResolveObject(_properties.RawStartingWith(data.Row, "Icon"))?.Name
                       ?? string.Empty;
            var delay = _properties.ObjectInherited(availability, "Delay");
            result.Add(new AvailableAction(
                setting.GetPathName(),
                ClassHierarchy(SelectActionActor(setting, biome)),
                new UnitCommanderAsset(ReadMinutes(delay, "InitialDelay"), displayName, icon)));
        }
        return result;
    }

    private UObject? SelectActionActor(UObject setting, string biome)
    {
        var versions = _properties.ArrayInherited(setting, "ActionVersions")
            .Select(UnrealPropertyReader.Unwrap)
            .OfType<IPropertyHolder>()
            .ToArray();
        var selected = versions.FirstOrDefault(version =>
            string.Equals(
                VehicleTicketRulesReader.EnumMember(UnrealPropertyReader.ToStringValue(
                    _properties.RawStartingWith(version, "Biome_"))),
                biome,
                StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();
        return selected is null
            ? null
            : _properties.ResolveObject(_properties.RawStartingWith(selected, "ActionActor_"));
    }

    private IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>> ReadTeamActions(LayerReadContext context)
    {
        return _worldTeamActions.GetOrAdd(
            context.World.GetPathName(),
            _ => new Lazy<IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>>>(
                () =>
                {
                    var table = ResolveTeamCommands(context.WorldSettings);
                    if (table is null) return EmptyTeamActions;
                    lock (_teamActions)
                    {
                        if (_teamActions.TryGetValue(table.GetPathName(), out var cached)) return cached;
                        return _teamActions[table.GetPathName()] = BuildTeamActions(table);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>> BuildTeamActions(UDataTable table)
    {
        var result = new Dictionary<string, List<TeamCommand>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.RowMap.Values)
        {
            var command = _properties.Object(row, "CommandData");
            if (command is null) continue;
            var displayName = _properties.StringInherited(command, string.Empty, "DisplayName");
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            var icon = _properties.ObjectInherited(command, "Texture", "Icon")?.Name ?? string.Empty;
            var delay = (int)Math.Round(
                _properties.DoubleInherited(command, 0, "CooldownDuration") / 60d,
                MidpointRounding.AwayFromZero);
            var action = new TeamCommand(
                command.GetPathName(),
                ClassHierarchy(_properties.ObjectInherited(command, "CommandActor")),
                new UnitCommanderAsset(delay, displayName, icon));

            foreach (var team in _properties.Array(row, "Team")
                         .Select(UnrealPropertyReader.ToStringValue)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!result.TryGetValue(team!, out var actions))
                    result[team!] = actions = [];
                actions.Add(action);
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TeamCommand>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool ActorClassesMatch(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) => left.Count > 0 && right.Count > 0
        && (left.Contains(right[0], StringComparer.OrdinalIgnoreCase)
            || right.Contains(left[0], StringComparer.OrdinalIgnoreCase));

    private static TeamCommand? FindCanonicalCommand(
        IReadOnlyList<string> actionActorHierarchy,
        IReadOnlyList<TeamCommand> commands)
    {
        if (actionActorHierarchy.Count == 0) return null;
        var direct = commands.Where(command =>
            ActorClassesMatch(actionActorHierarchy, command.CommandActorHierarchy)).ToArray();
        if (direct.Length == 1) return direct[0];
        if (direct.Length > 1) return null;

        // Old ActionSettings and current TeamCommands can point at sibling faction variants.
        // Their most specific shared Blueprint actor base is the serialized game type identity;
        // only a unique best match is accepted.
        var ranked = commands
            .Select(command => (Command: command, Score: SharedBlueprintAncestorScore(
                actionActorHierarchy, command.CommandActorHierarchy)))
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        return ranked.Length > 0
               && (ranked.Length == 1 || ranked[0].Score > ranked[1].Score)
            ? ranked[0].Command
            : null;
    }

    private static int SharedBlueprintAncestorScore(
        IReadOnlyList<string> leftHierarchy,
        IReadOnlyList<string> rightHierarchy)
    {
        if (rightHierarchy.Count == 0) return -1;
        var rightPaths = rightHierarchy
            .Select((path, index) => (path, index))
            .ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase);
        var best = -1;
        foreach (var (path, leftIndex) in leftHierarchy.Select((path, index) => (path, index)))
        {
            if (!path.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)
                || !rightPaths.TryGetValue(path, out var rightIndex)) continue;
            best = Math.Max(best, 10_000 - leftIndex - rightIndex);
        }
        return best;
    }

    private static IReadOnlyList<string> ClassHierarchy(UObject? source)
    {
        var result = new List<string>();
        for (var current = source as UClass; current is not null;)
        {
            result.Add(current.GetPathName());
            current = current.SuperStruct?.TryLoad<UClass>(out var parent) == true ? parent : null;
        }
        return result;
    }

    private UDataTable? ResolveTeamCommands(UObject? worldSettings)
    {
        var gameMode = _properties.ObjectInherited(worldSettings, "DefaultGameMode");
        var teamClass = _properties.ObjectInherited(gameMode, "TeamClass");
        var commanderManager = _properties.ObjectInherited(teamClass, "CommanderManager");
        return _properties.ObjectInherited(commanderManager, "TeamCommands") as UDataTable;
    }

    private int ReadMinutes(UObject? restriction, string propertyName) =>
        _properties.RawInherited(restriction, propertyName) is FDateTime dateTime
            ? (int)Math.Round(TimeSpan.FromTicks(dateTime.Ticks).TotalMinutes, MidpointRounding.AwayFromZero)
            : 0;

    private sealed record AvailableAction(
        string SettingPath,
        IReadOnlyList<string> ActionActorHierarchy,
        UnitCommanderAsset Fallback);

    private sealed record TeamCommand(
        string CommandDataPath,
        IReadOnlyList<string> CommandActorHierarchy,
        UnitCommanderAsset Asset);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TeamCommand>> EmptyTeamActions =
        new Dictionary<string, IReadOnlyList<TeamCommand>>(StringComparer.OrdinalIgnoreCase);
}
