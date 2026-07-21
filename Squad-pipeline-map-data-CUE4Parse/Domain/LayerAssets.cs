using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record LayerAssets(
    [property: JsonPropertyName("vehicleSpawners")] IReadOnlyList<VehicleSpawner> VehicleSpawners,
    [property: JsonPropertyName("deployables")] IReadOnlyList<Deployable> Deployables,
    [property: JsonPropertyName("helipads")] IReadOnlyList<Helipad> Helipads);

public sealed record VehicleSpawner(
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("maxNum")] int MaxNum,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ,
    [property: JsonPropertyName("typePriorities")] IReadOnlyList<AssetPriority> TypePriorities,
    [property: JsonPropertyName("tagPriorities")] IReadOnlyList<AssetPriority> TagPriorities,
    [property: JsonPropertyName("authorizedVehicleTypes")] IReadOnlyList<string> AuthorizedVehicleTypes);

public sealed record AssetPriority(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string Icon);

public sealed record Deployable(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ);

public sealed record Helipad(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ);
