using System.IO;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

public sealed class LevelDisplayNameIndex
{
    private readonly IGameAssetProvider _assets;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _names;

    public LevelDisplayNameIndex(IGameAssetProvider assets)
    {
        _assets = assets;
        _names = new Lazy<IReadOnlyDictionary<string, string>>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string GetDisplayName(string levelId)
    {
        if (_names.Value.TryGetValue(levelId, out var displayName)) return displayName;
        throw new InvalidDataException($"SQLevelEntry was not found for LevelId '{levelId}'.");
    }

    private IReadOnlyDictionary<string, string> Build()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packagePath in _assets.PackagePaths.Where(IsLevelTablePackage))
        {
            foreach (var table in _assets.LoadPackageExports(packagePath).OfType<UDataTable>())
            {
                if (!string.Equals(table.RowStructName, "SQLevelEntry", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var row in table.RowMap)
                {
                    var displayName = ReadDisplayName(row.Value);
                    if (!string.IsNullOrWhiteSpace(displayName)) result[row.Key.Text] = displayName;
                }
            }
        }
        return result;
    }

    private static bool IsLevelTablePackage(string path)
    {
        if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) return false;
        var fileName = Path.GetFileNameWithoutExtension(path).Replace("_", string.Empty);
        return fileName.Contains("LevelTable", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ReadDisplayName(CUE4Parse.UE4.Assets.Exports.IPropertyHolder row)
    {
        var property = row.Properties.FirstOrDefault(candidate =>
            candidate.Name.Text.StartsWith("DisplayName", StringComparison.OrdinalIgnoreCase));
        return UnrealPropertyReader.ToStringValue(property?.Tag?.GenericValue);
    }
}
