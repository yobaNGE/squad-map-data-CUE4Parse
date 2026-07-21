using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public sealed class SdkPluginDiscovery
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Saved", "Intermediate", "DerivedDataCache", "Binaries"
    };

    public IReadOnlyList<SdkPluginProfile> Discover(string squadPath)
    {
        var layout = ContentLayoutDetector.Detect(squadPath);
        if (!layout.IsEditorSdk) return [];

        var modsRoot = Path.Combine(layout.Root.FullName, "Plugins", "Mods");
        if (!Directory.Exists(modsRoot)) return [];

        var result = new List<SdkPluginProfile>();
        foreach (var descriptorPath in Directory.EnumerateFiles(modsRoot, "*.uplugin", SearchOption.AllDirectories)
                     .Where(path => !HasIgnoredSegment(Path.GetRelativePath(modsRoot, path))))
        {
            var pluginDirectory = Path.GetDirectoryName(descriptorPath)!;
            var contentDirectory = Path.Combine(pluginDirectory, "Content");
            if (!Directory.Exists(contentDirectory)) continue;

            var descriptor = ReadDescriptor(descriptorPath);
            if (descriptor.CanContainContent == false) continue;

            var pluginName = Path.GetFileNameWithoutExtension(descriptorPath);
            var revision = BuildRevision(descriptorPath, contentDirectory, out var installedSize);
            result.Add(new SdkPluginProfile(
                $"sdk:{pluginName}",
                descriptor.FriendlyName ?? pluginName,
                pluginDirectory)
            {
                Version = descriptor.VersionName ?? string.Empty,
                ContentRevision = revision,
                InstalledSize = installedSize
            });
        }

        return result.OrderBy(plugin => plugin.FriendlyName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static PluginDescriptor ReadDescriptor(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PluginDescriptor>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new PluginDescriptor();
        }
        catch (JsonException)
        {
            return new PluginDescriptor();
        }
    }

    private static string BuildRevision(string descriptorPath, string contentDirectory, out long installedSize)
    {
        var files = new[] { descriptorPath }.Concat(Directory.EnumerateFiles(contentDirectory, "*.*", SearchOption.AllDirectories))
            .Where(IsRelevantFile)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new StringBuilder();
        long size = 0;
        foreach (var path in files)
        {
            var file = new FileInfo(path);
            size += file.Length;
            builder.Append(Path.GetRelativePath(Path.GetDirectoryName(descriptorPath)!, path))
                .Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
        }
        installedSize = size;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    internal static bool IsRelevantFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".uplugin", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".umap", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".uexp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ubulk", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".uptnl", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".tfc", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasIgnoredSegment(string relativePath) => relativePath
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(IgnoredDirectories.Contains);

    private sealed record PluginDescriptor
    {
        public string? FriendlyName { get; init; }
        public string? VersionName { get; init; }
        public bool? CanContainContent { get; init; }
    }
}
