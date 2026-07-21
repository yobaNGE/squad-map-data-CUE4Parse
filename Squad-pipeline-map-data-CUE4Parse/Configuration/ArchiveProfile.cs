using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public sealed record AesKeyEntry(string Guid, string Key);
public sealed record ModArchiveProfile(string Id, string FriendlyName, string ItemDirectory, string PaksDirectory)
{
    public bool Enabled { get; init; } = true;
    public string Version { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public long InstalledSize { get; init; }
}

public sealed record ArchiveProfile
{
    public string SquadPath { get; init; } = string.Empty;
    public IReadOnlyList<string> ModDirectories { get; init; } = [];
    public IReadOnlyList<ModArchiveProfile> Mods { get; init; } = [];
    public string? WorkshopPath { get; init; }
    public string? MappingsPath { get; init; }
    public string OutputDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "output");
    public int ExportParallelism { get; init; } = 2;

    [JsonIgnore]
    public IReadOnlyList<AesKeyEntry> AesKeys { get; init; } = [];

    [JsonIgnore]
    public bool ReadScriptData { get; init; }

    public DirectoryInfo ResolvePaksDirectory()
    {
        var selected = new DirectoryInfo(SquadPath);
        if (selected.Name.Equals("Paks", StringComparison.OrdinalIgnoreCase))
            return selected;

        var candidates = new[]
        {
            Path.Combine(selected.FullName, "SquadGame", "Content", "Paks"),
            Path.Combine(selected.FullName, "Content", "Paks")
        };
        return new DirectoryInfo(candidates.FirstOrDefault(Directory.Exists) ?? selected.FullName);
    }

    [JsonIgnore]
    public IReadOnlyList<string> EffectiveModDirectories => Mods.Count > 0
        ? Mods.Where(mod => mod.Enabled).Select(mod => mod.PaksDirectory).ToArray()
        : ModDirectories;
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ProfileStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadPipeline");
        _path = Path.Combine(directory, "profile.json");
    }

    public ArchiveProfile Load()
    {
        if (!File.Exists(_path)) return new ArchiveProfile();
        try
        {
            return JsonSerializer.Deserialize<ArchiveProfile>(File.ReadAllText(_path), JsonOptions)
                   ?? new ArchiveProfile();
        }
        catch (JsonException)
        {
            return new ArchiveProfile();
        }
    }

    public void Save(ArchiveProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(profile, JsonOptions));
    }
}
