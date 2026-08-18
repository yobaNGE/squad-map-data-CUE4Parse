using System.Globalization;
using System.IO;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed record WorldGeometry(
    MapCameraActor Camera,
    IReadOnlyList<BorderPoint> Border,
    string MapSize,
    IReadOnlyList<MapTextureCorner> TextureCorners);

internal sealed class WorldGeometryReader(IGameAssetProvider assets)
{
    private readonly UnrealPropertyReader _properties = new(assets);

    private static readonly IReadOnlyDictionary<string, int> CornerIndexes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Zero"] = 0,
            ["One"] = 1,
            ["Two"] = 2,
            ["Three"] = 3
        };

    public WorldGeometry Read(LayerReadContext context)
    {
        var worldSettings = context.WorldSettings
                            ?? throw new InvalidDataException($"World '{context.World.Name}' does not contain SQWorldSettings.");

        var camera = ReadCamera(worldSettings);
        var textureCorners = ReadTextureCorners(worldSettings);
        var border = ReadBorder(context.FindExact("SQMapBoundary"));
        if (border.Count == 0) border = ReadStreamingBorder(context.World);
        if (border.Count == 0) border = ReadTextureCornerFallback(textureCorners);
        return new WorldGeometry(camera, border, FormatMapSize(border, textureCorners), textureCorners);
    }

    private MapCameraActor ReadCamera(IPropertyHolder worldSettings)
    {
        var actor = _properties.Object(worldSettings, "MapCameraLocation")
                    ?? throw new InvalidDataException("SQWorldSettings does not reference MapCameraLocation.");
        var component = _properties.Object(actor, "SceneComponent", "RootComponent")
                        ?? throw new InvalidDataException($"Map camera actor '{actor.Name}' does not have a scene component.");
        var location = _properties.Vector(component, "RelativeLocation");
        var rotation = _properties.Rotation(component, "RelativeRotation");

        return new MapCameraActor(
            actor.Name,
            location.X,
            location.Y,
            location.Z,
            rotation.Roll,
            rotation.Pitch,
            rotation.Yaw);
    }

    private IReadOnlyList<MapTextureCorner> ReadTextureCorners(IPropertyHolder worldSettings)
    {
        var corners = new SortedDictionary<int, MapTextureCorner>();
        foreach (var property in worldSettings.Properties)
        {
            var propertyName = property.Name.Text;
            if (!propertyName.StartsWith("MapTextureCorner", StringComparison.OrdinalIgnoreCase)) continue;

            var actor = _properties.ResolveObject(property.Tag?.GenericValue);
            var component = _properties.Object(actor, "RootComponent", "SceneComponent");
            if (actor is null || component is null) continue;

            var index = ResolveCornerIndex(propertyName, actor.Name);
            var location = _properties.Vector(component, "RelativeLocation");
            corners[index] = new MapTextureCorner(index, location.X, location.Y, location.Z);
        }
        return corners.Values.ToArray();
    }

    private IReadOnlyList<BorderPoint> ReadBorder(IReadOnlyList<UObject> exports)
    {
        if (exports.Count == 0) return [];
        if (exports.Count > 1)
            throw new InvalidDataException("A level contains more than one SQMapBoundary.");

        var boundary = exports[0];
        var spline = _properties.Object(boundary, "XYBoundary", "RootComponent");
        var curves = _properties.Struct(spline, "SplineCurves");
        var position = _properties.Struct(curves, "Position");
        var relativeLocation = _properties.Vector(spline, "RelativeLocation");

        var border = new List<BorderPoint>();
        foreach (var point in _properties.Array(position, "Points").OfType<IPropertyHolder>())
        {
            var location = relativeLocation + _properties.Vector(point, "OutVal");
            border.Add(new BorderPoint(border.Count, location.X, location.Y, location.Z));
        }
        return border;
    }

    private IReadOnlyList<BorderPoint> ReadStreamingBorder(UObject world)
    {
        var visitedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var borders = ReadStreamingLevels(world).Where(border => border.Count > 0).ToArray();
        return borders.Length switch
        {
            0 => [],
            1 => borders[0],
            _ => throw new InvalidDataException($"World '{world.Name}' streams more than one SQMapBoundary.")
        };

        IEnumerable<IReadOnlyList<BorderPoint>> ReadStreamingLevels(UObject currentWorld)
        {
            foreach (var reference in _properties.Array(currentWorld, "StreamingLevels"))
            {
                var streamingLevel = _properties.ResolveObject(reference);
                var worldAssetPath = UnrealPropertyReader.ToStringValue(_properties.Raw(streamingLevel, "WorldAsset"));
                var packagePath = worldAssetPath is null ? null : assets.ResolvePackagePath(worldAssetPath);
                if (packagePath is null || !visitedPackages.Add(packagePath)) continue;

                var streamedWorld = assets.LoadPackageExports(packagePath).FirstOrDefault(export =>
                    export.ExportType.Equals("World", StringComparison.OrdinalIgnoreCase));
                if (streamedWorld is null) continue;

                var context = new LayerReadContext(streamedWorld, _properties);
                yield return ReadBorder(context.FindExact("SQMapBoundary"));
                foreach (var border in ReadStreamingLevels(streamedWorld)) yield return border;
            }
        }
    }

    private static IReadOnlyList<BorderPoint> ReadTextureCornerFallback(
        IReadOnlyList<MapTextureCorner> textureCorners)
    {
        if (textureCorners.Count < 2) return [];
        return textureCorners.Take(2)
            .Select((corner, index) => new BorderPoint(index, corner.LocationX, corner.LocationY, corner.LocationZ))
            .ToArray();
    }

    private static int ResolveCornerIndex(string propertyName, string actorName)
    {
        foreach (var (suffix, index) in CornerIndexes)
        {
            if (propertyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return index;
        }

        var firstDigit = actorName.Length;
        while (firstDigit > 0 && char.IsDigit(actorName[firstDigit - 1])) firstDigit--;
        if (firstDigit < actorName.Length)
            return int.Parse(actorName.AsSpan(firstDigit), CultureInfo.InvariantCulture);

        throw new InvalidDataException($"Cannot determine the index of '{propertyName}'.");
    }

    private static string FormatMapSize(
        IReadOnlyList<BorderPoint> border,
        IReadOnlyList<MapTextureCorner> textureCorners)
    {
        var locations = border.Count > 0
            ? border.Select(point => (point.LocationX, point.LocationY))
            : textureCorners.Select(point => (point.LocationX, point.LocationY));
        var points = locations.ToArray();
        if (points.Length < 2) return string.Empty;

        var width = (points.Max(point => point.LocationX) - points.Min(point => point.LocationX)) / 100_000d;
        var height = (points.Max(point => point.LocationY) - points.Min(point => point.LocationY)) / 100_000d;
        return $"{width.ToString("0.0", CultureInfo.InvariantCulture)}x{height.ToString("0.0", CultureInfo.InvariantCulture)} km";
    }
}
