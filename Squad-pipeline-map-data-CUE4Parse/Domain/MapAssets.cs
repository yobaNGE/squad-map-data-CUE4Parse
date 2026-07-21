using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record MapAssets(
    [property: JsonPropertyName("protectionZones")] IReadOnlyList<ProtectionZone> ProtectionZones,
    [property: JsonPropertyName("spawnGroups")] IReadOnlyList<SpawnGroup> SpawnGroups,
    [property: JsonPropertyName("spawnPoints")] IReadOnlyList<SpawnPoint> SpawnPoints);

public sealed record ProtectionZone(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("deployableLockDistance")] double DeployableLockDistance,
    [property: JsonPropertyName("teamid")] string TeamId,
    [property: JsonPropertyName("objects")] IReadOnlyList<MapAssetVolume> Objects);

public sealed record SpawnGroup(
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("initialLifeSpan")] int InitialLifeSpan,
    [property: JsonPropertyName("spawningEnabled")] bool SpawningEnabled,
    [property: JsonPropertyName("displayName")] string DisplayName);

public sealed record SpawnPoint(
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("initialLifeSpan")] int InitialLifeSpan,
    [property: JsonPropertyName("spawningEnabled")] bool SpawningEnabled,
    [property: JsonPropertyName("spawnGroup")] string SpawnGroup);

public sealed record MapAssetVolume(
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("isSphere")] bool IsSphere,
    [property: JsonPropertyName("sphereRadius")] double SphereRadius,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("isBox")] bool IsBox,
    [property: JsonPropertyName("boxExtent")] MapAssetExtent BoxExtent,
    [property: JsonPropertyName("isCapsule")] bool IsCapsule);

public sealed record MapAssetExtent(
    [property: JsonPropertyName("extent_x")] double ExtentX,
    [property: JsonPropertyName("extent_y")] double ExtentY,
    [property: JsonPropertyName("extent_z")] double ExtentZ,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ);
