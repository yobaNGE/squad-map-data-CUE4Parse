using System.Collections.Concurrent;
using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class UnitVehicleReader
{
    private readonly IGameAssetProvider _assets;
    private readonly UnrealPropertyReader _properties;
    private readonly DataTableRowResolver _rows;
    private readonly UnrealEnumDisplayNameIndex _vehicleTypes;
    private readonly UnrealEnumDisplayNameIndex _vehicleTags;
    private readonly UnrealEnumDisplayNameIndex _spawnerSizes;
    private readonly ConcurrentDictionary<string, Lazy<VehicleSettingsTemplate>> _settings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<VehicleClassFacts>> _vehicleClasses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<UnitVehicleTemplate>>> _unitVehicles =
        new(StringComparer.OrdinalIgnoreCase);

    public UnitVehicleReader(IGameAssetProvider assets, UnrealPropertyReader properties)
    {
        _assets = assets;
        _properties = properties;
        _rows = new DataTableRowResolver(properties);
        _vehicleTypes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Vehicle/ESQVehicle.ESQVehicle");
        _vehicleTags = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Vehicle/ESQVehicleTag.ESQVehicleTag");
        _spawnerSizes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Spawners/Vehicle/ESQVehicleSpawnerSize.ESQVehicleSpawnerSize");
    }

    public IReadOnlyList<ResolvedUnitVehicle> Read(UObject unit, string biome) =>
        Read(unit, biome, VehicleTicketRules.Empty, string.Empty);

    public IReadOnlyList<ResolvedUnitVehicle> Read(
        string unitObjectPath,
        string biome,
        VehicleTicketRules ticketRules,
        string killerTeam) => Resolve(
        GetUnitVehicles(
            unitObjectPath,
            biome,
            () => _assets.LoadObject(unitObjectPath)
                  ?? throw new InvalidDataException($"Unable to load unit '{unitObjectPath}'.")),
        ticketRules,
        killerTeam);

    public IReadOnlyList<ResolvedUnitVehicle> Read(
        UObject unit,
        string biome,
        VehicleTicketRules ticketRules,
        string killerTeam) => Resolve(
        GetUnitVehicles(unit.GetPathName(), biome, () => unit),
        ticketRules,
        killerTeam);

    private IReadOnlyList<UnitVehicleTemplate> GetUnitVehicles(
        string unitObjectPath,
        string biome,
        Func<UObject> loadUnit)
    {
        var normalizedBiome = VehicleTicketRulesReader.EnumMember(biome) ?? biome;
        return _unitVehicles.GetOrAdd(
            $"{unitObjectPath}|{normalizedBiome}",
            _ => new Lazy<IReadOnlyList<UnitVehicleTemplate>>(
                () => ReadTemplates(loadUnit(), normalizedBiome),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private IReadOnlyList<UnitVehicleTemplate> ReadTemplates(UObject unit, string biome)
    {
        var result = new List<UnitVehicleTemplate>();
        foreach (var reference in _properties.ArrayInherited(unit, "Vehicles"))
        {
            var availability = _properties.ResolveObject(reference);
            if (availability is null) continue;
            var settings = _properties.ObjectInherited(availability, "Setting");
            if (settings is null) continue;

            var template = GetSettings(settings);
            var vehiclePath = template.SelectVehicle(biome);
            var vehicleClass = _assets.LoadObject(vehiclePath)
                               ?? throw new InvalidDataException($"Unable to load vehicle '{vehiclePath}'.");
            var classFacts = GetVehicleClassFacts(vehicleClass);
            var delay = _properties.ObjectInherited(availability, "Delay");
            var count = _properties.ObjectInherited(availability, "LimitedCount");

            result.Add(new UnitVehicleTemplate(
                template.DisplayName,
                classFacts.RawType,
                template.Icon,
                _properties.IntInherited(count, 0, "BaseAvailability"),
                ReadMinutes(delay, "InitialDelay"),
                ReadMinutes(delay, "Delay"),
                _properties.BoolInherited(count, false, "IsUniqueUsage"),
                template.VehicleType,
                template.SpawnerSize,
                classFacts.PassengerSeats,
                classFacts.DriverSeats,
                template.Tags,
                template.Tags.Contains("Watercraft", StringComparer.OrdinalIgnoreCase),
                template.Tags.Contains("ATGM", StringComparer.OrdinalIgnoreCase),
                template.RawVehicleType,
                template.RawTags));
        }
        return result;
    }

    private static IReadOnlyList<ResolvedUnitVehicle> Resolve(
        IReadOnlyList<UnitVehicleTemplate> templates,
        VehicleTicketRules ticketRules,
        string killerTeam) => templates.Select(template => new ResolvedUnitVehicle(
            template.Type,
            template.RawType,
            template.Icon,
            template.Count,
            template.Delay,
            template.RespawnTime,
            template.SingleUse,
            template.VehicleType,
            template.SpawnerSize,
            template.PassengerSeats,
            template.DriverSeats,
            template.VehicleTags,
            template.IsAmphibious,
            ticketRules.Resolve(killerTeam, template.RawVehicleType, template.RawTags),
            template.Atgm)).ToArray();

    private VehicleSettingsTemplate GetSettings(UObject settings) => _settings.GetOrAdd(
        settings.GetPathName(),
        _ => new Lazy<VehicleSettingsTemplate>(
            () => ReadSettings(settings),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private VehicleSettingsTemplate ReadSettings(UObject settings)
    {
        var data = _rows.Resolve(settings)
                   ?? throw new InvalidDataException($"Vehicle settings '{settings.GetPathName()}' has no Data row.");
        var rawTags = _properties.ArrayInherited(settings, "VehicleTags")
            .Select(UnrealPropertyReader.ToStringValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var tags = rawTags.Select(_vehicleTags.Resolve).ToArray();
        var rawVehicleType = _properties.StringInherited(settings, _vehicleTypes.DefaultValue, "VehicleType");
        var versions = _properties.ArrayInherited(settings, "VehicleVersions")
            .Select(UnrealPropertyReader.Unwrap)
            .OfType<IPropertyHolder>()
            .Select(ReadVersion)
            .ToArray();

        if (versions.Length == 0)
            throw new InvalidDataException($"Vehicle settings '{settings.GetPathName()}' has no VehicleVersions.");

        return new VehicleSettingsTemplate(
            RowValue(data.Row, "DisplayName"),
            _properties.ResolveObject(_properties.RawStartingWith(data.Row, "Icon"))?.Name ?? string.Empty,
            rawVehicleType,
            rawTags.Select(VehicleTicketRulesReader.EnumMember)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            _vehicleTypes.Resolve(rawVehicleType),
            _spawnerSizes.Resolve(_properties.StringInherited(settings, string.Empty, "SpawnerSize")),
            tags,
            versions);
    }

    private VehicleVersion ReadVersion(IPropertyHolder version)
    {
        var biome = UnrealPropertyReader.ToStringValue(_properties.RawStartingWith(version, "Biome_"))
                    ?? string.Empty;
        var vehicle = _properties.ResolveObject(_properties.RawStartingWith(version, "Vehicle_"))
                      ?? throw new InvalidDataException("VehicleVersions entry has no loadable Vehicle class.");
        return new VehicleVersion(VehicleTicketRulesReader.EnumMember(biome) ?? biome, vehicle.GetPathName());
    }

    private VehicleClassFacts GetVehicleClassFacts(UObject vehicleClass) => _vehicleClasses.GetOrAdd(
        vehicleClass.GetPathName(),
        _ => new Lazy<VehicleClassFacts>(
            () => ReadVehicleClassFacts(vehicleClass),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private VehicleClassFacts ReadVehicleClassFacts(UObject vehicleClass) => new(
        vehicleClass.Name,
        _properties.ArrayInherited(vehicleClass, "AdditionalSeatsConfig").Count,
        _properties.RawInherited(vehicleClass, "DriverSeatConfig") is null ? 0 : 1);

    private int ReadMinutes(UObject? restriction, string propertyName) =>
        UnrealPropertyReader.Unwrap(_properties.RawInherited(restriction, propertyName)) is FDateTime value
            ? checked((int)TimeSpan.FromTicks(value.Ticks).TotalMinutes)
            : 0;

    private string RowValue(IPropertyHolder row, string prefix) =>
        UnrealPropertyReader.ToStringValue(_properties.RawStartingWith(row, prefix)) ?? string.Empty;

    private sealed record VehicleSettingsTemplate(
        string DisplayName,
        string Icon,
        string RawVehicleType,
        IReadOnlySet<string> RawTags,
        string VehicleType,
        string SpawnerSize,
        IReadOnlyList<string> Tags,
        IReadOnlyList<VehicleVersion> Versions)
    {
        public string SelectVehicle(string biome)
        {
            var normalizedBiome = VehicleTicketRulesReader.EnumMember(biome) ?? biome;
            return Versions.FirstOrDefault(version =>
                       version.Biome.Equals(normalizedBiome, StringComparison.OrdinalIgnoreCase))?.VehicleObjectPath
                   ?? Versions[0].VehicleObjectPath;
        }
    }

    private sealed record VehicleVersion(string Biome, string VehicleObjectPath);
    private sealed record VehicleClassFacts(string RawType, int PassengerSeats, int DriverSeats);

    private sealed record UnitVehicleTemplate(
        string Type,
        string RawType,
        string Icon,
        int Count,
        int Delay,
        int RespawnTime,
        bool SingleUse,
        string VehicleType,
        string SpawnerSize,
        int PassengerSeats,
        int DriverSeats,
        IReadOnlyList<string> VehicleTags,
        bool IsAmphibious,
        bool Atgm,
        string RawVehicleType,
        IReadOnlySet<string> RawTags);
}

internal sealed record ResolvedUnitVehicle(
    string Type,
    string RawType,
    string Icon,
    int Count,
    int Delay,
    int RespawnTime,
    bool SingleUse,
    string VehicleType,
    string SpawnerSize,
    int PassengerSeats,
    int DriverSeats,
    IReadOnlyList<string> VehicleTags,
    bool IsAmphibious,
    int TicketValue,
    bool Atgm);
