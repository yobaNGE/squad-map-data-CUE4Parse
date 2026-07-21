using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal readonly record struct SceneTransform(Vec3 Location, Rotator Rotation, Vec3 Scale)
{
    public static SceneTransform Identity => new(Vec3.Zero, Rotator.Zero, Vec3.One);
}

internal sealed class SceneTransformResolver(UnrealPropertyReader properties)
{
    private readonly Dictionary<string, SceneTransform> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SceneTransform ResolveActor(UObject actor)
    {
        var root = properties.ObjectInherited(actor, "RootComponent", "DefaultSceneRoot", "SceneComponent");
        return ResolveComponent(root);
    }

    public SceneTransform ResolveComponent(UObject? component) =>
        ResolveComponent(component, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private SceneTransform ResolveComponent(UObject? component, ISet<string> resolving)
    {
        if (component is null) return SceneTransform.Identity;
        var path = component.GetPathName();
        if (_cache.TryGetValue(path, out var cached)) return cached;
        if (!resolving.Add(path)) return SceneTransform.Identity;

        var local = new SceneTransform(
            properties.VectorInherited(component, "RelativeLocation"),
            properties.RotationInherited(component, "RelativeRotation"),
            properties.VectorInherited(component, "RelativeScale3D", Vec3.One));
        var parentComponent = properties.Object(component, "AttachParent");
        var result = parentComponent is null ? local : Compose(local, ResolveComponent(parentComponent, resolving));

        resolving.Remove(path);
        _cache[path] = result;
        return result;
    }

    private static SceneTransform Compose(SceneTransform child, SceneTransform parent)
    {
        var childTransform = ToUnrealTransform(child);
        var parentTransform = ToUnrealTransform(parent);
        var world = childTransform * parentTransform;
        var rotation = world.Rotator();
        return new SceneTransform(
            new Vec3(world.Translation.X, world.Translation.Y, world.Translation.Z),
            new Rotator(rotation.Pitch, rotation.Yaw, rotation.Roll),
            new Vec3(world.Scale3D.X, world.Scale3D.Y, world.Scale3D.Z));
    }

    private static FTransform ToUnrealTransform(SceneTransform transform) =>
        new(
            new FRotator(transform.Rotation.Pitch, transform.Rotation.Yaw, transform.Rotation.Roll),
            new FVector(transform.Location.X, transform.Location.Y, transform.Location.Z),
            new FVector(transform.Scale.X, transform.Scale.Y, transform.Scale.Z));
}
