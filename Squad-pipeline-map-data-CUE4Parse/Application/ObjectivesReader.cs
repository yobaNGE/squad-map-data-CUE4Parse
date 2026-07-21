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
        CapturePoints capturePoints) => gamemode.ToUpperInvariant() switch
        {
            "INVASION" => ReadInvasion(context.Exports, capturePoints),
            "AAS" => ReadAas(context.Exports, capturePoints),
            "RAAS" => ReadRaas(context.Exports, capturePoints),
            "SKIRMISH" => ReadSkirmish(context.Exports, capturePoints),
            "TC" or "TERRITORYCONTROL" => new Dictionary<string, LayerObjective>(),
            "SEED" => ReadSeed(context.Exports, capturePoints),
            _ => new Dictionary<string, LayerObjective>()
        };

    private IReadOnlyDictionary<string, LayerObjective> ReadInvasion(
        IReadOnlyList<UObject> exports,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var clusters = exports
            .Where(export => export.ExportType.Equals("BP_CaptureZoneCluster_C", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pointsByCluster = new Dictionary<string, List<ObjectivePoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in exports.Where(export =>
                     export.ExportType.Equals("BP_CaptureZoneInvasion_C", StringComparison.OrdinalIgnoreCase)))
        {
            var cluster = FindParentActor(actor, clusters);
            if (cluster is null) continue;
            var clusterName = GetGraphNodeName(cluster);
            if (!pointsByCluster.TryGetValue(clusterName, out var points))
                pointsByCluster[clusterName] = points = [];
            points.Add(ReadPoint(actor, exports, transforms, includeDisplayName: false, includeScaling: false));
        }

        var graphOrder = capturePoints.Clusters.PointsOrder ?? [];
        var positions = BuildReverseGraphPositions(capturePoints.Clusters.Links ?? []);
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

        foreach (var main in exports
                     .Where(export => export.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetGraphNodeName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), exports, transforms, false);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, int> BuildReverseGraphPositions(IReadOnlyList<CaptureLink> links)
    {
        var predecessors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var nodesWithOutgoingLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            nodesWithOutgoingLinks.Add(link.NodeA);
            predecessors.TryAdd(link.NodeA, []);
            if (!predecessors.TryGetValue(link.NodeB, out var incoming))
                predecessors[link.NodeB] = incoming = [];
            incoming.Add(link.NodeA);
        }

        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var terminal in predecessors.Keys.Where(node => !nodesWithOutgoingLinks.Contains(node)))
        {
            positions[terminal] = 1;
            queue.Enqueue(terminal);
        }

        while (queue.TryDequeue(out var node))
        {
            foreach (var predecessor in predecessors[node])
            {
                if (!positions.TryAdd(predecessor, positions[node] + 1)) continue;
                queue.Enqueue(predecessor);
            }
        }

        return positions;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadAas(
        IReadOnlyList<UObject> exports,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var order = capturePoints.Points.PointsOrder ?? [];
        var positions = order.Select((name, index) => (name, position: index + 1))
            .ToDictionary(entry => entry.name, entry => entry.position, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in exports.Where(export =>
                     export.ExportType.Equals("BP_CaptureZone_C", StringComparison.OrdinalIgnoreCase)))
        {
            var displayName = GetAasDisplayName(actor, exports);
            result[actor.Name] = ReadCaptureActor(actor, displayName, exports, transforms);
        }

        foreach (var main in exports
                     .Where(export => export.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetGraphNodeName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), exports, transforms, false);
        }
        return result;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadRaas(
        IReadOnlyList<UObject> exports,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var clusters = exports
            .Where(export => export.ExportType.Equals("BP_CaptureZoneCluster_C", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pointsByCluster = new Dictionary<string, List<ObjectivePoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in exports.Where(export =>
                     export.ExportType.Equals("BP_CaptureZone_C", StringComparison.OrdinalIgnoreCase)))
        {
            var cluster = FindParentActor(actor, clusters);
            if (cluster is null) continue;
            var clusterName = GetGraphNodeName(cluster);
            if (!pointsByCluster.TryGetValue(clusterName, out var points))
                pointsByCluster[clusterName] = points = [];
            points.Add(ReadPoint(
                actor,
                exports,
                transforms,
                includeDisplayName: true,
                includeScaling: false,
                GetAasDisplayName(actor, exports)));
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

        foreach (var main in exports
                     .Where(export => export.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetGraphNodeName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), exports, transforms, false);
        }
        return result;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadSkirmish(
        IReadOnlyList<UObject> exports,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var actorsByFlagName = exports
            .Where(export => export.ExportType.Equals("BP_CaptureZone_C", StringComparison.OrdinalIgnoreCase))
            .GroupBy(actor => ReadFlagName(actor, exports), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var objectiveNames = (capturePoints.Points.Links ?? [])
            .SelectMany(link => new[] { link.NodeA, link.NodeB })
            .Where(name => !name.EndsWith(" Main", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var displayName in objectiveNames)
        {
            var separator = displayName.IndexOf('-');
            var flagName = separator < 0 ? displayName : displayName[(separator + 1)..];
            if (!actorsByFlagName.TryGetValue(flagName, out var actor)) continue;
            result[actor.Name] = ReadCaptureActor(actor, displayName, exports, transforms);
        }

        var mainPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["00-Team1 Main"] = 1,
            ["Z-Team2 Main"] = (capturePoints.Points.Links?.Count ?? 0) + 1
        };
        foreach (var main in exports
                     .Where(export => export.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetGraphNodeName(main);
            result[name] = ReadActor(main, name, "Main", mainPositions[name], exports, transforms, false);
        }
        return result;
    }

    private IReadOnlyDictionary<string, LayerObjective> ReadSeed(
        IReadOnlyList<UObject> exports,
        CapturePoints capturePoints)
    {
        var transforms = new ObjectiveTransformResolver(properties);
        var order = capturePoints.Points.PointsOrder ?? [];
        var positions = order.Select((name, index) => (name, position: index + 1))
            .ToDictionary(entry => entry.name, entry => entry.position, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, LayerObjective>(StringComparer.OrdinalIgnoreCase);

        foreach (var actor in exports.Where(export =>
                     export.ExportType.Equals("BP_CaptureZone_C", StringComparison.OrdinalIgnoreCase)))
        {
            var graphName = GetGraphNodeName(actor);
            var separator = graphName.IndexOf('-');
            var displayName = separator < 0
                ? TextFormatting.Prettify(graphName)
                : graphName[..(separator + 1)] + TextFormatting.Prettify(graphName[(separator + 1)..]);
            result[actor.Name] = ReadCaptureActor(actor, displayName, exports, transforms);
        }

        foreach (var main in exports
                     .Where(export => export.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(GetGraphNodeName, StringComparer.OrdinalIgnoreCase))
        {
            var name = GetGraphNodeName(main);
            result[name] = ReadActor(main, name, "Main", positions.GetValueOrDefault(name), exports, transforms, false);
        }
        return result;
    }

    private ObjectiveActor ReadCaptureActor(
        UObject actor,
        string displayName,
        IReadOnlyList<UObject> exports,
        ObjectiveTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        return new ObjectiveActor(
            ReadFlagName(actor, exports),
            actor.Name,
            displayName,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadVolumes(actor, exports, transforms, false),
            null);
    }

    private string GetAasDisplayName(UObject actor, IReadOnlyList<UObject> exports)
    {
        var flagName = ReadFlagName(actor, exports);
        var separator = actor.Name.IndexOf('-');
        return separator < 0 ? flagName : actor.Name[..(separator + 1)] + flagName;
    }

    private ObjectivePoint ReadPoint(
        UObject actor,
        IReadOnlyList<UObject> exports,
        ObjectiveTransformResolver transforms,
        bool includeDisplayName,
        bool includeScaling,
        string? displayName = null)
    {
        var transform = transforms.ResolveActor(actor);
        var name = ReadFlagName(actor, exports);
        return new ObjectivePoint(
            name,
            actor.Name,
            includeDisplayName ? displayName ?? actor.Name : null,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadVolumes(actor, exports, transforms, includeScaling));
    }

    private ObjectiveActor ReadActor(
        UObject actor,
        string displayName,
        string name,
        int? position,
        IReadOnlyList<UObject> exports,
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
            ReadVolumes(actor, exports, transforms, includeScaling),
            position);
    }

    private IReadOnlyList<ObjectiveVolume> ReadVolumes(
        UObject actor,
        IReadOnlyList<UObject> exports,
        ObjectiveTransformResolver transforms,
        bool includeScaling) => exports
        .Where(export => IsOwnedBy(export, actor) && IsVolume(export))
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
        var x = VolumeTransformMath.Multiply(baseRadius, transform.Scale.X);
        var y = VolumeTransformMath.Multiply(baseRadius, transform.Scale.Y);
        var z = VolumeTransformMath.Multiply(baseRadius, transform.Scale.Z);
        var extents = VolumeTransformMath.RotateExtents(new Vec3(x, y, z), transform.Rotation);
        var radius = VolumeTransformMath.RotateRadius(new Vec3(x, y, z), transform.Rotation);
        return new VolumeWithRadius(
            CreateVolume(component, transform, true, radius, false,
                CreateExtent(extents.X, extents.Y, extents.Z, transform, includeScaling), false),
            radius);
    }

    private VolumeWithRadius ReadCapsule(UObject component, SceneTransform transform, bool includeScaling)
    {
        var radius = properties.DoubleInherited(component, 0, "CapsuleRadius");
        var halfHeight = properties.DoubleInherited(component, 0, "CapsuleHalfHeight");
        var radiusX = VolumeTransformMath.Multiply(radius, transform.Scale.X);
        var radiusY = VolumeTransformMath.Multiply(radius, transform.Scale.Y);
        var scaledRadius = Math.Max(radiusX, radiusY);
        var scaledHalfHeight = VolumeTransformMath.Multiply(halfHeight, transform.Scale.Z);
        var length = scaledHalfHeight;
        var extents = VolumeTransformMath.RotateExtents(
            new Vec3(radiusX, radiusY, scaledHalfHeight),
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

    private string ReadFlagName(UObject actor, IReadOnlyList<UObject> exports)
    {
        var component = properties.ObjectInherited(actor, "SQCaptureZone", "SQCaptureZoneInvasion")
                        ?? exports.FirstOrDefault(export => IsOwnedBy(export, actor) &&
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

    private static bool IsOwnedBy(UObject export, UObject actor) => export.GetPathName()
        .StartsWith(actor.GetPathName() + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsVolume(UObject export) => export.ExportType is
        "BoxComponent" or "SphereComponent" or "CapsuleComponent";

    private static string FormatNumber(double value) => value.ToString("0.0#####", CultureInfo.InvariantCulture);

    private static string GetGraphNodeName(UObject actor)
    {
        if (actor.Name.Contains("Team1Main", StringComparison.OrdinalIgnoreCase)) return "00-Team1 Main";
        if (actor.Name.Contains("Team2Main", StringComparison.OrdinalIgnoreCase)) return "Z-Team2 Main";
        return actor.Name;
    }

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
