using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LayerReadContext
{
    public LayerReadContext(UObject world, UnrealPropertyReader properties)
    {
        World = world;
        Exports = world.Owner?.GetExports().ToArray() ?? [];
        WorldSettings = Exports.FirstOrDefault(export =>
            export.ExportType.Equals("SQWorldSettings", StringComparison.OrdinalIgnoreCase));
        Transforms = new SceneTransformResolver(properties);
    }

    public UObject World { get; }
    public IReadOnlyList<UObject> Exports { get; }
    public UObject? WorldSettings { get; }
    public SceneTransformResolver Transforms { get; }
}
