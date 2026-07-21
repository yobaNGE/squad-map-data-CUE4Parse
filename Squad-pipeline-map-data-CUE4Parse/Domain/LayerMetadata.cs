using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record LayerMetadata(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("rawName")] string RawName,
    [property: JsonPropertyName("mapId")] string MapId,
    [property: JsonPropertyName("mapName")] string MapName,
    [property: JsonPropertyName("gamemode")] string Gamemode,
    [property: JsonPropertyName("layerVersion")] string LayerVersion,
    [property: JsonPropertyName("seaLevel")] int SeaLevel,
    [property: JsonPropertyName("mapCameraActor")] MapCameraActor MapCameraActor,
    [property: JsonPropertyName("border")] IReadOnlyList<BorderPoint> Border,
    [property: JsonPropertyName("mapSize")] string MapSize,
    [property: JsonPropertyName("mapTextureCorners")] IReadOnlyList<MapTextureCorner> MapTextureCorners,
    [property: JsonPropertyName("assets")] LayerAssets Assets,
    [property: JsonPropertyName("capturePoints")] CapturePoints CapturePoints,
    [property: JsonPropertyName("objectives")] IReadOnlyDictionary<string, LayerObjective> Objectives,
    [property: JsonPropertyName("mapAssets")] MapAssets MapAssets,
    [property: JsonPropertyName("teamConfigs")] TeamConfigs TeamConfigs,
    [property: JsonPropertyName("helicoptersAvailable")] bool HelicoptersAvailable,
    [property: JsonPropertyName("boatsAvailable")] bool BoatsAvailable,
    [property: JsonPropertyName("tanksAvailable")] bool TanksAvailable,
    [property: JsonPropertyName("commanderDisabled")] bool CommanderDisabled,
    [property: JsonPropertyName("team1boats")] bool Team1Boats,
    [property: JsonPropertyName("team2boats")] bool Team2Boats,
    [property: JsonPropertyName("team1heli")] bool Team1Helicopters,
    [property: JsonPropertyName("team2heli")] bool Team2Helicopters,
    [property: JsonPropertyName("units")] Units Units);

public sealed record MapCameraActor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ);

public sealed record BorderPoint(
    [property: JsonPropertyName("point")] int Point,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ);

public sealed record MapTextureCorner(
    [property: JsonPropertyName("point")] int Point,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ);
