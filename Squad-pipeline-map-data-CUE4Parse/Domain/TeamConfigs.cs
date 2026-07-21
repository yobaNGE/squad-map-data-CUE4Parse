using System.Text.Json.Serialization;

namespace Squad_pipeline_map_data_CUE4Parse.Domain;

public sealed record TeamConfigs(
    [property: JsonPropertyName("team1")] TeamConfig Team1,
    [property: JsonPropertyName("team2")] TeamConfig Team2,
    [property: JsonPropertyName("factions")] TeamFactions Factions);

public sealed record TeamConfig(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("defaultFactionUnit")] string DefaultFactionUnit,
    [property: JsonPropertyName("tickets")] int Tickets,
    [property: JsonPropertyName("disabledVeh")] bool DisabledVehicles,
    [property: JsonPropertyName("playerPercent")] int PlayerPercent,
    [property: JsonPropertyName("allowedAlliances")] IReadOnlyList<string> AllowedAlliances,
    [property: JsonPropertyName("allowedFactionUnitTypes")] IReadOnlyList<string> AllowedFactionUnitTypes,
    [property: JsonPropertyName("requiredTags")] IReadOnlyList<string> RequiredTags);

public sealed record TeamFactions(
    [property: JsonPropertyName("separatedFactionsList")] bool SeparatedFactionsList,
    [property: JsonPropertyName("team1Units")] IReadOnlyList<FactionConfig> Team1Units,
    [property: JsonPropertyName("team2Units")] IReadOnlyList<FactionConfig> Team2Units);

public sealed record FactionConfig(
    [property: JsonPropertyName("factionID")] string FactionId,
    [property: JsonPropertyName("defaultUnit")] string DefaultUnit,
    [property: JsonPropertyName("types")] IReadOnlyList<FactionType> Types);

public sealed record FactionType(
    [property: JsonPropertyName("unitType")] string UnitType,
    [property: JsonPropertyName("unit")] string Unit);
