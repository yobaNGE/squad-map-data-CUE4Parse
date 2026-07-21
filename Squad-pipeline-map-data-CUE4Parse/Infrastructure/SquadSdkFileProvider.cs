using System.IO;
using System.Runtime.CompilerServices;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using Squad_pipeline_map_data_CUE4Parse.Configuration;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

internal sealed class SquadSdkFileProvider : DefaultFileProvider
{
    private readonly ContentLayout _layout;
    private readonly IReadOnlyList<DirectoryInfo> _modPlugins;

    public SquadSdkFileProvider(
        ContentLayout layout,
        IReadOnlyList<DirectoryInfo> modPlugins,
        VersionContainer versions,
        StringComparer pathComparer)
        : base(layout.Root, SearchOption.TopDirectoryOnly, versions, pathComparer)
    {
        _layout = layout;
        _modPlugins = modPlugins;
    }

    public override void Initialize()
    {
        if (!_layout.Root.Exists)
            throw new DirectoryNotFoundException($"Squad SDK directory was not found: {_layout.Root.FullName}");

        var files = new Dictionary<string, GameFile>(PathComparer);
        AddFile(files, new FileInfo(Path.Combine(_layout.Root.FullName, "SquadGame.uproject")), "SquadGame/SquadGame.uproject");
        AddContentTree(files, _layout.ContentDirectory, "SquadGame/Content");

        var pluginsRoot = new DirectoryInfo(Path.Combine(_layout.Root.FullName, "Plugins"));
        if (pluginsRoot.Exists)
        {
            foreach (var descriptor in pluginsRoot.EnumerateFiles("*.uplugin", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(pluginsRoot.FullName, descriptor.FullName);
                if (SdkPluginDiscovery.HasIgnoredSegment(relative) || IsSdkMod(relative)) continue;
                AddPlugin(files, pluginsRoot, descriptor);
            }
        }

        foreach (var pluginDirectory in _modPlugins.Where(directory => directory.Exists))
        {
            var descriptor = pluginDirectory.EnumerateFiles("*.uplugin", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (descriptor is not null && pluginsRoot.Exists && IsWithin(descriptor.FullName, pluginsRoot.FullName))
                AddPlugin(files, pluginsRoot, descriptor);
        }

        Files.AddFiles(files);
        LooseFileCount = files.Values.Count(file => file.IsUePackage);
    }

    public override IPackage LoadPackage(GameFile file)
    {
        if (file is not MountedOsGameFile) return base.LoadPackage(file);
        if (!file.IsUePackage) throw new ArgumentException("cannot load non-UE package", nameof(file));

        Files.FindPayloads(file, out var uexp, out var ubulks, out var uptnls);
        var uasset = file.CreateReader();
        var lazyUbulk = ubulks.Count > 0
            ? new Func<FByteBulkDataHeader?, FArchive?>(header => ubulks[0].SafeCreateReader(header))
            : null;
        var lazyUptnl = uptnls.Count > 0
            ? new Func<FByteBulkDataHeader?, FArchive?>(header => uptnls[0].SafeCreateReader(header))
            : null;
        return new Package(uasset, uexp?.CreateReader(), lazyUbulk, lazyUptnl, this, UseLazyPackageSerialization);
    }

    private void AddPlugin(Dictionary<string, GameFile> files, DirectoryInfo pluginsRoot, FileInfo descriptor)
    {
        var descriptorRelative = Path.GetRelativePath(pluginsRoot.FullName, descriptor.FullName).Replace('\\', '/');
        AddFile(files, descriptor, $"SquadGame/Plugins/{descriptorRelative}");

        var contentDirectory = new DirectoryInfo(Path.Combine(descriptor.DirectoryName!, "Content"));
        if (!contentDirectory.Exists) return;

        var pluginRelative = Path.GetRelativePath(pluginsRoot.FullName, descriptor.DirectoryName!).Replace('\\', '/');
        AddContentTree(files, contentDirectory, $"SquadGame/Plugins/{pluginRelative}/Content");
    }

    private void AddContentTree(Dictionary<string, GameFile> files, DirectoryInfo physicalRoot, string virtualRoot)
    {
        if (!physicalRoot.Exists) return;
        foreach (var file in physicalRoot.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(physicalRoot.FullName, file.FullName);
            if (SdkPluginDiscovery.HasIgnoredSegment(relative) || !IsKnownFile(file)) continue;
            AddFile(files, file, $"{virtualRoot}/{relative.Replace('\\', '/')}");
        }
    }

    private void AddFile(Dictionary<string, GameFile> files, FileInfo file, string virtualPath)
    {
        if (!file.Exists) return;
        if (file.Extension.Equals(".tfc", StringComparison.OrdinalIgnoreCase))
            RegisterTextureCache(file);

        var gameFile = new MountedOsGameFile(file, virtualPath, Versions);
        files[gameFile.Path] = gameFile;
    }

    private static bool IsKnownFile(FileInfo file)
    {
        var extension = file.Extension.TrimStart('.').ToUpperInvariant();
        return GameFile.UeKnownExtensionsSet.Contains(extension);
    }

    private static bool IsSdkMod(string relativePluginPath) => relativePluginPath
        .Replace('\\', '/')
        .StartsWith("Mods/", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MountedOsGameFile(FileInfo file, string virtualPath, VersionContainer versions)
        : VersionedGameFile(virtualPath.Replace('\\', '/').TrimStart('/'), file.Length, versions)
    {
        public FileInfo ActualFile { get; } = file;

        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override byte[] Read(FByteBulkDataHeader? header = null)
        {
            if (header is null) return File.ReadAllBytes(ActualFile.FullName);

            using var stream = ActualFile.OpenRead();
            stream.Seek(header.Value.OffsetInFile, SeekOrigin.Begin);
            var buffer = new byte[header.Value.SizeOnDisk];
            stream.ReadExactly(buffer);
            return buffer;
        }
    }
}
