using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public sealed record InstalledContentSource(
    string Id,
    string Name,
    bool IsVanilla,
    bool Enabled,
    string Version,
    string Revision,
    long InstalledSize);

public sealed class ContentVersionService
{
    public InstalledContentSource ReadVanilla(string squadPath)
    {
        var executable = FindSquadExecutable(squadPath);
        if (executable is null)
            return new InstalledContentSource("vanilla", "Vanilla", true, true, string.Empty, string.Empty, 0);

        var info = FileVersionInfo.GetVersionInfo(executable.FullName);
        var version = info.ProductVersion ?? info.FileVersion ?? string.Empty;
        return new InstalledContentSource(
            "vanilla",
            "Vanilla",
            true,
            true,
            version,
            $"{version}:{executable.Length}",
            executable.Length);
    }

    public InstalledContentSource FromMod(ModArchiveProfile mod) => new(
        mod.Id,
        mod.FriendlyName,
        false,
        mod.Enabled,
        mod.Version,
        mod.ContentRevision,
        mod.InstalledSize);

    public string ReadMappingsSignature(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static FileInfo? FindSquadExecutable(string squadPath)
    {
        if (string.IsNullOrWhiteSpace(squadPath)) return null;
        var current = new DirectoryInfo(Path.GetFullPath(squadPath));
        for (var depth = 0; current is not null && depth < 6; depth++, current = current.Parent)
        {
            var path = Path.Combine(current.FullName, "SquadGame", "Binaries", "Win64",
                "SquadGame-Win64-Shipping.exe");
            if (File.Exists(path)) return new FileInfo(path);
        }
        return null;
    }
}
