using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LayerReadContext
{
    private readonly LevelExportIndex _exports;

    public LayerReadContext(UObject world, UnrealPropertyReader properties)
    {
        World = world;
        _exports = new LevelExportIndex(world);
        WorldSettings = _exports.WorldSettings;
        Transforms = new SceneTransformResolver(properties);
    }

    public UObject World { get; }
    public UObject? WorldSettings { get; }
    public SceneTransformResolver Transforms { get; }

    public IReadOnlyList<UObject> FindExact(params string[] exportTypes) => _exports.FindExact(exportTypes);

    public IReadOnlyList<UObject> FindActorsDerivedFrom(params string[] typeNames) =>
        _exports.FindActorsDerivedFrom(typeNames);

    public IReadOnlyList<UObject> OwnedBy(UObject actor) => _exports.OwnedBy(actor);
}
