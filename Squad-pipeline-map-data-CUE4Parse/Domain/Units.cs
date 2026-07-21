using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record Units(
    [property: JsonPropertyName("team1Units")] IReadOnlyList<Unit> Team1Units,
    [property: JsonPropertyName("team2Units")] IReadOnlyList<Unit> Team2Units);

public sealed record Unit(
    [property: JsonPropertyName("unitObjectName")] string UnitObjectName,
    [property: JsonPropertyName("unitIcon")] string UnitIcon,
    [property: JsonPropertyName("factionID")] string FactionId,
    [property: JsonPropertyName("shortName")] string ShortName,
    [property: JsonPropertyName("factionName")] string FactionName,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("unitBadge")] string UnitBadge,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("useCommanderActionNearVehicle")] bool UseCommanderActionNearVehicle,
    [property: JsonPropertyName("hasBuddyRally")] bool HasBuddyRally,
    [property: JsonPropertyName("characteristics")] IReadOnlyList<string> Characteristics,
    [property: JsonPropertyName("vehicles")] IReadOnlyList<UnitVehicle> Vehicles,
    [property: JsonPropertyName("commanderAssets")] IReadOnlyList<UnitCommanderAsset> CommanderAssets);

public sealed record UnitVehicle(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("rawType")] string RawType,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("delay")] int Delay,
    [property: JsonPropertyName("respawnTime")] int RespawnTime,
    [property: JsonPropertyName("singleUse")] bool SingleUse,
    [property: JsonPropertyName("vehType")] string VehicleType,
    [property: JsonPropertyName("spawnerSize")] string SpawnerSize,
    [property: JsonPropertyName("passengerSeats")] int PassengerSeats,
    [property: JsonPropertyName("driverSeats")] int DriverSeats,
    [property: JsonPropertyName("vehTags")] IReadOnlyList<string> VehicleTags,
    [property: JsonPropertyName("isAmphibious")] bool IsAmphibious,
    [property: JsonPropertyName("ticketValue")] int TicketValue,
    [property: JsonPropertyName("ATGM")] bool Atgm);

public sealed record UnitCommanderAsset(
    [property: JsonPropertyName("delay")] int Delay,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("icon")] string Icon);
