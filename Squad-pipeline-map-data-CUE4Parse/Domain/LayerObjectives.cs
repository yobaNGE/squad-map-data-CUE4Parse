using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

[JsonPolymorphic]
[JsonDerivedType(typeof(ObjectiveActor))]
[JsonDerivedType(typeof(ObjectiveCluster))]
public abstract record LayerObjective;

public sealed record ObjectiveActor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("objectDisplayName")] string ObjectDisplayName,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("objects")] IReadOnlyList<ObjectiveVolume> Objects,
    [property: JsonPropertyName("pointPosition")] int? PointPosition) : LayerObjective;

public sealed record ObjectiveCluster(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pointPosition")] int PointPosition,
    [property: JsonPropertyName("avgLocation")] ObjectiveLocation AverageLocation,
    [property: JsonPropertyName("points")] IReadOnlyList<ObjectivePoint> Points) : LayerObjective;

public sealed record ObjectivePoint(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("objectDisplayName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ObjectDisplayName,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("objects")] IReadOnlyList<ObjectiveVolume> Objects);

public sealed record ObjectiveLocation(
    [property: JsonPropertyName("location_x")] double X,
    [property: JsonPropertyName("location_y")] double Y,
    [property: JsonPropertyName("location_z")] double Z);

public sealed record ObjectiveVolume(
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("isSphere")] bool IsSphere,
    [property: JsonPropertyName("sphereRadius")] string SphereRadius,
    [property: JsonPropertyName("isBox")] bool IsBox,
    [property: JsonPropertyName("boxExtent")] ObjectiveExtent BoxExtent,
    [property: JsonPropertyName("isCapsule")] bool IsCapsule,
    [property: JsonPropertyName("capsuleRadius"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CapsuleRadius = null,
    [property: JsonPropertyName("capsuleLength"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CapsuleLength = null,
    [property: JsonPropertyName("rotation_x"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? RotationX = null,
    [property: JsonPropertyName("rotation_y"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? RotationY = null,
    [property: JsonPropertyName("rotation_z"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? RotationZ = null);

public sealed record ObjectiveExtent(
    [property: JsonPropertyName("extent_x")] double X,
    [property: JsonPropertyName("extent_y")] double Y,
    [property: JsonPropertyName("extent_z")] double Z,
    [property: JsonPropertyName("rotation_x")] double RotationX,
    [property: JsonPropertyName("rotation_y")] double RotationY,
    [property: JsonPropertyName("rotation_z")] double RotationZ,
    [property: JsonPropertyName("scaling_x"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? ScaleX = null,
    [property: JsonPropertyName("scaling_y"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? ScaleY = null,
    [property: JsonPropertyName("scaling_z"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? ScaleZ = null);
