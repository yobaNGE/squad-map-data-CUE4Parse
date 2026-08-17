namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal enum ObjectiveLayout
{
    Unknown,
    Invasion,
    Aas,
    Raas,
    Skirmish,
    TerritoryControl,
    Seed,
    Destruction
}

internal static class ObjectiveLayoutResolver
{
    public static ObjectiveLayout Resolve(LayerReadContext context, string gamemode)
    {
        if (context.FindExact("BP_DestructionPhaseDirector_C").Count != 0) return ObjectiveLayout.Destruction;
        if (context.FindExact("TC_HexGraph_C").Count != 0) return ObjectiveLayout.TerritoryControl;
        if (context.FindExact("SQRAASLaneInitializer_C").Count != 0) return ObjectiveLayout.Raas;
        if (context.FindExact("BP_CaptureZoneInvasion_C").Count != 0
            && (context.FindExact("SQGraphRAASInitializerComponent").Count != 0
                || context.FindExact("SQGraphAASInitializerComponent").Count != 0))
            return ObjectiveLayout.Invasion;

        return gamemode.ToUpperInvariant() switch
        {
            "AAS" => ObjectiveLayout.Aas,
            "RAAS" => ObjectiveLayout.Raas,
            "SKIRMISH" => ObjectiveLayout.Skirmish,
            "TC" or "TERRITORYCONTROL" => ObjectiveLayout.TerritoryControl,
            "SEED" => ObjectiveLayout.Seed,
            _ => ObjectiveLayout.Unknown
        };
    }
}
