using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.AssetRegistry.Objects;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using Squad_pipeline_map_data_CUE4Parse.Configuration;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

public sealed class GameAssetProvider(
    ArchiveProfile profile,
    IGameAssetProvider? primaryAssetFallback = null) : IGameAssetProvider
{
    private readonly ConcurrentDictionary<string, WeakReference<UObject>> _objects =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, PrimaryAssetReference>>> _vanillaPrimaryAssets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scriptDataSync = new();
    private readonly object _registryIndexSync = new();
    private DefaultFileProvider? _provider;
    private IReadOnlyList<FAssetData>? _assetRegistryAssets;
    private PrimaryAssetRegistryIndex? _primaryAssetRegistry;
    private IReadOnlyCollection<string> _packagePaths = [];
    private IReadOnlyDictionary<string, string> _packagesByAssetName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ArchiveProfile Profile { get; } = profile;

    public IReadOnlyCollection<string> PackagePaths => _packagePaths;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paks = Profile.ResolvePaksDirectory();
        if (!paks.Exists)
            throw new DirectoryNotFoundException($"Squad Paks directory was not found: {paks.FullName}");

        var mods = Profile.EffectiveModDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new DirectoryInfo(path))
            .ToArray();

        var version = new VersionContainer(EGame.GAME_Squad);
        var provider = new DefaultFileProvider(
            paks,
            mods,
            SearchOption.TopDirectoryOnly,
            version,
            StringComparer.OrdinalIgnoreCase);
        provider.ReadScriptData = Profile.ReadScriptData;

        if (!string.IsNullOrWhiteSpace(Profile.MappingsPath))
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(Profile.MappingsPath);

        provider.Initialize();
        provider.Mount();

        foreach (var entry in Profile.AesKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guid = string.IsNullOrWhiteSpace(entry.Guid) ? new FGuid() : new FGuid(entry.Guid);
            provider.SubmitKey(guid, new FAesKey(entry.Key));
        }

        provider.PostMount();
        provider.LoadVirtualPaths();
        _provider = provider;
        _packagePaths = provider.Files.Keys.ToArray();
        _packagesByAssetName = _packagePaths
            .Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }, cancellationToken);

    public IReadOnlyList<UObject> LoadPackageExports(string packagePath)
    {
        var provider = RequireProvider();
        return provider.LoadPackage(packagePath).GetExports().ToArray();
    }

    public IReadOnlyList<UObject> LoadPackageExportsWithScriptData(string packagePath)
    {
        var provider = RequireProvider();
        lock (_scriptDataSync)
        {
            var previous = provider.ReadScriptData;
            provider.ReadScriptData = true;
            try
            {
                return provider.LoadPackage(packagePath).GetExports().ToArray();
            }
            finally
            {
                provider.ReadScriptData = previous;
            }
        }
    }

    public UObject? LoadObject(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath)) return null;
        if (_objects.TryGetValue(objectPath, out var reference) && reference.TryGetTarget(out var cached))
            return cached;

        var provider = RequireProvider();
        var loaded = provider.SafeLoadPackageObject(objectPath) ?? LoadObjectFromMountedPackage(objectPath);
        if (loaded is not null) _objects[objectPath] = new WeakReference<UObject>(loaded);
        return loaded;
    }

    public UObject? LoadPrimaryAsset(string primaryAssetType, string primaryAssetName)
    {
        var registered = GetPrimaryAssetRegistry().ByTypeAndName
            .GetValueOrDefault(primaryAssetType)?
            .GetValueOrDefault(primaryAssetName);
        if (registered is not null)
            return LoadObject(registered.ObjectPath);

        // Unreal's default primary asset name is the asset object name. Assets that override it
        // are resolved above from the cooked AssetRegistry tags.
        _packagesByAssetName.TryGetValue(primaryAssetName, out var packagePath);
        var defaultNamedAsset = packagePath is null
            ? null
            : LoadPackageExports(packagePath).FirstOrDefault(export =>
                export.Name.Equals(primaryAssetName, StringComparison.OrdinalIgnoreCase)
                && export.ExportType.Equals(primaryAssetType, StringComparison.OrdinalIgnoreCase));
        if (defaultNamedAsset is not null) return defaultNamedAsset;

        var fallbackReference = primaryAssetFallback?.GetPrimaryAssets(primaryAssetType)
            .FirstOrDefault(asset => asset.Name.Equals(primaryAssetName, StringComparison.OrdinalIgnoreCase));
        if (fallbackReference is not null)
        {
            // Resolve the Vanilla identity through this provider so a mod override at the same
            // object path remains scoped to the active overlay.
            var overlayObject = LoadObject(fallbackReference.ObjectPath);
            if (overlayObject is not null) return overlayObject;
        }

        var vanillaReference = GetVanillaPrimaryAssets(primaryAssetType).GetValueOrDefault(primaryAssetName);
        return vanillaReference is not null
            ? LoadObject(vanillaReference.ObjectPath)
            : primaryAssetFallback?.LoadPrimaryAsset(primaryAssetType, primaryAssetName);
    }

    public IReadOnlyList<PrimaryAssetReference> GetPrimaryAssets(string primaryAssetType)
    {
        var local = GetPrimaryAssetRegistry().ByType.GetValueOrDefault(primaryAssetType) ?? [];

        if (primaryAssetFallback is null)
            return local.OrderBy(asset => asset.ObjectPath, StringComparer.OrdinalIgnoreCase).ToArray();

        return local.Concat(primaryAssetFallback.GetPrimaryAssets(primaryAssetType))
            .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(asset => asset.ObjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? ResolvePackagePath(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath)) return null;
        var dot = objectPath.IndexOf('.');
        var virtualPackagePath = (dot >= 0 ? objectPath[..dot] : objectPath).TrimStart('/');
        var slash = virtualPackagePath.IndexOf('/');
        if (slash < 0) return null;

        var mountPoint = virtualPackagePath[..slash];
        var relativePath = virtualPackagePath[(slash + 1)..];
        var mountedSuffix = mountPoint.Equals("Game", StringComparison.OrdinalIgnoreCase)
            ? $"SquadGame/Content/{relativePath}"
            : $"{mountPoint}/Content/{relativePath}";
        return PackagePaths.FirstOrDefault(path =>
            (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            && Path.ChangeExtension(path, null)?.EndsWith(mountedSuffix, StringComparison.OrdinalIgnoreCase) == true);
    }

    public ContentSource GetContentSource(string packagePath)
    {
        var provider = RequireProvider();
        if (!provider.Files.TryGetValue(packagePath, out var file))
            return new ContentSource("vanilla", "Vanilla", true);

        var archivePath = file switch
        {
            VfsEntry entry => entry.Vfs.Path,
            OsGameFile looseFile => looseFile.ActualFile.FullName,
            _ => string.Empty
        };

        foreach (var mod in Profile.Mods)
            if (IsWithin(archivePath, mod.PaksDirectory))
                return new ContentSource(mod.Id, mod.FriendlyName, false);

        foreach (var directory in Profile.EffectiveModDirectories)
            if (IsWithin(archivePath, directory))
                return new ContentSource(directory, Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), false);

        return new ContentSource("vanilla", "Vanilla", true);
    }

    public IReadOnlyList<FAssetData> ReadAssetRegistryAssets()
    {
        if (_assetRegistryAssets is not null) return _assetRegistryAssets;

        var assets = new List<FAssetData>();
        foreach (var file in RequireProvider().Files.Values.Where(file =>
                     file.Name.Equals("AssetRegistry.bin", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = file.CreateReader();
            assets.AddRange(new FAssetRegistryState(reader).PreallocatedAssetDataBuffers);
        }

        return _assetRegistryAssets = assets;
    }

    private PrimaryAssetRegistryIndex GetPrimaryAssetRegistry()
    {
        if (_primaryAssetRegistry is not null) return _primaryAssetRegistry;
        lock (_registryIndexSync)
        {
            if (_primaryAssetRegistry is not null) return _primaryAssetRegistry;
            var byTypeAndPath = new Dictionary<string, Dictionary<string, PrimaryAssetReference>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var asset in ReadAssetRegistryAssets())
            {
                var type = RegistryTag(asset, "PrimaryAssetType");
                if (string.IsNullOrWhiteSpace(type)) continue;
                var name = RegistryTag(asset, "PrimaryAssetName") ?? asset.AssetName.Text;
                if (!byTypeAndPath.TryGetValue(type, out var assets))
                    byTypeAndPath[type] = assets = new Dictionary<string, PrimaryAssetReference>(
                        StringComparer.OrdinalIgnoreCase);
                assets[asset.ObjectPath] = new PrimaryAssetReference(asset.ObjectPath, name);
            }

            var byType = byTypeAndPath.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<PrimaryAssetReference>)entry.Value.Values.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var byTypeAndName = byType.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyDictionary<string, PrimaryAssetReference>)entry.Value
                    .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            _assetRegistryAssets = null;
            return _primaryAssetRegistry = new PrimaryAssetRegistryIndex(byType, byTypeAndName);
        }
    }

    private IReadOnlyDictionary<string, PrimaryAssetReference> GetVanillaPrimaryAssets(string primaryAssetType) =>
        _vanillaPrimaryAssets.GetOrAdd(
            primaryAssetType,
            type => new Lazy<IReadOnlyDictionary<string, PrimaryAssetReference>>(
                () => BuildVanillaPrimaryAssetIndex(type),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private IReadOnlyDictionary<string, PrimaryAssetReference> BuildVanillaPrimaryAssetIndex(string primaryAssetType)
    {
        var root = primaryAssetType switch
        {
            "BP_SQFaction_C" => "SquadGame/Content/Settings/Factions/",
            _ => null
        };
        if (root is null) return new Dictionary<string, PrimaryAssetReference>(StringComparer.OrdinalIgnoreCase);

        var properties = new UnrealPropertyReader(this);
        var result = new Dictionary<string, PrimaryAssetReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var packagePath in PackagePaths.Where(path =>
                     path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                     && path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var export in LoadPackageExports(packagePath).Where(export =>
                         export.ExportType.Equals(primaryAssetType, StringComparison.OrdinalIgnoreCase)))
            {
                var data = properties.Struct(export, "Data");
                var primaryAssetName = properties.String(data, string.Empty, "RowName");
                if (!string.IsNullOrWhiteSpace(primaryAssetName)
                    && !primaryAssetName.Equals("None", StringComparison.OrdinalIgnoreCase))
                    result[primaryAssetName] = new PrimaryAssetReference(export.GetPathName(), primaryAssetName);
            }
        }
        return result;
    }

    private static bool HasRegistryTag(FAssetData asset, string key, string value) =>
        asset.TagsAndValues.Any(tag => tag.Key.Text.Equals(key, StringComparison.OrdinalIgnoreCase)
                                      && tag.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static string? RegistryTag(FAssetData asset, string key) =>
        asset.TagsAndValues.FirstOrDefault(tag =>
            tag.Key.Text.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private UObject? LoadObjectFromMountedPackage(string objectPath)
    {
        var packagePath = ResolvePackagePath(objectPath);
        if (packagePath is null) return null;

        var dot = objectPath.IndexOf('.');
        var virtualPackagePath = (dot >= 0 ? objectPath[..dot] : objectPath).TrimStart('/');
        var objectName = dot >= 0 ? objectPath[(dot + 1)..] : Path.GetFileName(virtualPackagePath);
        return LoadPackageExports(packagePath).FirstOrDefault(export =>
            export.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase));
    }

    public string CreateArchiveFingerprint()
    {
        var roots = new[] { Profile.ResolvePaksDirectory().FullName }.Concat(Profile.EffectiveModDirectories);
        var builder = new StringBuilder("squad-pipeline-v1\n");
        foreach (var root in roots.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(path => path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
                                        || path.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                builder.Append(info.FullName).Append('|').Append(info.Length).Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }

        if (File.Exists(Profile.MappingsPath))
        {
            var mappings = new FileInfo(Profile.MappingsPath);
            builder.Append(mappings.FullName).Append('|').Append(mappings.Length).Append('|')
                .Append(mappings.LastWriteTimeUtc.Ticks);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private DefaultFileProvider RequireProvider() =>
        _provider ?? throw new InvalidOperationException("The game asset provider is not initialized.");

    private static bool IsWithin(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _objects.Clear();
        _vanillaPrimaryAssets.Clear();
        _assetRegistryAssets = null;
        _primaryAssetRegistry = null;
        _packagePaths = [];
        _packagesByAssetName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _provider?.Dispose();
        _provider = null;
    }

    private sealed record PrimaryAssetRegistryIndex(
        IReadOnlyDictionary<string, IReadOnlyList<PrimaryAssetReference>> ByType,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, PrimaryAssetReference>> ByTypeAndName);
}
