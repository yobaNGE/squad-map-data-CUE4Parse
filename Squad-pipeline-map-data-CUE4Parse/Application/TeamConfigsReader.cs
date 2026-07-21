using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class TeamConfigsReader
{
    private readonly UnrealPropertyReader _properties;
    private readonly UnrealEnumDisplayNameIndex _alliances;
    private readonly UnrealEnumDisplayNameIndex _factionSetupTypes;

    public TeamConfigsReader(IGameAssetProvider assets)
    {
        _properties = new UnrealPropertyReader(assets);
        _alliances = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Factions/SQEAlliance.SQEAlliance");
        _factionSetupTypes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/FactionSetups/ESQFactionSetupType.ESQFactionSetupType");
    }

    public TeamConfigs Read(UObject layer, LayerFactionSelections selections)
    {
        var factions = ToTeamFactions(selections);
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

        return new TeamConfigs(
            ReadTeamConfig(team1Object, 1, factions.Team1Units),
            ReadTeamConfig(team2Object, 2, factions.Team2Units),
            factions);
    }

    private TeamConfig ReadTeamConfig(UObject? config, int index, IReadOnlyList<FactionConfig> factions)
    {
        var defaultFactionUnit = ReadAssetName(_properties.RawInherited(config, "SpecificFactionSetup"));
        if (string.IsNullOrWhiteSpace(defaultFactionUnit))
            defaultFactionUnit = factions.FirstOrDefault()?.DefaultUnit ?? string.Empty;

        return new TeamConfig(
            index,
            defaultFactionUnit,
            _properties.IntInherited(config, 0, "Tickets"),
            _properties.BoolInherited(config, false,
                "DisableVehicleDuringStaggingPhase", "VehiclesDisabled", "bVehiclesDisabled", "DisableVehicles"),
            _properties.IntInherited(config, 50,
                "PlayerPercent", "PlayerPercentOverride", "PlayerCountPercent"),
            ReadEnumArray(config, _alliances, "Allowed Alliances"),
            ReadEnumArray(config, _factionSetupTypes, "AllowedFactionSetupTypes"),
            ReadTags(config));
    }

    private static TeamFactions ToTeamFactions(LayerFactionSelections selections) => new(
        selections.SeparatedFactionsList,
        selections.Team1.Select(ToFactionConfig).ToArray(),
        selections.Team2.Select(ToFactionConfig).ToArray());

    private static FactionConfig ToFactionConfig(LayerFactionSelection selection) => new(
        selection.FactionId,
        selection.DefaultUnit.ObjectName,
        selection.TypedUnits
            .Where(type => !type.Unit.ObjectPath.Equals(
                selection.DefaultUnit.ObjectPath, StringComparison.OrdinalIgnoreCase))
            .Select(type => new FactionType(type.Type, type.Unit.ObjectName))
            .ToArray());

    private IReadOnlyList<string> ReadEnumArray(
        UObject? config,
        UnrealEnumDisplayNameIndex names,
        string propertyName) => _properties.ArrayInherited(config, propertyName)
        .Select(UnrealPropertyReader.ToStringValue)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => names.Resolve(value!))
        .ToArray();

    private IReadOnlyList<string> ReadTags(UObject? config) => _properties.ArrayInherited(config, "RequiredTags")
        .Select(value => UnrealPropertyReader.Unwrap(value) is IPropertyHolder tag
            ? _properties.String(tag, string.Empty, "TagName")
            : UnrealPropertyReader.ToStringValue(value) ?? string.Empty)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(TextFormatting.Prettify)
        .ToArray();

    private static int ReadTeamIndex(string value)
    {
        var token = TextFormatting.EnumToken(value).Replace("_", string.Empty);
        if (token.Equals("TeamOne", StringComparison.OrdinalIgnoreCase)) return 1;
        if (token.Equals("TeamTwo", StringComparison.OrdinalIgnoreCase)) return 2;
        return int.TryParse(token, out var index) ? index : -1;
    }

    private static string ReadAssetName(object? value) =>
        TextFormatting.AssetName(UnrealPropertyReader.ToStringValue(value));

}
