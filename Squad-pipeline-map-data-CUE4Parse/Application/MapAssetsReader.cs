using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed partial class MapAssetsReader(UnrealPropertyReader properties)
{
    public MapAssets Read(LayerReadContext context)
    {
        var transforms = context.Transforms;
        var zoneActors = context.FindExact("Gameplay_TeamZone_C");
        var groupActors = context.FindExact("SQTeamSpawnGroup");
        var zoneNames = BuildDisplayNames(zoneActors, adjustSpawnTokens: false);
        var groupNames = BuildDisplayNames(groupActors, adjustSpawnTokens: true);
        var groupsByPath = groupActors.ToDictionary(
            actor => actor.GetPathName(),
            actor => ReadSpawnGroup(actor, groupNames[actor.GetPathName()], transforms),
            StringComparer.OrdinalIgnoreCase);

        var protectionZones = zoneActors
            .Select(actor => ReadProtectionZone(actor, zoneNames[actor.GetPathName()], context, transforms))
            .ToArray();
        var spawnGroups = groupActors.Select(actor => groupsByPath[actor.GetPathName()]).ToArray();
        var spawnPoints = context.FindExact("SQTeamSpawnPoint")
            .Select(actor => ReadSpawnPoint(actor, groupsByPath, transforms))
            .ToArray();

        return new MapAssets(protectionZones, spawnGroups, spawnPoints);
    }

    private ProtectionZone ReadProtectionZone(
        UObject actor,
        string displayName,
        LayerReadContext context,
        SceneTransformResolver transforms) => new(
        displayName,
        properties.DoubleInherited(actor, 15000, "DeployableLockDistance"),
        properties.IntInherited(actor, 0, "TeamId").ToString(),
        context.OwnedBy(actor)
            .Where(export => IsOwnedBy(export, actor) && IsVolume(export) &&
                             !export.Name.Equals("DummyPresetCollision", StringComparison.OrdinalIgnoreCase))
            .OrderBy(export => export.Name, StringComparer.OrdinalIgnoreCase)
            .Select(component => ReadVolume(component, transforms))
            .ToArray());

    private SpawnGroup ReadSpawnGroup(UObject actor, string displayName, SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        return new SpawnGroup(
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            ReadTeam(actor),
            (int)properties.DoubleInherited(actor, 0, "InitialLifeSpan"),
            true,
            displayName);
    }

    private SpawnPoint ReadSpawnPoint(
        UObject actor,
        IReadOnlyDictionary<string, SpawnGroup> groupsByPath,
        SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveActor(actor);
        var groupActor = properties.ObjectInherited(actor, "Group");
        groupsByPath.TryGetValue(groupActor?.GetPathName() ?? string.Empty, out var group);
        return new SpawnPoint(
            transform.Location.X,
            transform.Location.Y,
            transform.Location.Z,
            group?.Team ?? ReadTeam(actor),
            group?.InitialLifeSpan ?? (int)properties.DoubleInherited(actor, 0, "InitialLifeSpan"),
            true,
            group?.DisplayName ?? AdjustSpawnTokens(PrettifyActorName(groupActor?.Name ?? string.Empty)));
    }

    private MapAssetVolume ReadVolume(UObject component, SceneTransformResolver transforms)
    {
        var transform = transforms.ResolveComponent(component);
        return component.ExportType switch
        {
            "BoxComponent" => ReadBox(component, transform),
            "SphereComponent" => ReadSphere(component, transform),
            "CapsuleComponent" => ReadCapsule(component, transform),
            _ => throw new InvalidOperationException($"Unsupported volume component '{component.ExportType}'.")
        };
    }

    private MapAssetVolume ReadBox(UObject component, SceneTransform transform)
    {
        var baseExtent = properties.VectorInherited(component, "BoxExtent", new Vec3(32, 32, 32));
        var extent = new Vec3(
            VolumeTransformMath.Multiply(baseExtent.X, transform.Scale.X),
            VolumeTransformMath.Multiply(baseExtent.Y, transform.Scale.Y),
            VolumeTransformMath.Multiply(baseExtent.Z, transform.Scale.Z));
        return CreateVolume(component, transform, false, VolumeTransformMath.Size(extent), true, extent, false);
    }

    private MapAssetVolume ReadSphere(UObject component, SceneTransform transform)
    {
        var baseRadius = properties.DoubleInherited(component, 0, "SphereRadius");
        var radius = VolumeTransformMath.ScaleSphereRadius(baseRadius, transform.Scale);
        var extent = new Vec3(radius, radius, radius);
        return CreateVolume(
            component,
            transform,
            true,
            radius,
            false,
            extent,
            false);
    }

    private MapAssetVolume ReadCapsule(UObject component, SceneTransform transform)
    {
        var baseRadius = properties.DoubleInherited(component, 0, "CapsuleRadius");
        var halfHeight = properties.DoubleInherited(component, 0, "CapsuleHalfHeight");
        var extent = new Vec3(
            VolumeTransformMath.Multiply(baseRadius, transform.Scale.X),
            VolumeTransformMath.Multiply(baseRadius, transform.Scale.Y),
            VolumeTransformMath.Multiply(halfHeight, transform.Scale.Z));
        return CreateVolume(
            component,
            transform,
            false,
            Math.Max(extent.X, extent.Y),
            false,
            extent,
            true);
    }

    private static MapAssetVolume CreateVolume(
        UObject component,
        SceneTransform transform,
        bool isSphere,
        double radius,
        bool isBox,
        Vec3 extent,
        bool isCapsule) => new(
        component.Name,
        isSphere,
        radius,
        transform.Location.X,
        transform.Location.Y,
        transform.Location.Z,
        isBox,
        new MapAssetExtent(
            extent.X,
            extent.Y,
            extent.Z,
            VolumeTransformMath.CleanRotation(transform.Rotation.Roll),
            VolumeTransformMath.CleanRotation(transform.Rotation.Pitch),
            VolumeTransformMath.CleanRotation(transform.Rotation.Yaw)),
        isCapsule);

    private string ReadTeam(UObject actor)
    {
        var token = TextFormatting.EnumToken(properties.StringInherited(actor, string.Empty, "Team"));
        if (!string.IsNullOrWhiteSpace(token)) return token.Replace('_', ' ');
        if (actor.Name.Contains("Team1", StringComparison.OrdinalIgnoreCase)) return "Team One";
        if (actor.Name.Contains("Team2", StringComparison.OrdinalIgnoreCase)) return "Team Two";
        return "Neutral";
    }

    private static string PrettifyActorName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = DuplicateSuffixRegex().Replace(value, string.Empty).Replace('_', ' ');
        var result = new StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0 && char.IsUpper(current) &&
                (char.IsLower(text[index - 1]) || char.IsDigit(text[index - 1])))
                result.Append(' ');
            result.Append(current);
        }
        return result.ToString().Trim();
    }

    private static string AdjustSpawnTokens(string value) => value
        .Replace("Spawn Group", "SpawnGroup", StringComparison.Ordinal)
        .Replace("Spawn Point", "SpawnPoint", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> BuildDisplayNames(
        IReadOnlyList<UObject> actors,
        bool adjustSpawnTokens)
    {
        var duplicateCounts = actors
            .GroupBy(actor => BaseActorName(actor.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return actors.ToDictionary(
            actor => actor.GetPathName(),
            actor =>
            {
                var baseName = BaseActorName(actor.Name);
                var displayName = PrettifyActorName(baseName);
                var suffix = DuplicateSuffixRegex().Match(actor.Name);
                if (duplicateCounts[baseName] > 1 && suffix.Success &&
                    int.TryParse(suffix.Value.AsSpan(1), out var duplicateNumber) && duplicateNumber > 0)
                    displayName += duplicateNumber + 1;
                return adjustSpawnTokens ? AdjustSpawnTokens(displayName) : displayName;
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static string BaseActorName(string value) => DuplicateSuffixRegex().Replace(value, string.Empty);

    private static bool IsOwnedBy(UObject export, UObject actor) => export.GetPathName()
        .StartsWith(actor.GetPathName() + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsVolume(UObject export) => export.ExportType is
        "BoxComponent" or "SphereComponent" or "CapsuleComponent";

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex DuplicateSuffixRegex();
}
