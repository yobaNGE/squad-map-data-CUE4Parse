using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Versions;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public sealed partial class WorkshopModDiscovery
{
    public const string SquadWorkshopAppId = "393380";

    public string ResolveWorkshopPath(string squadPath, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return Path.GetFullPath(configuredPath);
        if (string.IsNullOrWhiteSpace(squadPath)) return string.Empty;

        var current = new DirectoryInfo(Path.GetFullPath(squadPath));
        while (current is not null && !current.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            current = current.Parent;

        return current is null
            ? string.Empty
            : Path.Combine(current.FullName, "workshop", "content", SquadWorkshopAppId);
    }

    public IReadOnlyList<ModArchiveProfile> Discover(string workshopPath)
    {
        if (string.IsNullOrWhiteSpace(workshopPath) || !Directory.Exists(workshopPath)) return [];

        var workshopItems = ReadWorkshopItems(workshopPath);
        var result = new List<ModArchiveProfile>();
        foreach (var itemDirectory in Directory.EnumerateDirectories(workshopPath)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var pluginPath = Directory.EnumerateFiles(itemDirectory, "*.uplugin", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            var paksDirectory = Path.Combine(itemDirectory, "Content", "Paks", "Windows");
            if (pluginPath is null || !Directory.Exists(paksDirectory)) continue;

            var id = Path.GetFileName(itemDirectory);
            var version = ReadModVersion(itemDirectory);
            workshopItems.TryGetValue(id, out var workshopItem);
            var revision = BuildArchiveRevision(paksDirectory) ?? workshopItem.Manifest;
            result.Add(new ModArchiveProfile(
                id,
                ReadFriendlyName(pluginPath) ?? Path.GetFileNameWithoutExtension(pluginPath),
                itemDirectory,
                paksDirectory)
            {
                Version = version,
                ContentRevision = revision,
                InstalledSize = workshopItem.Size
            });
        }

        return result;
    }

    private static string ReadModVersion(string itemDirectory)
    {
        var path = Directory.EnumerateFiles(itemDirectory, "*.mi", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (path is null) return string.Empty;

        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || !line[..separator].Trim().Equals("Version", StringComparison.OrdinalIgnoreCase))
                continue;
            return line[(separator + 1)..].Trim();
        }
        return string.Empty;
    }

    private static IReadOnlyDictionary<string, WorkshopItem> ReadWorkshopItems(string workshopPath)
    {
        var workshopDirectory = Directory.GetParent(Path.GetDirectoryName(workshopPath) ?? string.Empty)?.FullName;
        var manifestPath = workshopDirectory is null
            ? null
            : Path.Combine(workshopDirectory, $"appworkshop_{SquadWorkshopAppId}.acf");
        if (manifestPath is null || !File.Exists(manifestPath))
            return new Dictionary<string, WorkshopItem>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, WorkshopItem>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WorkshopItemRegex().Matches(File.ReadAllText(manifestPath)))
        {
            var id = match.Groups["id"].Value;
            _ = long.TryParse(match.Groups["size"].Value, out var size);
            result[id] = new WorkshopItem(match.Groups["manifest"].Value, size);
        }
        return result;
    }

    private static string? BuildArchiveRevision(string paksDirectory)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(paksDirectory, "*.*", SearchOption.AllDirectories)
                     .Where(IsArchiveIndex)
                     .OrderBy(path => Path.GetRelativePath(paksDirectory, path), StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(Path.GetRelativePath(paksDirectory, path)).Append(':')
                .Append(ArchiveIndexHash(path)).Append('\n');
        }

        return builder.Length == 0
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static bool IsArchiveIndex(string path) =>
        path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase);

    private static string ArchiveIndexHash(string path)
    {
        if (path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
        {
            using var pak = new PakFileReader(path, new VersionContainer(EGame.GAME_Squad));
            return pak.Info.IndexHash.ToString();
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? ReadFriendlyName(string pluginPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(pluginPath));
            return document.RootElement.TryGetProperty("FriendlyName", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("\\\"(?<id>\\d+)\\\"\\s*\\{\\s*\\\"size\\\"\\s*\\\"(?<size>\\d+)\\\"\\s*\\\"timeupdated\\\"\\s*\\\"\\d+\\\"\\s*\\\"manifest\\\"\\s*\\\"(?<manifest>\\d+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex WorkshopItemRegex();

    private readonly record struct WorkshopItem(string Manifest, long Size);
}
