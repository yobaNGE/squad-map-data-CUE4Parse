using System.Globalization;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LayerAssetsReader
{
    private const string VehicleSpawnerType = "BP_VehicleSpawner_C";
    private const string DeployableSpawnerType = "BP_SQDeployableSpawner_C";
    private const string HelipadType = "BP_helicopter_repair_pad_C";

    private readonly UnrealPropertyReader _properties;
    private readonly UnrealEnumDisplayNameIndex _vehicleTypes;
    private readonly UnrealEnumDisplayNameIndex _vehicleTags;
    private readonly UnrealEnumDisplayNameIndex _vehicleSizes;
    private readonly UnrealEnumDisplayNameIndex _deployableTypes;

    public LayerAssetsReader(IGameAssetProvider assets)
    {
        _properties = new UnrealPropertyReader(assets);
        _vehicleTypes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Vehicle/ESQVehicle.ESQVehicle");
        _vehicleTags = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Vehicle/ESQVehicleTag.ESQVehicleTag");
        _vehicleSizes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Spawners/Vehicle/ESQVehicleSpawnerSize.ESQVehicleSpawnerSize");
        _deployableTypes = new UnrealEnumDisplayNameIndex(
            assets, "/Game/Settings/Deployables/ESQDeployable.ESQDeployable");
    }

    public LayerAssets Read(LayerReadContext context)
    {
        var exports = context.Exports;
        var transforms = context.Transforms;
        var vehicleSpawners = new List<VehicleSpawner>();
        var deployables = new List<Deployable>();
        var helipads = new List<Helipad>();
        var assetKinds = new Dictionary<string, AssetKind>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in exports)
        {
            if (!assetKinds.TryGetValue(actor.ExportType, out var kind))
            {
                kind = Classify(actor);
                assetKinds[actor.ExportType] = kind;
            }

            switch (kind)
            {
                case AssetKind.VehicleSpawner:
                    vehicleSpawners.Add(ReadVehicleSpawner(actor, transforms));
                    break;
                case AssetKind.Deployable:
                    deployables.Add(ReadDeployable(actor, transforms));
                    break;
                case AssetKind.Helipad:
                    helipads.Add(ReadHelipad(actor, transforms));
                    break;
            }
        }

        return new LayerAssets(vehicleSpawners, deployables, helipads);
    }

    private VehicleSpawner ReadVehicleSpawner(UObject actor, SceneTransformResolver transforms)
    {
        var settings = _properties.ObjectInherited(actor, "Settings");
        var transform = transforms.ResolveActor(actor);
        var row = ReadSettingsRow(settings);

        return new VehicleSpawner(
            ReadMapIcon(row, "questionmark"),
            actor.Name,
            ReadTeam(actor, "Team"),
            _vehicleSizes.Resolve(_properties.StringInherited(settings, string.Empty, "Size")),
            _properties.IntInherited(actor, 0, "MaxNum"),
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            transform.Rotation.Roll,
            transform.Rotation.Pitch,
            transform.Rotation.Yaw,
            ReadPriorities(settings, _vehicleTypes, "Types Priorities"),
            ReadPriorities(settings, _vehicleTags, "Tags Priorities"),
            _properties.ArrayInherited(settings, "AuthorizedVehicleTypes")
                .Select(UnrealPropertyReader.ToStringValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => _vehicleTypes.Resolve(value!))
                .ToArray());
    }

    private Deployable ReadDeployable(UObject actor, SceneTransformResolver transforms)
    {
        var settings = _properties.ObjectInherited(actor, "Settings");
        var transform = transforms.ResolveActor(actor);
        var row = ReadSettingsRow(settings);
        var displayName = row is null ? null : LevelDisplayNameIndex.ReadDisplayName(row);
        var type = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : _deployableTypes.Resolve(_properties.StringInherited(settings, string.Empty, "Type"));

        return new Deployable(
            type,
            ReadMapIcon(row, "questionmark"),
            ReadTeam(actor, "Team"),
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            transform.Rotation.Roll,
            transform.Rotation.Pitch,
            transform.Rotation.Yaw);
    }

    private Helipad ReadHelipad(UObject actor, SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        return new Helipad(
            actor.Name,
            "deployable_helipad",
            ReadTeam(actor, "InitialTeam"),
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            transform.Rotation.Roll,
            transform.Rotation.Pitch,
            transform.Rotation.Yaw);
    }

    private IReadOnlyList<AssetPriority> ReadPriorities(
        UObject? settings,
        UnrealEnumDisplayNameIndex names,
        string propertyName) =>
        _properties.MapInherited(settings, propertyName)
            .Select(entry => new AssetPriority(
                names.Resolve(UnrealPropertyReader.ToStringValue(entry.Key) ?? string.Empty),
                Convert.ToString(UnrealPropertyReader.Unwrap(entry.Value), CultureInfo.InvariantCulture) ?? string.Empty))
            .ToArray();

    private IPropertyHolder? ReadSettingsRow(UObject? settings)
    {
        string? rowName = null;
        UDataTable? table = null;
        foreach (var source in _properties.InheritanceChain(settings))
        {
            var data = _properties.Struct(source, "Data");
            rowName ??= _properties.String(data, string.Empty, "RowName") is { Length: > 0 } name ? name : null;
            table ??= _properties.Object(data, "DataTable") as UDataTable;
        }
        return table is not null && rowName is not null &&
               table.TryGetDataTableRow(rowName, StringComparison.OrdinalIgnoreCase, out var row)
            ? row
            : null;
    }

    private string ReadMapIcon(IPropertyHolder? row, string defaultIcon)
    {
        var property = row?.Properties.FirstOrDefault(candidate =>
            candidate.Name.Text.StartsWith("MapIcon", StringComparison.OrdinalIgnoreCase));
        return _properties.ResolveObject(property?.Tag?.GenericValue)?.Name ?? defaultIcon;
    }

    private string ReadTeam(UObject actor, string propertyName)
    {
        var rawTeam = _properties.StringInherited(actor, string.Empty, propertyName);
        var separator = rawTeam.LastIndexOf("::", StringComparison.Ordinal);
        var name = separator >= 0 ? rawTeam[(separator + 2)..] : rawTeam;
        return string.IsNullOrWhiteSpace(name) ? "Neutral" : name.Replace('_', ' ');
    }

    private static bool IsType(UObject actor, string typeName)
    {
        if (actor.ExportType.Equals(typeName, StringComparison.OrdinalIgnoreCase)) return true;

        var current = actor.Class;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && visited.Add(current.GetPathName()))
        {
            if (current.Name.Text.Equals(typeName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!current.TryLoad(out UObject? classObject)) break;
            current = classObject.Super;
        }
        return false;
    }

    private static AssetKind Classify(UObject actor)
    {
        if (IsType(actor, VehicleSpawnerType)) return AssetKind.VehicleSpawner;
        if (IsType(actor, DeployableSpawnerType)) return AssetKind.Deployable;
        if (IsType(actor, HelipadType)) return AssetKind.Helipad;
        return AssetKind.None;
    }

    private enum AssetKind
    {
        None,
        VehicleSpawner,
        Deployable,
        Helipad
    }
}
