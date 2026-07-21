using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class LevelExportIndex
{
    private readonly Package? _package;
    private readonly IReadOnlyList<UObject> _fallbackExports;
    private readonly int[] _actorIndexes;
    private readonly int[] _levelIndexes;
    private readonly int[] _rootByExport;
    private readonly Dictionary<UObject, int> _loadedIndexes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(IPackage? Owner, int Index), IReadOnlySet<string>> _classHierarchies = [];

    public LevelExportIndex(UObject world)
    {
        UObject? directWorldSettings = null;
        if (TryResolvePersistentLevel(world, out var package, out var level))
        {
            level.WorldSettings.TryLoad(out directWorldSettings);
            _package = package;
            _fallbackExports = [];

            var actorIndexes = level.Actors
                .Where(index => index is { IsExport: true } && ReferenceEquals(index.Owner, package))
                .Select(index => index!.Index - 1)
                .Where(index => index >= 0 && index < package.ExportMap.Length)
                .ToHashSet();
            var roots = new HashSet<int>(actorIndexes);
            if (level.WorldSettings is { IsExport: true }
                && ReferenceEquals(level.WorldSettings.Owner, package))
                roots.Add(level.WorldSettings.Index - 1);

            _rootByExport = BuildOwnership(package, roots);
            _actorIndexes = Enumerable.Range(0, package.ExportMap.Length)
                .Where(actorIndexes.Contains)
                .ToArray();
            _levelIndexes = Enumerable.Range(0, package.ExportMap.Length)
                .Where(index => _rootByExport[index] >= 0)
                .ToArray();

            WorldSettings = level.WorldSettings is { IsExport: true }
                            && ReferenceEquals(level.WorldSettings.Owner, package)
                ? Load(level.WorldSettings.Index - 1)
                : directWorldSettings;
            return;
        }

        _fallbackExports = world.Owner?.GetExports().ToArray() ?? [];
        _actorIndexes = [];
        _levelIndexes = [];
        _rootByExport = [];
        WorldSettings = directWorldSettings ?? _fallbackExports.FirstOrDefault(export =>
            export.ExportType.Equals("SQWorldSettings", StringComparison.OrdinalIgnoreCase));
    }

    public UObject? WorldSettings { get; }

    public IReadOnlyList<UObject> FindExact(params string[] exportTypes)
    {
        if (_package is null)
            return _fallbackExports.Where(export => exportTypes.Contains(
                export.ExportType, StringComparer.OrdinalIgnoreCase)).ToArray();

        var types = exportTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _levelIndexes
            .Where(index => types.Contains(_package.ExportMap[index].ClassName))
            .Select(Load)
            .ToArray();
    }

    public IReadOnlyList<UObject> FindActorsDerivedFrom(params string[] typeNames)
    {
        var types = typeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_package is null)
            return _fallbackExports.Where(export => IsType(export, types)).ToArray();

        return _actorIndexes
            .Where(index => ClassHierarchy(_package.ExportMap[index].ClassIndex).Overlaps(types))
            .Select(Load)
            .ToArray();
    }

    public IReadOnlyList<UObject> OwnedBy(UObject actor)
    {
        if (_package is null)
        {
            var prefix = actor.GetPathName() + ".";
            return _fallbackExports.Where(export => export.GetPathName().StartsWith(
                prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        if (!_loadedIndexes.TryGetValue(actor, out var actorIndex)) return [];
        return _levelIndexes
            .Where(index => index != actorIndex && _rootByExport[index] == actorIndex)
            .Select(Load)
            .ToArray();
    }

    private UObject Load(int index)
    {
        var export = _package!.ExportsLazy[index].Value;
        _loadedIndexes[export] = index;
        return export;
    }

    private IReadOnlySet<string> ClassHierarchy(FPackageIndex classIndex)
    {
        var key = (classIndex.Owner, classIndex.Index);
        if (_classHierarchies.TryGetValue(key, out var cached)) return cached;

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ResolvedObject? current = classIndex.Owner?.ResolvePackageIndex(classIndex);
        while (current is not null)
        {
            var name = current.Name.Text;
            if (!visited.Add(current.GetPathName())) break;
            result.Add(name);
            current = current.Super;
        }
        return _classHierarchies[key] = result;
    }

    private static bool IsType(UObject export, IReadOnlySet<string> typeNames)
    {
        if (typeNames.Contains(export.ExportType)) return true;
        ResolvedObject? current = export.Class;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && visited.Add(current.GetPathName()))
        {
            if (typeNames.Contains(current.Name.Text)) return true;
            current = current.Super;
        }
        return false;
    }

    private static bool TryResolvePersistentLevel(UObject packageMember, out Package package, out ULevel level)
    {
        package = null!;
        level = null!;
        if (packageMember.Owner is not Package owner) return false;

        if (packageMember is UWorld directWorld
            && directWorld.PersistentLevel.TryLoad<ULevel>(out var directLevel)
            && directLevel is not null
            && ReferenceEquals(directLevel.Owner, owner))
        {
            package = owner;
            level = directLevel;
            return true;
        }

        for (var index = 0; index < owner.ExportMap.Length; index++)
        {
            if (!owner.ExportMap[index].ClassName.Equals("World", StringComparison.OrdinalIgnoreCase)) continue;
            if (owner.ExportsLazy[index].Value is not UWorld candidate) continue;
            if (!candidate.PersistentLevel.TryLoad<ULevel>(out var candidateLevel)
                || candidateLevel is null
                || !ReferenceEquals(candidateLevel.Owner, owner)) continue;
            package = owner;
            level = candidateLevel;
            return true;
        }
        return false;
    }

    private static int[] BuildOwnership(Package package, IReadOnlySet<int> roots)
    {
        const int Unknown = -2;
        const int Resolving = -3;
        var ownership = Enumerable.Repeat(Unknown, package.ExportMap.Length).ToArray();
        foreach (var root in roots)
            if (root >= 0 && root < ownership.Length)
                ownership[root] = root;

        for (var index = 0; index < ownership.Length; index++) Resolve(index);
        return ownership;

        int Resolve(int index)
        {
            if (ownership[index] != Unknown) return ownership[index] == Resolving ? -1 : ownership[index];
            ownership[index] = Resolving;
            var outer = package.ExportMap[index].OuterIndex;
            var root = outer is { IsExport: true }
                       && ReferenceEquals(outer.Owner, package)
                       && outer.Index - 1 >= 0
                       && outer.Index - 1 < ownership.Length
                ? Resolve(outer.Index - 1)
                : -1;
            return ownership[index] = root;
        }
    }
}
