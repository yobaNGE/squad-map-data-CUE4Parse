using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LayerFactionSelectionReader
{
    private readonly UnrealPropertyReader _properties;

    public LayerFactionSelectionReader(IGameAssetProvider assets)
    {
        _properties = new UnrealPropertyReader(assets);
    }

    public LayerFactionSelections Read(UObject layer)
    {
        var separated = _properties.BoolInherited(layer, false, "bSeparatedFactionsList");
        var common = new Lazy<IReadOnlyList<LayerFactionSelection>>(
            () => ReadFactionList(_properties.MapInherited(layer, "FactionsList")));
        var hasTeam1List = _properties.Raw(layer, "FactionsListTeamOne") is not null;
        var hasTeam2List = _properties.Raw(layer, "FactionsListTeamTwo") is not null;
        var team1 = hasTeam1List
            ? ReadFactionList(_properties.Map(layer, "FactionsListTeamOne"))
            : common.Value;
        var team2 = hasTeam2List
            ? ReadFactionList(_properties.Map(layer, "FactionsListTeamTwo"))
            : separated ? [] : common.Value;

        // TeamConfigs[].SpecificFactionSetup names an extra guaranteed-available faction per team
        // that isn't necessarily part of FactionsList (seen on Seed layers in particular) — it adds
        // to the pool rather than replacing it, so append it when it's not already listed.
        var (team1Config, team2Config) = ResolveTeamConfigs(layer);
        team1 = AddSpecificFactionSetup(team1, team1Config);
        team2 = AddSpecificFactionSetup(team2, team2Config);

        return new LayerFactionSelections(separated, team1, team2);
    }

    private IReadOnlyList<LayerFactionSelection> AddSpecificFactionSetup(
        IReadOnlyList<LayerFactionSelection> factions,
        UObject? config)
    {
        var specific = ReadSpecificFactionSetup(config);
        if (specific.Count == 0) return factions;
        var extra = specific[0];
        return factions.Any(faction => faction.FactionId.Equals(extra.FactionId, StringComparison.OrdinalIgnoreCase))
            ? factions
            : [..factions, extra];
    }

    // Mirrors TeamConfigsReader's own resolution: a config is matched to a team by explicit
    // Index first, but a team's config commonly omits Index entirely (implicitly "the other
    // one"), so whichever config wasn't claimed by the other team fills the remaining slot.
    private (UObject? Team1, UObject? Team2) ResolveTeamConfigs(UObject layer)
    {
        var configObjects = _properties.Array(layer, "TeamConfigs")
            .Select(_properties.ResolveObject)
            .Where(config => config is not null)
            .Cast<UObject>()
            .ToArray();
        UObject? team1Object = null;
        UObject? team2Object = null;

        foreach (var config in configObjects)
        {
            var index = ReadTeamIndex(_properties.StringInherited(config, string.Empty, "Index"));
            if (index == 1 && team1Object is null) team1Object = config;
            else if (index == 2 && team2Object is null) team2Object = config;
        }
        team1Object ??= configObjects.FirstOrDefault(config => !ReferenceEquals(config, team2Object));
        team2Object ??= configObjects.FirstOrDefault(config => !ReferenceEquals(config, team1Object));

        return (team1Object, team2Object);
    }

    private IReadOnlyList<LayerFactionSelection> ReadSpecificFactionSetup(UObject? config)
    {
        var setup = _properties.ResolveObject(_properties.RawInherited(config, "SpecificFactionSetup"));
        if (setup is null) return [];

        var factionId = _properties.StringInherited(setup, string.Empty, "FactionId");
        if (string.IsNullOrWhiteSpace(factionId)) return [];

        return [new LayerFactionSelection(factionId, new LayerUnitReference(setup.GetPathName(), setup.Name), [])];
    }

    private static int ReadTeamIndex(string value)
    {
        var token = TextFormatting.EnumToken(value).Replace("_", string.Empty);
        if (token.Equals("TeamOne", StringComparison.OrdinalIgnoreCase)) return 1;
        if (token.Equals("TeamTwo", StringComparison.OrdinalIgnoreCase)) return 2;
        return int.TryParse(token, out var index) ? index : -1;
    }

    private IReadOnlyList<LayerFactionSelection> ReadFactionList(
        IReadOnlyList<KeyValuePair<object?, object?>> entries) => entries
        .Select(ReadFaction)
        .Where(faction => faction is not null)
        .Cast<LayerFactionSelection>()
        .OrderBy(faction => faction.FactionId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private LayerFactionSelection? ReadFaction(KeyValuePair<object?, object?> entry)
    {
        if (UnrealPropertyReader.Unwrap(entry.Value) is not IPropertyHolder value) return null;

        var factionId = UnrealPropertyReader.ToStringValue(entry.Key);
        if (string.IsNullOrWhiteSpace(factionId)) return null;

        var defaultUnit = ReadUnitReference(_properties.Raw(value, "Faction"));
        if (defaultUnit is null) return null;

        var typedUnits = _properties.Map(value, "Types")
            .Select(type => ReadTypedUnit(type.Key, type.Value))
            .Where(type => type is not null)
            .Cast<LayerTypedUnitSelection>()
            .ToArray();

        return new LayerFactionSelection(factionId, defaultUnit, typedUnits);
    }

    private LayerTypedUnitSelection? ReadTypedUnit(object? type, object? reference)
    {
        var typeName = UnrealPropertyReader.ToStringValue(type);
        var unit = ReadUnitReference(reference);
        return string.IsNullOrWhiteSpace(typeName) || unit is null
            ? null
            : new LayerTypedUnitSelection(typeName, unit);
    }

    private LayerUnitReference? ReadUnitReference(object? reference)
    {
        var unit = _properties.ResolveObject(reference);
        return unit is null ? null : new LayerUnitReference(unit.GetPathName(), unit.Name);
    }
}

internal sealed record LayerFactionSelections(
    bool SeparatedFactionsList,
    IReadOnlyList<LayerFactionSelection> Team1,
    IReadOnlyList<LayerFactionSelection> Team2);

internal sealed record LayerFactionSelection(
    string FactionId,
    LayerUnitReference DefaultUnit,
    IReadOnlyList<LayerTypedUnitSelection> TypedUnits);

internal sealed record LayerTypedUnitSelection(
    string Type,
    LayerUnitReference Unit);

internal sealed record LayerUnitReference(
    string ObjectPath,
    string ObjectName);
