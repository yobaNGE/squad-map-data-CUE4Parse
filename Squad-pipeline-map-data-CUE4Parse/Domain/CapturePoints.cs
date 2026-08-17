using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record CapturePoints(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("lanes")] CaptureLanes Lanes,
    [property: JsonPropertyName("points")] CapturePointGraph Points,
    [property: JsonPropertyName("clusters")] CaptureClusterGraph Clusters,
    [property: JsonPropertyName("hexs")] CaptureHexs Hexs,
    [property: JsonPropertyName("objectiveSpawnLocations")] IReadOnlyList<CaptureObjectiveSpawnLocation> ObjectiveSpawnLocations,
    [property: JsonPropertyName("destructionObject")] CaptureDestructionObject DestructionObject)
{
    public static CapturePoints Empty(string type = "Unknown") => new(
        type,
        new CaptureLanes(),
        new CapturePointGraph(),
        new CaptureClusterGraph(),
        new CaptureHexs(),
        [],
        new CaptureDestructionObject());
}

public sealed record CaptureLink(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("nodeA")] string NodeA,
    [property: JsonPropertyName("nodeB")] string NodeB);

public sealed record CapturePointGraph(
    [property: JsonPropertyName("pointsOrder"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? PointsOrder = null,
    [property: JsonPropertyName("numberOfPoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? NumberOfPoints = null,
    [property: JsonPropertyName("listOfMains"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ListOfMains = null,
    [property: JsonPropertyName("links"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CaptureLink>? Links = null,
    [property: JsonPropertyName("objectives"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CaptureObjective>? Objectives = null);

public sealed record CaptureClusterGraph(
    [property: JsonPropertyName("links"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CaptureLink>? Links = null,
    [property: JsonPropertyName("pointsOrder"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? PointsOrder = null,
    [property: JsonPropertyName("numberOfPoints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? NumberOfPoints = null,
    [property: JsonPropertyName("listOfMains"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ListOfMains = null);

public sealed record CaptureLanes(
    [property: JsonPropertyName("links"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CaptureLink>? Links = null,
    [property: JsonPropertyName("listOfLanes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ListOfLanes = null,
    [property: JsonPropertyName("laneObjects"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, CaptureLane>? LaneObjects = null);

public sealed record CaptureLane(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("laneLinks")] IReadOnlyList<CaptureLink> LaneLinks,
    [property: JsonPropertyName("pointsOrder")] IReadOnlyList<string> PointsOrder,
    [property: JsonPropertyName("numberOfPoints")] int NumberOfPoints,
    [property: JsonPropertyName("listOfMains")] IReadOnlyList<string> ListOfMains);

public sealed record CaptureHexs(
    [property: JsonPropertyName("startOwnership"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? StartOwnership = null,
    [property: JsonPropertyName("endOwnership"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? EndOwnership = null,
    [property: JsonPropertyName("startRandomAnchorDist:"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? StartRandomAnchorDistance = null,
    [property: JsonPropertyName("endRandomAnchorDist:"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? EndRandomAnchorDistance = null,
    [property: JsonPropertyName("team1Anchors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? Team1Anchors = null,
    [property: JsonPropertyName("team2Anchors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? Team2Anchors = null,
    [property: JsonPropertyName("hexs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CaptureHex>? Hexs = null);

public sealed record CaptureHex(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hexNum")] int HexNumber,
    [property: JsonPropertyName("initialTeam")] string InitialTeam,
    [property: JsonPropertyName("flagName")] string FlagName,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("sphereRadius")] double SphereRadius,
    [property: JsonPropertyName("boxExtent")] CaptureHexExtent BoxExtent);

public sealed record CaptureHexExtent(
    [property: JsonPropertyName("location_x")] double X,
    [property: JsonPropertyName("location_y")] double Y,
    [property: JsonPropertyName("location_z")] double Z);

public sealed record CaptureObjective(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("objectDisplayName")] string ObjectDisplayName,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("objects")] IReadOnlyList<ObjectiveVolume> Objects,
    [property: JsonPropertyName("pointPosition")] int PointPosition);

public sealed record CaptureObjectiveSpawnLocation(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ);

public sealed record CaptureDestructionObject(
    [property: JsonPropertyName("attackingTeam"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AttackingTeam = null,
    [property: JsonPropertyName("delayBetweenPhases"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DelayBetweenPhases = null,
    [property: JsonPropertyName("objectiveClass"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ObjectiveClass = null,
    [property: JsonPropertyName("roundTimerIncrease"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RoundTimerIncrease = null,
    [property: JsonPropertyName("timerIncreasePerPhaseActive"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? TimerIncreasePerPhaseActive = null,
    [property: JsonPropertyName("phases"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CaptureDestructionPhase>? Phases = null,
    [property: JsonPropertyName("noDeployZones"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CaptureDestructionNoDeployZone>? NoDeployZones = null);

public sealed record CaptureDestructionPhase(
    [property: JsonPropertyName("PhaseNumber")] int PhaseNumber,
    [property: JsonPropertyName("phaseObjectives")] IReadOnlyList<CaptureDestructionObjective> PhaseObjectives);

public sealed record CaptureDestructionObjective(
    [property: JsonPropertyName("numberOfSpots")] int NumberOfSpots,
    [property: JsonPropertyName("minDistanceBetweenSpots")] int MinDistanceBetweenSpots,
    [property: JsonPropertyName("numberOfCaches"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NumberOfCaches,
    [property: JsonPropertyName("splinePoints")] IReadOnlyList<CaptureDestructionSplinePoint> SplinePoints);

public sealed record CaptureDestructionSplinePoint(
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ);

public sealed record CaptureDestructionNoDeployZone(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("objects")] IReadOnlyList<CaptureDestructionNoDeployVolume> Objects);

public sealed record CaptureDestructionNoDeployVolume(
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ,
    [property: JsonPropertyName("sphereRadius")] string SphereRadius,
    [property: JsonPropertyName("boxExtent")] CaptureDestructionExtent BoxExtent);

public sealed record CaptureDestructionExtent(
    [property: JsonPropertyName("location_x")] double LocationX,
    [property: JsonPropertyName("location_y")] double LocationY,
    [property: JsonPropertyName("location_z")] double LocationZ);
