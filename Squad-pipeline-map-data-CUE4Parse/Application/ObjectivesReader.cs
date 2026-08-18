using System.Globalization;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class ObjectivesReader(UnrealPropertyReader properties)
{
    public IReadOnlyDictionary<string, LayerObjective> Read(
        LayerReadContext context,
        string gamemode,
        CapturePoints capturePoints) => ObjectiveLayoutResolver.Resolve(context, gamemode) switch
        {
            ObjectiveLayout.Invasion => ReadInvasion(context, capturePoints),
            ObjectiveLayout.Aas => ReadAas(context, capturePoints),
            ObjectiveLayout.Raas => ReadRaas(context, capturePoints),
            ObjectiveLayout.Skirmish => ReadSkirmish(context, capturePoints),
            ObjectiveLayout.TerritoryControl => new Dictionary<string, LayerObjective>(),
            ObjectiveLayout.Seed => ReadSeed(context, capturePoints),
            _ => new Dictionary<string, LayerObjective>()
        };

    private IReadOnlyDictionary<string, LayerObjective> ReadInvasion(
        LayerReadContext context,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var clusters = context.FindExact("BP_CaptureZoneCluster_C");
        var nodeNames = CapturePointNames.ByPath(capturePoints.Clusters.Links);
        var pointsByCluster = new Dictionary<string, List<ObjectivePoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in context.FindExact("BP_CaptureZoneInvasion_C"))
        {
            var cluster = FindParentActor(actor, clusters);
            if (cluster is null) continue;
            var clusterName = GetGraphNodeName(cluster);
            if (!pointsByCluster.TryGetValue(clusterName, out var points))
                pointsByCluster[clusterName] = points = [];
            points.Add(ReadPoint(actor, context, transforms, includeDisplayName: false, includeScaling: false));
        }

        var graphOrder = capturePoints.Clusters.PointsOrder ?? [];
        var positions = BuildGraphPositions(capturePoints.Clusters.Links ?? []);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var clusterName in graphOrder
                     .Where(name => !name.EndsWith(" Main", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var points = pointsByCluster.GetValueOrDefault(clusterName) ?? [];
            result[clusterName] = new ObjectiveCluster(
                clusterName,
                positions.GetValueOrDefault(clusterName),
                Average(points),
                points);
        }

        foreach (var main in context.FindExact("BP_CaptureZoneMain_C")
                     .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetMainName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), context, transforms, false);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, int> BuildGraphPositions(IReadOnlyList<CaptureLink> links)
    {
        var successors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var nodesWithIncomingLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            nodesWithIncomingLinks.Add(link.NodeB);
            successors.TryAdd(link.NodeB, []);
            if (!successors.TryGetValue(link.NodeA, out var outgoing))
                successors[link.NodeA] = outgoing = [];
            outgoing.Add(link.NodeB);
        }

        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var source in successors.Keys.Where(node => !nodesWithIncomingLinks.Contains(node)))
        {
            positions[source] = 1;
            queue.Enqueue(source);
        }

        while (queue.TryDequeue(out var node))
        {
            foreach (var successor in successors[node])
            {
                if (!positions.TryAdd(successor, positions[node] + 1)) continue;
                queue.Enqueue(successor);
            }
        }

        return positions;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadAas(
        LayerReadContext context,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var positions = capturePoints.Points.PositionsByPath ??
                        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodeNames = CapturePointNames.ByPath(capturePoints.Points.Links);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in context.FindExact("BP_CaptureZone_C"))
        {
            var displayName = nodeNames.GetValueOrDefault(actor.GetPathName(), GetAasDisplayName(actor, context));
            result[actor.Name] = ReadCaptureActor(actor, displayName, context, transforms);
        }

        foreach (var main in context.FindExact("BP_CaptureZoneMain_C")
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetMainName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(main.GetPathName()), context, transforms, false);
        }
        return result;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadRaas(
        LayerReadContext context,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var clusters = context.FindExact("BP_CaptureZoneCluster_C");
        var nodeNames = CapturePointNames.ByPath(capturePoints.Lanes.Links);
        var pointsByCluster = new Dictionary<string, List<ObjectivePoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in context.FindExact("BP_CaptureZone_C", "BP_CaptureZoneInvasion_C"))
        {
            var cluster = FindParentActor(actor, clusters);
            var clusterName = cluster is null ? GetGraphNodeName(actor) : GetGraphNodeName(cluster);
            if (!pointsByCluster.TryGetValue(clusterName, out var points))
                pointsByCluster[clusterName] = points = [];
            points.Add(ReadPoint(
                actor,
                context,
                transforms,
                includeDisplayName: true,
                includeScaling: false,
                GetAasDisplayName(actor, context)));
        }

        var clusterOrder = new List<string>();
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var laneName in capturePoints.Lanes.ListOfLanes ?? [])
        {
            if (capturePoints.Lanes.LaneObjects?.GetValueOrDefault(laneName) is not { } lane) continue;
            for (var index = 0; index < lane.PointsOrder.Count; index++)
            {
                var name = lane.PointsOrder[index];
                positions.TryAdd(name, index + 1);
                if (!name.EndsWith(" Main", StringComparison.OrdinalIgnoreCase) &&
                    !clusterOrder.Contains(name, StringComparer.OrdinalIgnoreCase))
                    clusterOrder.Add(name);
            }
        }

        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);
        foreach (var clusterName in clusterOrder)
        {
            var points = pointsByCluster.GetValueOrDefault(clusterName) ?? [];
            result[clusterName] = new ObjectiveCluster(
                clusterName,
                positions.GetValueOrDefault(clusterName),
                Average(points),
                points);
        }

        foreach (var main in context.FindExact("BP_CaptureZoneMain_C")
                     .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetMainName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), context, transforms, false);
        }
        return result;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadSkirmish(
        LayerReadContext context,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var nodeNames = CapturePointNames.ByPath(capturePoints.Points.Links);
        var actorsByPath = context.FindExact("BP_CaptureZone_C")
            .ToDictionary(actor => actor.GetPathName(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in capturePoints.Points.Links ?? [])
        {
            AddObjective(link.NodeAPath, link.NodeA);
            AddObjective(link.NodeBPath, link.NodeB);
        }

        var mainPositions = capturePoints.Points.PositionsByPath ??
                            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var main in context.FindExact("BP_CaptureZoneMain_C")
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetMainName(main);
            result[name] = ReadActor(main, name, "Main", mainPositions.GetValueOrDefault(main.GetPathName()), context, transforms, false);
        }
        return result;

        void AddObjective(string path, string displayName)
        {
            if (displayName.EndsWith(" Main", StringComparison.OrdinalIgnoreCase) ||
                !actorsByPath.TryGetValue(path, out var actor)) return;
            result[actor.Name] = ReadCaptureActor(actor, displayName, context, transforms);
        }
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadSeed(
        LayerReadContext context,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var order = capturePoints.Points.PointsOrder ?? [];
        var nodeNames = CapturePointNames.ByPath(capturePoints.Points.Links);
        var positions = order.Select((name, index) => (name, position: index + 1))
            .ToDictionary(entry => entry.name, entry => entry.position, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in context.FindExact("BP_CaptureZone_C"))
        {
            var graphName = GetGraphNodeName(actor);
            var separator = graphName.IndexOf('-');
            var displayName = nodeNames.GetValueOrDefault(actor.GetPathName()) ?? (separator < 0
                ? TextFormatting.Prettify(graphName)
                : graphName[..(separator + 1)] + TextFormatting.Prettify(graphName[(separator + 1)..]));
            result[actor.Name] = ReadCaptureActor(actor, displayName, context, transforms);
        }

        foreach (var main in context.FindExact("BP_CaptureZoneMain_C")
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetMainName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), context, transforms, false);
        }
        return result;
    }

    private ObjectiveActor ReadCaptureActor(
        UObject actor,
        string displayName,
        LayerReadContext context,
        ObjectiveTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        return new ObjectiveActor(
            ReadFlagName(actor, context),
            actor.Name,
            displayName,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadVolumes(actor, context, transforms, false),
            null);
    }

    private string GetAasDisplayName(UObject actor, LayerReadContext context)
    {
        var flagName = ReadFlagName(actor, context);
        var separator = actor.Name.IndexOf('-');
        return separator < 0 ? flagName : actor.Name[..(separator + 1)] + flagName;
    }

    private ObjectivePoint ReadPoint(
        UObject actor,
        LayerReadContext context,
        ObjectiveTransformResolver transforms,
        bool includeDisplayName,
        bool includeScaling,
        string? displayName = null)
    {
        var transform = transforms.ResolveActor(actor);
        var name = ReadFlagName(actor, context);
        return new ObjectivePoint(
            name,
            actor.Name,
            includeDisplayName ? displayName ?? actor.Name : null,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadVolumes(actor, context, transforms, includeScaling));
    }

    private ObjectiveActor ReadActor(
        UObject actor,
        string displayName,
        string name,
        int? position,
        LayerReadContext context,
        ObjectiveTransformResolver transforms,
        bool includeScaling)
    {
        var transform = transforms.ResolveActor(actor);
        return new ObjectiveActor(
            name,
            displayName,
            displayName,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadVolumes(actor, context, transforms, includeScaling),
            position);
    }

    private IReadOnlyList<ObjectiveVolume> ReadVolumes(
        UObject actor,
        LayerReadContext context,
        ObjectiveTransformResolver transforms,
        bool includeScaling) => context.OwnedBy(actor)
        .Where(IsVolume)
        .Select(component => ReadVolume(component, transforms, includeScaling))
        .Where(volume => volume is not null)
        .Cast<VolumeWithRadius>()
        .OrderBy(volume => volume.Radius)
        .Select(volume => volume.Volume)
        .ToArray();

    private VolumeWithRadius? ReadVolume(
        UObject component,
        ObjectiveTransformResolver transforms,
        bool includeScaling)
    {
        var transform = transforms.ResolveComponent(component);
        return component.ExportType switch
        {
            "BoxComponent" => ReadBox(component, transform, includeScaling),
            "SphereComponent" => ReadSphere(component, transform, includeScaling),
            "CapsuleComponent" => ReadCapsule(component, transform, includeScaling),
            _ => null
        };
    }

    private VolumeWithRadius ReadBox(UObject component, SceneTransform transform, bool includeScaling)
    {
        var baseExtent = properties.VectorInherited(component, "BoxExtent", new Vec3(32, 32, 32));
        var x = VolumeTransformMath.Multiply(baseExtent.X, transform.Scale.X);
        var y = VolumeTransformMath.Multiply(baseExtent.Y, transform.Scale.Y);
        var z = VolumeTransformMath.Multiply(baseExtent.Z, transform.Scale.Z);
        var baseRadius = VolumeTransformMath.Size(baseExtent);
        var bounds = new FBoxSphereBounds(
            FVector.ZeroVector,
            new FVector(baseExtent.X, baseExtent.Y, baseExtent.Z),
            (float)baseRadius);
        var unrealTransform = new FTransform(
            new FRotator(transform.Rotation.Pitch, transform.Rotation.Yaw, transform.Rotation.Roll),
            FVector.ZeroVector,
            new FVector(transform.Scale.X, transform.Scale.Y, transform.Scale.Z));
        var radius = bounds.TransformBy(unrealTransform).SphereRadius;
        return new VolumeWithRadius(
            CreateVolume(component, transform, false, radius, true,
                CreateExtent(baseExtent.X, baseExtent.Y, baseExtent.Z, transform, true), false),
            radius);
    }

    private VolumeWithRadius ReadSphere(UObject component, SceneTransform transform, bool includeScaling)
    {
        var baseRadius = properties.DoubleInherited(component, 0, "SphereRadius");
        var radius = VolumeTransformMath.ScaleSphereRadius(baseRadius, transform.Scale);
        var extents = new Vec3(radius, radius, radius);
        return new VolumeWithRadius(
            CreateVolume(component, transform, true, radius, false,
                CreateExtent(extents.X, extents.Y, extents.Z, transform, includeScaling), false),
            radius);
    }

    private VolumeWithRadius ReadCapsule(UObject component, SceneTransform transform, bool includeScaling)
    {
        var radius = properties.DoubleInherited(component, 0, "CapsuleRadius");
        var halfHeight = properties.DoubleInherited(component, 0, "CapsuleHalfHeight");
        var scaledRadius = VolumeTransformMath.ScaleCapsuleRadius(radius, transform.Scale);
        var scaledHalfHeight = VolumeTransformMath.ScaleCapsuleHalfHeight(halfHeight, transform.Scale);
        var length = scaledHalfHeight;
        var extents = VolumeTransformMath.RotateExtents(
            new Vec3(scaledRadius, scaledRadius, scaledHalfHeight),
            transform.Rotation);
        var extent = CreateExtent(extents.X, extents.Y, extents.Z, transform, includeScaling);
        var volume = CreateVolume(component, transform, false, length, false, extent, true) with
        {
            CapsuleRadius = FormatNumber(scaledRadius),
            CapsuleLength = FormatNumber(length),
            RotationX = VolumeTransformMath.CleanRotation(transform.Rotation.Roll),
            RotationY = VolumeTransformMath.CleanRotation(transform.Rotation.Pitch),
            RotationZ = VolumeTransformMath.CleanRotation(transform.Rotation.Yaw)
        };
        return new VolumeWithRadius(volume, Math.Max(scaledRadius, length / 2));
    }

    private static ObjectiveVolume CreateVolume(
        UObject component,
        SceneTransform transform,
        bool isSphere,
        double radius,
        bool isBox,
        ObjectiveExtent extent,
        bool isCapsule) => new(
        component.Name,
        transform.Location.X,
        transform.Location.Y,
        transform.Location.Z,
        isSphere,
        FormatNumber(radius),
        isBox,
        extent,
        isCapsule);

    private static ObjectiveExtent CreateExtent(
        double x,
        double y,
        double z,
        SceneTransform transform,
        bool includeScaling) => new(
        x,
        y,
        z,
        VolumeTransformMath.CleanRotation(transform.Rotation.Roll),
        VolumeTransformMath.CleanRotation(transform.Rotation.Pitch),
        VolumeTransformMath.CleanRotation(transform.Rotation.Yaw),
        includeScaling ? transform.Scale.X : null,
        includeScaling ? transform.Scale.Y : null,
        includeScaling ? transform.Scale.Z : null);

    private string ReadFlagName(UObject actor, LayerReadContext context)
    {
        var component = properties.ObjectInherited(actor, "SQCaptureZone", "SQCaptureZoneInvasion")
                        ?? context.OwnedBy(actor).FirstOrDefault(export =>
                            export.ExportType.Contains("CaptureZone", StringComparison.OrdinalIgnoreCase));
        return properties.StringInherited(component, actor.Name, "FlagName");
    }

    private UObject? FindParentActor(UObject actor, IEnumerable<UObject> candidates)
    {
        var root = properties.ObjectInherited(actor, "RootComponent", "DefaultSceneRoot");
        var parent = properties.Object(root, "AttachParent");
        if (parent is null) return null;
        var parentPath = parent.GetPathName();
        return candidates.FirstOrDefault(candidate =>
            parentPath.StartsWith(candidate.GetPathName() + ".", StringComparison.OrdinalIgnoreCase));
    }

    private static ObjectiveLocation Average(IReadOnlyList<ObjectivePoint> points) => points.Count == 0
        ? new ObjectiveLocation(0, 0, 0)
        : new ObjectiveLocation(
            AverageFloat(points.Select(point => point.LocationX)),
            AverageFloat(points.Select(point => point.LocationY)),
            AverageFloat(points.Select(point => point.LocationZ)));

    private static double AverageFloat(IEnumerable<double> values)
    {
        float sum = 0;
        var count = 0;
        foreach (var value in values)
        {
            sum += (float)value;
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    private static bool IsVolume(UObject export) => export.ExportType is
        "BoxComponent" or "SphereComponent" or "CapsuleComponent";

    private static string FormatNumber(double value) => value.ToString("0.0#####", CultureInfo.InvariantCulture);

    private string GetMainName(UObject actor)
    {
        var captureZone = properties.ObjectInherited(actor, "SQCaptureZone");
        var initialTeam = properties.IntInherited(captureZone, 0, "InitialTeam");
        return CapturePointNames.MainName(initialTeam, GetGraphNodeName(actor));
    }

    private string GetGraphNodeName(UObject actor) =>
        properties.String(actor, actor.Name, "ActorLabel");

    private sealed record VolumeWithRadius(ObjectiveVolume Volume, double Radius);

    private sealed class ObjectiveTransformResolver(UnrealPropertyReader propertyReader)
    {
        private readonly Dictionary<string, SceneTransform> _cache = new(StringComparer.OrdinalIgnoreCase);

        public SceneTransform ResolveActor(UObject actor) => ResolveComponent(
            propertyReader.ObjectInherited(actor, "RootComponent", "DefaultSceneRoot"));

        public SceneTransform ResolveComponent(UObject? component) => ResolveComponent(
            component,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private SceneTransform ResolveComponent(UObject? component, ISet<string> resolving)
        {
            if (component is null) return SceneTransform.Identity;
            var path = component.GetPathName();
            if (_cache.TryGetValue(path, out var cached)) return cached;
            if (!resolving.Add(path)) return SceneTransform.Identity;

            var local = new SceneTransform(
                propertyReader.VectorInherited(component, "RelativeLocation"),
                propertyReader.RotationInherited(component, "RelativeRotation"),
                propertyReader.VectorInherited(component, "RelativeScale3D", Vec3.One));
            var parent = ResolveComponent(propertyReader.Object(component, "AttachParent"), resolving);
            var rotatedLocation = Rotate(local.Location * parent.Scale, parent.Rotation);
            var result = new SceneTransform(
                new Vec3(
                    (float)(parent.Location.X + rotatedLocation.X),
                    (float)(parent.Location.Y + rotatedLocation.Y),
                    (float)(parent.Location.Z + rotatedLocation.Z)),
                new Rotator(
                    parent.Rotation.Pitch + local.Rotation.Pitch,
                    parent.Rotation.Yaw + local.Rotation.Yaw,
                    parent.Rotation.Roll + local.Rotation.Roll),
                parent.Scale * local.Scale);

            resolving.Remove(path);
            _cache[path] = result;
            return result;
        }

        private static Vec3 Rotate(Vec3 vector, Rotator rotation)
        {
            var pitch = rotation.Pitch * Math.PI / 180;
            var yaw = rotation.Yaw * Math.PI / 180;
            var roll = rotation.Roll * Math.PI / 180;
            var cp = Math.Cos(pitch);
            var sp = Math.Sin(pitch);
            var cy = Math.Cos(yaw);
            var sy = Math.Sin(yaw);
            var cr = Math.Cos(roll);
            var sr = Math.Sin(roll);
            return new Vec3(
                cy * cp * vector.X + (cy * sp * sr - sy * cr) * vector.Y +
                (cy * sp * cr + sy * sr) * vector.Z,
                sy * cp * vector.X + (sy * sp * sr + cy * cr) * vector.Y +
                (sy * sp * cr - cy * sr) * vector.Z,
                -sp * vector.X + cp * sr * vector.Y + cp * cr * vector.Z);
        }
    }
}
