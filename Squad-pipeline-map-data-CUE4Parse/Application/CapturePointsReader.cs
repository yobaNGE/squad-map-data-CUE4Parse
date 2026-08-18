using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class CapturePointsReader(UnrealPropertyReader properties)
{
    public CapturePoints Read(LayerReadContext context, string gamemode) => ObjectiveLayoutResolver.Resolve(context, gamemode) switch
    {
        ObjectiveLayout.Invasion => ReadInvasion(context),
        ObjectiveLayout.Aas => ReadAas(context),
        ObjectiveLayout.Raas => ReadRaas(context),
        ObjectiveLayout.Skirmish => ReadSkirmish(context),
        ObjectiveLayout.TerritoryControl => ReadTerritoryControl(context),
        ObjectiveLayout.Seed => ReadSeed(context),
        ObjectiveLayout.Destruction => ReadDestruction(context),
        _ => CapturePoints.Empty()
    };

    private CapturePoints ReadInvasion(LayerReadContext context)
    {
        var initializer = context.FindExact("SQGraphRAASInitializerComponent").FirstOrDefault()
                          ?? FindExport(context, "SQGraphAASInitializerComponent");
        var links = CapturePointNames.NormalizeMains(ReadLinks(initializer, "DesignOutgoingLinks"));
        var pointsOrder = BuildPointsOrder(links);

        return CapturePoints.Empty("Invasion Graph") with
        {
            Clusters = new CaptureClusterGraph(
                links,
                pointsOrder,
                pointsOrder.Count,
                FindMains(pointsOrder))
        };
    }

    private CapturePoints ReadAas(LayerReadContext context)
    {
        var initializer = FindExport(context, "SQGraphAASInitializerComponent");
        var links = CapturePointNames.NormalizeMains(ReadLinks(initializer, "DesignOutgoingLinks", GetCapturePointName));
        var graph = BuildDirectedGraph(links);

        return CapturePoints.Empty("AAS Graph") with
        {
            Points = new CapturePointGraph(
                graph.PointsOrder,
                graph.NumberOfPoints,
                graph.Mains,
                links,
                PositionsByPath: graph.PositionsByPath)
        };
    }

    private CapturePoints ReadRaas(LayerReadContext context)
    {
        var clusters = context.FindExact("BP_CaptureZoneCluster_C");
        var nodesWithCapturePoints = FindRaasNodesWithCapturePoints(context, clusters);
        var graphInitializer = FindExport(context, "SQGraphRAASInitializerComponent");
        if (graphInitializer is not null)
            return ReadRaasGraph(graphInitializer, clusters, nodesWithCapturePoints);

        var initializer = FindExport(context, "SQRAASLaneInitializer_C");
        var allLinks = new List<CaptureLink>();
        var laneNames = new List<string>();
        var lanes = new Dictionary<string, CaptureLane>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in properties.Array(initializer, "AAS Lanes"))
        {
            if (value is not IPropertyHolder lane) continue;
            var laneName = UnrealPropertyReader.ToStringValue(properties.RawStartingWith(lane, "LaneName_"));
            if (string.IsNullOrWhiteSpace(laneName)) continue;

            var laneLinks = CapturePointNames.NormalizeMains(ProjectRaasLinks(
                ReadLinks(
                    properties.ArrayStartingWith(lane, "AASLaneLinks_"),
                    actor => GetRaasNodeName(actor, clusters)),
                nodesWithCapturePoints));
            var pointsOrder = BuildPointsOrder(laneLinks);
            laneNames.Add(laneName);
            allLinks.AddRange(laneLinks);
            lanes[laneName] = new CaptureLane(
                laneName,
                laneLinks,
                pointsOrder,
                pointsOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                FindMains(pointsOrder));
        }

        return CapturePoints.Empty("RAASLane Graph") with
        {
            Lanes = new CaptureLanes(allLinks, laneNames, lanes)
        };
    }

    private CapturePoints ReadRaasGraph(
        UObject initializer,
        IReadOnlyList<UObject> clusters,
        IReadOnlySet<string> nodesWithCapturePoints)
    {
        const string laneName = "RAAS";
        var links = CapturePointNames.NormalizeMains(ProjectRaasLinks(
            ReadLinks(initializer, "DesignOutgoingLinks", actor => GetRaasNodeName(actor, clusters)),
            nodesWithCapturePoints));
        var pointsOrder = BuildPointsOrder(links);
        var lane = new CaptureLane(
            laneName,
            links,
            pointsOrder,
            pointsOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            FindMains(pointsOrder));

        return CapturePoints.Empty("RAASLane Graph") with
        {
            Lanes = new CaptureLanes(links, [laneName], new Dictionary<string, CaptureLane>
            {
                [laneName] = lane
            })
        };
    }

    private CapturePoints ReadSkirmish(LayerReadContext context)
    {
        var initializer = FindExport(context, "SQGraphAASInitializerComponent");
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pointNumber = 0;
        var links = CapturePointNames.NormalizeMains(ReadLinks(initializer, "DesignOutgoingLinks", GetSkirmishPointName));
        var graph = BuildDirectedGraph(links);

        return CapturePoints.Empty("Skirmish Graph") with
        {
            Points = new CapturePointGraph(
                ["invalidForSkirmishGameMode"],
                graph.NumberOfPoints,
                graph.Mains,
                links,
                PositionsByPath: graph.PositionsByPath)
        };

        string GetSkirmishPointName(UObject actor)
        {
            var mainName = GetGraphNodeName(actor);
            if (mainName.EndsWith(" Main", StringComparison.OrdinalIgnoreCase)) return mainName;

            var path = actor.GetPathName();
            if (names.TryGetValue(path, out var name)) return name;
            var captureZone = properties.ObjectInherited(actor, "SQCaptureZone");
            var flagName = properties.StringInherited(captureZone, mainName, "FlagName");
            name = $"{++pointNumber:00}-{flagName}";
            names[path] = name;
            return name;
        }
    }

    private CapturePoints ReadTerritoryControl(LayerReadContext context)
    {
        var graph = FindExport(context, "TC_HexGraph_C");
        var transforms = context.Transforms;
        var mains = context.FindExact("BP_CaptureZoneMain_C")
            .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mainNames = mains.Select(GetGraphNodeName).ToArray();
        var objectives = mains.Select((main, index) => ReadMainObjective(main, index + 1, transforms)).ToArray();
        var hexs = context.FindExact("TC_HexZone_C")
            .Select(hex => ReadHex(hex, transforms))
            .OrderByDescending(hex => hex.HexNumber)
            .ToArray();

        return CapturePoints.Empty("TC Hex Zone") with
        {
            Points = new CapturePointGraph(
                mainNames,
                mainNames.Length,
                mainNames,
                Objectives: objectives),
            Hexs = new CaptureHexs(
                properties.Double(graph, 0, "Start Spline Ownership"),
                properties.Double(graph, 0, "End Spline Ownership"),
                properties.Double(graph, 0, "Start Random Anchor Distance"),
                properties.Double(graph, 0, "End Random Anchor Distance"),
                ReadAnchorNumbers(graph, "Team 1 Anchors"),
                ReadAnchorNumbers(graph, "Team 2 Anchors"),
                hexs)
        };
    }

    private CapturePoints ReadSeed(LayerReadContext context)
    {
        var initializer = FindExport(context, "SQGraphAASInitializerComponent");
        var links = CapturePointNames.NormalizeMains(ReadLinks(initializer, "DesignOutgoingLinks", GetSeedPointName));
        var pointsOrder = BuildPointsOrder(links);

        return CapturePoints.Empty("AAS Graph") with
        {
            Points = new CapturePointGraph(
                pointsOrder,
                pointsOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                FindMains(pointsOrder),
                links)
        };
    }

    private CapturePoints ReadDestruction(LayerReadContext context)
    {
        var transforms = context.Transforms;
        var mains = context.FindExact("BP_CaptureZoneMain_C")
            .OrderBy(GetGraphNodeName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mainNames = mains.Select(GetGraphNodeName).ToArray();
        var director = FindExport(context, "BP_DestructionPhaseDirector_C")
                       ?? throw new InvalidDataException("Destruction layer does not contain a phase director.");

        return CapturePoints.Empty("Destruction") with
        {
            Points = new CapturePointGraph(
                mainNames,
                mainNames.Length,
                Objectives: mains.Select((main, index) => ReadMainObjective(main, index + 1, transforms)).ToArray()),
            ObjectiveSpawnLocations = context.FindExact("BP_ObjectiveSpawnLocation_C")
                .Select(actor => ReadObjectiveSpawnLocation(actor, transforms))
                .ToArray(),
            DestructionObject = ReadDestructionObject(director, context, transforms)
        };
    }

    private CaptureDestructionObject ReadDestructionObject(
        UObject director,
        LayerReadContext context,
        SceneTransformResolver transforms) => new(
        ReadTeam(properties.StringInherited(director, string.Empty, "AttackingTeam")),
        properties.IntInherited(director, 0, "DelayBetweenPhases"),
        properties.ObjectInherited(director, "Objective class")?.Name,
        properties.IntInherited(director, 0, "RoundTimerIncrease"),
        properties.BoolInherited(director, false, "TimerIncreasePerPhase"),
        properties.Array(director, "Phases setup")
            .OfType<IPropertyHolder>()
            .Select((phase, index) => ReadDestructionPhase(phase, index, transforms))
            .ToArray(),
        context.FindActorsDerivedFrom("BP_NoDeployZone_Destruction_C")
            .Select(zone => ReadNoDeployZone(zone, context, transforms))
            .ToArray());

    private CaptureDestructionPhase ReadDestructionPhase(
        IPropertyHolder phase,
        int index,
        SceneTransformResolver transforms) => new(
        index,
        properties.ArrayStartingWith(phase, "Phaseobjectives_")
            .Select(properties.ResolveObject)
            .Where(actor => actor is not null)
            .Cast<UObject>()
            .Select(actor => ReadDestructionObjective(actor, transforms))
            .ToArray());

    private CaptureDestructionObjective ReadDestructionObjective(UObject actor, SceneTransformResolver transforms) => new(
        properties.IntInherited(actor, 0, "Number of spots"),
        properties.IntInherited(actor, 0, "Min Distance Between Spots"),
        UnrealPropertyReader.ToInt(properties.RawInherited(actor, "Number of caches")),
        ReadSplinePoints(properties.ObjectInherited(actor, "ObjectiveAreaBorder"), transforms));

    private IReadOnlyList<CaptureDestructionSplinePoint> ReadSplinePoints(
        UObject? border,
        SceneTransformResolver transforms)
    {
        var spline = properties.ObjectInherited(border, "Spline", "RootComponent");
        var position = properties.Struct(properties.Struct(spline, "SplineCurves"), "Position");
        var transform = transforms.ResolveComponent(spline);
        return properties.Array(position, "Points")
            .OfType<IPropertyHolder>()
            .Select(point => transforms.TransformPosition(transform, properties.Vector(point, "OutVal")))
            .Select(point => new CaptureDestructionSplinePoint(point.X, point.Y, point.Z))
            .ToArray();
    }

    private CaptureDestructionNoDeployZone ReadNoDeployZone(
        UObject actor,
        LayerReadContext context,
        SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        var components = context.OwnedBy(actor)
            .Where(component => component.ExportType is "SphereComponent" or "BoxComponent")
            .Select(component => ReadNoDeployVolume(component, transforms))
            .ToArray();

        return new CaptureDestructionNoDeployZone(
            actor.Name,
            actor.ExportType,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            components);
    }

    private CaptureDestructionNoDeployVolume ReadNoDeployVolume(UObject component, SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveComponent(component);
        var isSphere = component.ExportType.Equals("SphereComponent", StringComparison.OrdinalIgnoreCase);
        var baseExtent = isSphere
            ? Vec3.Zero
            : properties.VectorInherited(component, "BoxExtent", new Vec3(32, 32, 32));
        var sphereRadius = isSphere
            ? VolumeTransformMath.ScaleSphereRadius(
                properties.DoubleInherited(component, 32, "SphereRadius"),
                transform.Scale)
            : 0;
        var scaled = isSphere
            ? new Vec3(sphereRadius, sphereRadius, sphereRadius)
            : new Vec3(
                VolumeTransformMath.Multiply(baseExtent.X, transform.Scale.X),
                VolumeTransformMath.Multiply(baseExtent.Y, transform.Scale.Y),
                VolumeTransformMath.Multiply(baseExtent.Z, transform.Scale.Z));
        var radius = isSphere ? sphereRadius : VolumeTransformMath.Size(scaled);

        return new CaptureDestructionNoDeployVolume(
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            radius.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            new CaptureDestructionExtent(scaled.X, scaled.Y, scaled.Z));
    }

    private static CaptureObjectiveSpawnLocation ReadObjectiveSpawnLocation(
        UObject actor,
        SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        return new CaptureObjectiveSpawnLocation(
            actor.Name,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z);
    }

    private static string ReadTeam(string value)
    {
        var team = TextFormatting.EnumToken(value);
        return string.IsNullOrWhiteSpace(team) ? "Neutral" : team.Replace('_', ' ');
    }

    private string GetSeedPointName(UObject actor)
    {
        var graphName = GetGraphNodeName(actor);
        if (graphName.EndsWith(" Main", StringComparison.OrdinalIgnoreCase)) return graphName;

        var separator = graphName.IndexOf('-');
        return separator < 0
            ? TextFormatting.Prettify(graphName)
            : graphName[..(separator + 1)] + TextFormatting.Prettify(graphName[(separator + 1)..]);
    }

    private CaptureObjective ReadMainObjective(UObject actor, int position, SceneTransformResolver transforms)
    {
        var name = GetGraphNodeName(actor);
        var actorTransform = transforms.ResolveActor(actor);
        var sphere = properties.ObjectInherited(actor, "Sphere");
        var sphereTransform = transforms.ResolveComponent(sphere);
        var radius = properties.Double(sphere, 0, "SphereRadius") * MaxAbs(sphereTransform.Scale);

        return new CaptureObjective(
            "Main",
            name,
            name,
            actorTransform.Location.X,
            actorTransform.Location.Y,
            actorTransform.Location.Z,
            [new ObjectiveVolume(
                sphere?.Name ?? "Sphere",
                sphereTransform.Location.X,
                sphereTransform.Location.Y,
                sphereTransform.Location.Z,
                true,
                radius.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                false,
                new ObjectiveExtent(
                    radius,
                    radius,
                    radius,
                    sphereTransform.Rotation.Roll,
                    sphereTransform.Rotation.Pitch,
                    sphereTransform.Rotation.Yaw),
                false)],
            position);
    }

    private CaptureHex ReadHex(UObject actor, SceneTransformResolver transforms)
    {
        var number = properties.IntInherited(actor, 0, "Hex Num");
        var captureZone = properties.ObjectInherited(actor, "SQCaptureZoneTC");
        var flagName = properties.StringInherited(captureZone, "Territory {ID}", "FlagName")
            .Replace("{ID}", number.ToString("00"), StringComparison.OrdinalIgnoreCase);
        var root = properties.ObjectInherited(actor, "RootComponent", "Hex");
        var transform = transforms.ResolveComponent(root);
        var mesh = properties.ObjectInherited(root, "StaticMesh") as UStaticMesh;
        var bounds = mesh?.RenderData?.Bounds;
        var extent = bounds is null
            ? Vec3.Zero
            : new Vec3(
                ScaleFloat(bounds.BoxExtent.X, transform.Scale.X),
                ScaleFloat(bounds.BoxExtent.Y, transform.Scale.Y),
                ScaleFloat(bounds.BoxExtent.Z, transform.Scale.Z));
        var radius = bounds is null ? 0 : ScaleFloat(bounds.SphereRadius, MaxAbs(transform.Scale));

        return new CaptureHex(
            actor.Name,
            number,
            properties.IntInherited(captureZone, 0, "InitialTeam").ToString(),
            flagName,
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            radius,
            new CaptureHexExtent(extent.X, extent.Y, extent.Z));
    }

    private IReadOnlyList<int> ReadAnchorNumbers(UObject? graph, string propertyName) => properties
        .Array(graph, propertyName)
        .Select(properties.ResolveObject)
        .Where(anchor => anchor is not null)
        .Select(anchor => properties.IntInherited(anchor, 0, "Hex Num"))
        .ToArray();

    private static double MaxAbs(Vec3 value) => Math.Max(
        Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));

    private static double ScaleFloat(double value, double scale) => Math.Abs((float)value * (float)scale);

    private IReadOnlyList<CaptureLink> ReadLinks(
        IPropertyHolder? holder,
        string propertyName,
        Func<UObject, string>? getNodeName = null)
        => ReadLinks(properties.Array(holder, propertyName), getNodeName ?? GetGraphNodeName);

    private IReadOnlyList<CaptureLink> ReadLinks(
        IEnumerable<object?> values,
        Func<UObject, string> getNodeName)
    {
        var result = new List<CaptureLink>();
        foreach (var value in values)
        {
            if (value is not IPropertyHolder link) continue;
            var nodeA = properties.Object(link, "NodeA");
            var nodeB = properties.Object(link, "NodeB");
            if (nodeA is null || nodeB is null) continue;
            result.Add(new CaptureLink(
                $"Link{result.Count}",
                GetNodeName(nodeA),
                GetNodeName(nodeB),
                nodeA.GetPathName(),
                nodeB.GetPathName(),
                IsMain(nodeA),
                IsMain(nodeB)));
        }
        return result;

        string GetNodeName(UObject actor) => IsMain(actor) ? GetMainName(actor) : getNodeName(actor);
    }

    private static DirectedGraph BuildDirectedGraph(IReadOnlyList<CaptureLink> links)
    {
        var nodes = new Dictionary<string, AasGraphNode>(StringComparer.OrdinalIgnoreCase);
        var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            AddNode(link.NodeAPath, link.NodeA, link.NodeAIsMain);
            AddNode(link.NodeBPath, link.NodeB, link.NodeBIsMain);
            outgoing[link.NodeAPath].Add(link.NodeBPath);
            incomingCount[link.NodeBPath]++;
        }

        var queue = new Queue<string>(nodes.Keys.Where(path => incomingCount[path] == 0));
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in queue) positions[source] = 1;

        var pointsOrder = new List<string>(nodes.Count);
        while (queue.TryDequeue(out var path))
        {
            pointsOrder.Add(nodes[path].Name);
            foreach (var destination in outgoing[path])
            {
                var position = positions[path] + 1;
                if (positions.TryGetValue(destination, out var existingPosition) && existingPosition != position)
                    throw new InvalidDataException(
                    $"Capture graph node '{nodes[destination].Name}' has no unambiguous position.");

                positions[destination] = position;
                if (--incomingCount[destination] == 0) queue.Enqueue(destination);
            }
        }

        if (pointsOrder.Count != nodes.Count)
            throw new InvalidDataException("Capture graph contains a directed cycle.");

        return new DirectedGraph(
            pointsOrder,
            nodes.Count,
            nodes.Values.Where(node => node.IsMain).Select(node => node.Name).ToArray(),
            positions);

        void AddNode(string path, string name, bool isMain)
        {
            if (!nodes.TryAdd(path, new AasGraphNode(name, isMain))) return;
            outgoing[path] = [];
            incomingCount[path] = 0;
        }
    }

    private static IReadOnlyList<string> BuildPointsOrder(IReadOnlyList<CaptureLink> links)
    {
        if (links.Count == 0) return [];

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var allNodes = new List<string>();
        var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outgoing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            AddNode(link.NodeA);
            AddNode(link.NodeB);
            incoming.Add(link.NodeB);
            outgoing.Add(link.NodeA);
            if (!adjacency.TryGetValue(link.NodeA, out var destinations))
                adjacency[link.NodeA] = destinations = [];
            destinations.Add(link.NodeB);
            adjacency.TryAdd(link.NodeB, []);
        }

        var start = allNodes.FirstOrDefault(node => !incoming.Contains(node));
        var end = allNodes.FirstOrDefault(node => !outgoing.Contains(node));
        if (start is null || end is null) return allNodes;

        var paths = new List<IReadOnlyList<string>>();
        FindPaths(start, end, adjacency, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), paths);
        if (paths.Count == 0) return allNodes;

        paths.Sort((left, right) => StringComparer.Ordinal.Compare(
            string.Join("->", left), string.Join("->", right)));
        var order = new List<string>();
        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            var count = pathIndex < paths.Count - 1 ? path.Count - 1 : path.Count;
            for (var pointIndex = 0; pointIndex < count; pointIndex++) order.Add(path[pointIndex]);
        }
        return order;

        void AddNode(string node)
        {
            if (!allNodes.Contains(node, StringComparer.OrdinalIgnoreCase)) allNodes.Add(node);
        }
    }

    private static void FindPaths(
        string current,
        string target,
        IReadOnlyDictionary<string, List<string>> adjacency,
        List<string> path,
        ISet<string> visited,
        ICollection<IReadOnlyList<string>> result)
    {
        path.Add(current);
        if (current.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(path.ToArray());
        }
        else if (visited.Add(current))
        {
            if (adjacency.TryGetValue(current, out var destinations))
                foreach (var destination in destinations)
                    if (!visited.Contains(destination))
                        FindPaths(destination, target, adjacency, path, visited, result);
            visited.Remove(current);
        }
        path.RemoveAt(path.Count - 1);
    }

    private static IReadOnlyList<string> FindMains(IEnumerable<string> points) => points
        .Where(point => point.EndsWith(" Main", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool IsMain(UObject actor) =>
        actor.ExportType.Equals("BP_CaptureZoneMain_C", StringComparison.OrdinalIgnoreCase);

    private IReadOnlySet<string> FindRaasNodesWithCapturePoints(
        LayerReadContext context,
        IReadOnlyList<UObject> clusters)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var captureZone in context.FindExact("BP_CaptureZone_C"))
        {
            result.Add(captureZone.GetPathName());
            if (FindRaasCluster(captureZone, clusters) is { } cluster)
                result.Add(cluster.GetPathName());
        }
        return result;
    }

    private static IReadOnlyList<CaptureLink> ProjectRaasLinks(
        IReadOnlyList<CaptureLink> links,
        IReadOnlySet<string> nodesWithCapturePoints)
    {
        var nodes = new Dictionary<string, CaptureGraphNode>(StringComparer.OrdinalIgnoreCase);
        var outgoing = links.GroupBy(link => link.NodeAPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            nodes.TryAdd(link.NodeAPath, new CaptureGraphNode(link.NodeAPath, link.NodeA, link.NodeAIsMain));
            nodes.TryAdd(link.NodeBPath, new CaptureGraphNode(link.NodeBPath, link.NodeB, link.NodeBIsMain));
        }

        var result = new List<CaptureLink>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in nodes.Values.Where(IsActive))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.Path };
            var queue = new Queue<CaptureLink>(outgoing.GetValueOrDefault(source.Path) ?? []);
            while (queue.TryDequeue(out var link))
            {
                if (!visited.Add(link.NodeBPath)) continue;
                var destination = nodes[link.NodeBPath];
                if (IsActive(destination))
                {
                    if (emitted.Add($"{source.Path}\0{destination.Path}"))
                        result.Add(new CaptureLink(
                            $"Link{result.Count}",
                            source.Name,
                            destination.Name,
                            source.Path,
                            destination.Path,
                            source.IsMain,
                            destination.IsMain));
                    continue;
                }

                foreach (var next in outgoing.GetValueOrDefault(destination.Path) ?? []) queue.Enqueue(next);
            }
        }
        return result;

        bool IsActive(CaptureGraphNode node) => node.IsMain || nodesWithCapturePoints.Contains(node.Path);
    }

    private string GetCapturePointName(UObject actor)
    {
        var graphName = GetGraphNodeName(actor);
        if (graphName.EndsWith(" Main", StringComparison.OrdinalIgnoreCase)) return graphName;

        var captureZone = properties.ObjectInherited(actor, "SQCaptureZone");
        var flagName = properties.StringInherited(captureZone, string.Empty, "FlagName");
        if (string.IsNullOrWhiteSpace(flagName)) return graphName;

        var separator = graphName.IndexOf('-');
        return separator < 0 ? flagName : graphName[..(separator + 1)] + flagName;
    }

    private string GetMainName(UObject actor)
    {
        var captureZone = properties.ObjectInherited(actor, "SQCaptureZone");
        var initialTeam = properties.IntInherited(captureZone, 0, "InitialTeam");
        return CapturePointNames.MainName(initialTeam, GetGraphNodeName(actor));
    }

    private static UObject? FindExport(LayerReadContext context, string exportType) =>
        context.FindExact(exportType).FirstOrDefault();

    private string GetRaasNodeName(UObject actor, IReadOnlyList<UObject> clusters)
    {
        if (!actor.ExportType.Equals("BP_CaptureZone_C", StringComparison.OrdinalIgnoreCase))
            return GetGraphNodeName(actor);

        var cluster = FindRaasCluster(actor, clusters);
        return cluster is null ? GetGraphNodeName(actor) : GetGraphNodeName(cluster);
    }

    private UObject? FindRaasCluster(UObject actor, IReadOnlyList<UObject> clusters)
    {
        var root = properties.ObjectInherited(actor, "RootComponent", "DefaultSceneRoot");
        var parent = properties.Object(root, "AttachParent");
        if (parent is null) return null;

        var parentPath = parent.GetPathName();
        return clusters.FirstOrDefault(candidate =>
            parentPath.StartsWith(candidate.GetPathName() + ".", StringComparison.OrdinalIgnoreCase));
    }

    private string GetGraphNodeName(UObject actor) =>
        properties.String(actor, actor.Name, "ActorLabel");

    private sealed record DirectedGraph(
        IReadOnlyList<string> PointsOrder,
        int NumberOfPoints,
        IReadOnlyList<string> Mains,
        IReadOnlyDictionary<string, int> PositionsByPath);

    private sealed record AasGraphNode(string Name, bool IsMain);
    private sealed record CaptureGraphNode(string Path, string Name, bool IsMain);
}
