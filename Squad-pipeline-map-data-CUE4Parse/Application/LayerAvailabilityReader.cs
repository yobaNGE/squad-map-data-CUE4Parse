using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LayerAvailabilityReader(UnrealPropertyReader properties)
{
    public LayerAvailability Read(UObject layer, LayerAssets assets)
    {
        var flags = properties.Struct(layer, "GameFlags");
        return new LayerAvailability(
            properties.Bool(flags, true, "bHelicoptersAvailable"),
            properties.Bool(flags, false, "bBoatsAvailable"),
            properties.Bool(flags, true, "bTanksAvailable"),
            properties.Bool(flags, false, "CommanderDisabled"),
            HasSpawner(assets, "Team One", "Boat"),
            HasSpawner(assets, "Team Two", "Boat"),
            HasSpawner(assets, "Team One", "Helicopter"),
            HasSpawner(assets, "Team Two", "Helicopter"));
    }

    private static bool HasSpawner(LayerAssets assets, string team, string size) =>
        assets.VehicleSpawners.Any(spawner =>
            spawner.Type.Equals(team, StringComparison.OrdinalIgnoreCase) &&
            spawner.Size.Equals(size, StringComparison.OrdinalIgnoreCase));
}

internal sealed record LayerAvailability(
    bool HelicoptersAvailable,
    bool BoatsAvailable,
    bool TanksAvailable,
    bool CommanderDisabled,
    bool Team1Boats,
    bool Team2Boats,
    bool Team1Helicopters,
    bool Team2Helicopters);
