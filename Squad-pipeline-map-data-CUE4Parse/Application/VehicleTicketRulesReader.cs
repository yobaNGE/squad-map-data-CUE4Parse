using System.Collections;
using System.Collections.Concurrent;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class VehicleTicketRulesReader
{
    private readonly UnrealPropertyReader _properties;
    private readonly UnrealEnumDisplayNameIndex _vehicleTypes;
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<VehicleTicketRuleDefinition>>> _tables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<VehicleTicketRules>> _worlds =
        new(StringComparer.OrdinalIgnoreCase);

    public VehicleTicketRulesReader(IGameAssetProvider assets, UnrealPropertyReader properties)
    {
        _properties = properties;
        _vehicleTypes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Vehicle/ESQVehicle.ESQVehicle");
    }

    public VehicleTicketRules Read(LayerReadContext context) => _worlds.GetOrAdd(
        context.World.GetPathName(),
        _ => new Lazy<VehicleTicketRules>(
            () => ReadWorld(context.Exports),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private VehicleTicketRules ReadWorld(IReadOnlyList<UObject> exports)
    {
        var worldSettings = exports.FirstOrDefault(export =>
            _properties.RawInherited(export, "DefaultGameMode") is not null
            || _properties.RawInherited(export, "MapRulesets") is not null);
        if (worldSettings is null) return VehicleTicketRules.Empty;

        var ruleSets = new List<UObject>();
        var gameMode = _properties.ObjectInherited(worldSettings, "DefaultGameMode");
        AddRuleSets(ruleSets, _properties.ArrayInherited(gameMode, "RuleSetClasses"));
        AddRuleSets(ruleSets, _properties.ArrayInherited(worldSettings, "MapRulesets"));

        var activeRules = new List<ActiveVehicleTicketRule>();
        foreach (var ruleSet in ruleSets)
        {
            if (!_properties.BoolInherited(ruleSet, true, "bRulesetEnabled")) continue;

            foreach (var value in _properties.ArrayInherited(ruleSet, "Rules"))
            {
                if (UnrealPropertyReader.Unwrap(value) is not IPropertyHolder rule) continue;
                var relationships = ReadEnumValues(_properties.RawStartingWith(rule, "CoveredRelationships_"));
                if (!relationships.Contains("Enemy")) continue;

                var teams = ReadEnumValues(_properties.RawStartingWith(rule, "TargetTeam_"));
                var table = _properties.ResolveObject(_properties.RawStartingWith(rule, "RuleList_")) as UDataTable;
                if (table is null) continue;

                activeRules.Add(new ActiveVehicleTicketRule(teams, GetTableRules(table)));
            }
        }

        return new VehicleTicketRules(activeRules);
    }

    private void AddRuleSets(ICollection<UObject> destination, IEnumerable<object?> references)
    {
        foreach (var reference in references)
            if (_properties.ResolveObject(reference) is { } ruleSet)
                destination.Add(ruleSet);
    }

    private IReadOnlyList<VehicleTicketRuleDefinition> GetTableRules(UDataTable table) => _tables.GetOrAdd(
        table.GetPathName(),
        _ => new Lazy<IReadOnlyList<VehicleTicketRuleDefinition>>(
            () => ReadTable(table),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private IReadOnlyList<VehicleTicketRuleDefinition> ReadTable(UDataTable table)
    {
        var result = new List<VehicleTicketRuleDefinition>(table.RowMap.Count);
        foreach (var row in table.RowMap.Values)
        {
            var vehicleType = EnumMember(UnrealPropertyReader.ToStringValue(
                                  _properties.RawStartingWith(row, "VehicleType_"))
                              ?? _vehicleTypes.DefaultValue)!;

            result.Add(new VehicleTicketRuleDefinition(
                vehicleType,
                ReadEnumValues(_properties.RawStartingWith(row, "VehicleTag_")),
                UnrealPropertyReader.ToInt(_properties.RawStartingWith(row, "OwnerTicketLoss_")) ?? 0));
        }
        return result;
    }

    private static HashSet<string> ReadEnumValues(object? value)
    {
        value = UnrealPropertyReader.Unwrap(value);
        var values = value is IEnumerable sequence and not string
            ? sequence.Cast<object?>()
            : new[] { value };
        return values
            .Select(UnrealPropertyReader.ToStringValue)
            .Select(EnumMember)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string? EnumMember(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? value : value[(separator + 2)..];
    }
}

internal sealed class VehicleTicketRules(IReadOnlyList<ActiveVehicleTicketRule> activeRules)
{
    public static VehicleTicketRules Empty { get; } = new([]);

    public int Resolve(string killerTeam, string vehicleType, IReadOnlySet<string> vehicleTags)
    {
        var team = VehicleTicketRulesReader.EnumMember(killerTeam) ?? string.Empty;
        var type = VehicleTicketRulesReader.EnumMember(vehicleType) ?? string.Empty;
        var tickets = 0;

        foreach (var activeRule in activeRules)
        {
            if (!activeRule.TargetTeams.Contains(team)) continue;
            foreach (var rule in activeRule.Rules)
            {
                if (!rule.VehicleType.Equals(type, StringComparison.OrdinalIgnoreCase)) continue;
                if (!rule.RequiredTags.All(vehicleTags.Contains)) continue;
                tickets = checked(tickets + rule.OwnerTicketLoss);
            }
        }

        return tickets;
    }
}

internal sealed record ActiveVehicleTicketRule(
    IReadOnlySet<string> TargetTeams,
    IReadOnlyList<VehicleTicketRuleDefinition> Rules);

internal sealed record VehicleTicketRuleDefinition(
    string VehicleType,
    IReadOnlySet<string> RequiredTags,
    int OwnerTicketLoss);
