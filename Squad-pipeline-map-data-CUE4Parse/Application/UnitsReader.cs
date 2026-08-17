using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class UnitsReader
{
    private readonly IGameAssetProvider _assets;
    private readonly UnrealPropertyReader _properties;
    private readonly DataTableRowResolver _rows;
    private readonly UnitTypeDescriptorIndex _types;
    private readonly FactionDefinitionIndex _factions;
    private readonly UnitVehicleReader _vehicles;
    private readonly LevelBiomeResolver _biomes;
    private readonly VehicleTicketRulesReader _ticketRules;
    private readonly CommanderAssetsReader _commanderAssets;
    private readonly bool _ignoreMissingFactionPrimaryAssets;
    private readonly ConcurrentDictionary<string, Lazy<UnitTemplate>> _templates =
        new(StringComparer.OrdinalIgnoreCase);

    public UnitsReader(IGameAssetProvider assets, bool ignoreMissingFactionPrimaryAssets = false)
    {
        _assets = assets;
        _properties = new UnrealPropertyReader(assets);
        _rows = new DataTableRowResolver(_properties);
        _types = new UnitTypeDescriptorIndex(assets);
        _factions = new FactionDefinitionIndex(assets, _properties);
        _vehicles = new UnitVehicleReader(assets, _properties);
        _biomes = new LevelBiomeResolver(assets, _properties);
        _ticketRules = new VehicleTicketRulesReader(assets, _properties);
        _commanderAssets = new CommanderAssetsReader(assets);
        _ignoreMissingFactionPrimaryAssets = ignoreMissingFactionPrimaryAssets;
    }

    public Units Read(LayerFactionSelections selections, UObject layer, LayerReadContext context)
    {
        var biome = _biomes.Resolve(layer);
        var ticketRules = _ticketRules.Read(context);
        return new Units(
            ReadTeam(selections.Team1, biome, ticketRules, "Team_Two", context),
            ReadTeam(selections.Team2, biome, ticketRules, "Team_One", context));
    }

    private IReadOnlyList<Unit> ReadTeam(
        IReadOnlyList<LayerFactionSelection> factions,
        string biome,
        VehicleTicketRules ticketRules,
        string killerTeam,
        LayerReadContext context)
    {
        var result = new List<Unit>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var faction in factions)
        foreach (var reference in UnitReferences(faction))
        {
            if (!addedPaths.Add(reference.ObjectPath)) continue;
            UnitTemplate template;
            try
            {
                template = GetTemplate(reference);
            }
            catch (MissingFactionPrimaryAssetException) when (_ignoreMissingFactionPrimaryAssets)
            {
                continue;
            }
            var vehicles = _vehicles.Read(template.ObjectPath, biome, ticketRules, killerTeam)
                .Select(ToUnitVehicle)
                .ToArray();
            var commanderAssets = _commanderAssets.Read(template.FactionId, context);
            result.Add(template.ToUnit(vehicles, commanderAssets));
        }

        return result;
    }

    private UnitTemplate GetTemplate(LayerUnitReference reference) =>
        _templates.GetOrAdd(
            reference.ObjectPath,
            _ => new Lazy<UnitTemplate>(
                () => ReadTemplate(reference),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private UnitTemplate ReadTemplate(LayerUnitReference reference)
    {
        var asset = _assets.LoadObject(reference.ObjectPath)
                    ?? throw new InvalidDataException($"Unable to load unit '{reference.ObjectPath}'.");
        var data = _rows.Resolve(asset)
                   ?? throw new InvalidDataException($"Unit '{reference.ObjectPath}' has no resolvable Data row.");
        var row = data.Row;
        var factionId = _properties.StringInherited(asset, string.Empty, "FactionId");
        if (string.IsNullOrWhiteSpace(factionId))
            throw new InvalidDataException($"Unit '{reference.ObjectPath}' has no FactionId.");

        var outerFactionId = RowValue(row, "OuterFactionId");
        var faction = _factions.Resolve(outerFactionId);
        var type = _types.Resolve(_properties.StringInherited(asset, string.Empty, "Type"));
        var icon = _assets.LoadObject(type.IconObjectPath)?.Name
                   ?? throw new InvalidDataException($"Unable to load unit type icon '{type.IconObjectPath}'.");
        var badge = _properties.ResolveObject(_properties.RawStartingWith(row, "UI_UnitBadge"))?.Name
                    ?? string.Empty;

        return new UnitTemplate(
            reference.ObjectPath,
            reference.ObjectName,
            icon,
            factionId,
            RowValue(row, "ShortName"),
            faction.DisplayName,
            RowValue(row, "DisplayName"),
            RowValue(row, "Description"),
            badge,
            type.DisplayName,
            _properties.BoolInherited(asset, false, "CanUseCommanderActionNearVehicle"),
            _properties.BoolInherited(asset, false, "HasBuddyRally"),
            ReadCharacteristics(asset));
    }

    private IReadOnlyList<string> ReadCharacteristics(UObject asset)
    {
        var raw = UnrealPropertyReader.Unwrap(_properties.RawInherited(asset, "Characteristics"));
        var handles = raw switch
        {
            IPropertyHolder holder => [holder],
            IEnumerable sequence when raw is not string => sequence.Cast<object?>()
                .Select(UnrealPropertyReader.Unwrap)
                .OfType<IPropertyHolder>()
                .ToArray(),
            _ => []
        };
        return handles
            .Select(handle => _properties.String(handle, string.Empty, "RowName"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private string RowValue(IPropertyHolder row, string prefix) =>
        UnrealPropertyReader.ToStringValue(_properties.RawStartingWith(row, prefix)) ?? string.Empty;

    private static IEnumerable<LayerUnitReference> UnitReferences(LayerFactionSelection faction)
    {
        yield return faction.DefaultUnit;
        foreach (var typed in faction.TypedUnits) yield return typed.Unit;
    }

    private static UnitVehicle ToUnitVehicle(ResolvedUnitVehicle vehicle) => new(
        vehicle.Type,
        vehicle.RawType,
        vehicle.Icon,
        vehicle.Count,
        vehicle.Delay,
        vehicle.RespawnTime,
        vehicle.SingleUse,
        vehicle.VehicleType,
        vehicle.SpawnerSize,
        vehicle.PassengerSeats,
        vehicle.DriverSeats,
        vehicle.VehicleTags,
        vehicle.IsAmphibious,
        vehicle.TicketValue,
        vehicle.Atgm);

    private sealed record UnitTemplate(
        string ObjectPath,
        string UnitObjectName,
        string UnitIcon,
        string FactionId,
        string ShortName,
        string FactionName,
        string DisplayName,
        string Description,
        string UnitBadge,
        string Type,
        bool UseCommanderActionNearVehicle,
        bool HasBuddyRally,
        IReadOnlyList<string> Characteristics)
    {
        public Unit ToUnit(
            IReadOnlyList<UnitVehicle> vehicles,
            IReadOnlyList<UnitCommanderAsset> commanderAssets) => new(
            UnitObjectName,
            UnitIcon,
            FactionId,
            ShortName,
            FactionName,
            DisplayName,
            Description,
            UnitBadge,
            Type,
            UseCommanderActionNearVehicle,
            HasBuddyRally,
            Characteristics,
            vehicles,
            commanderAssets);
    }
}
