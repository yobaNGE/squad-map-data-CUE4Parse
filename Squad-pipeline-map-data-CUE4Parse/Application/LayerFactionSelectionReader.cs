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

        return new LayerFactionSelections(separated, team1, team2);
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
