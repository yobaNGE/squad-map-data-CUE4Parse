using System.IO;
using CUE4Parse.UE4.Objects.UObject;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class UnrealEnumDisplayNameIndex
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _names;
    private readonly Lazy<string> _defaultValue;

    public UnrealEnumDisplayNameIndex(IGameAssetProvider assets, string objectPath)
    {
        _names = new Lazy<IReadOnlyDictionary<string, string>>(
            () => Build(assets, objectPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _defaultValue = new Lazy<string>(
            () => ReadDefaultValue(assets, objectPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string DefaultValue => _defaultValue.Value;

    public string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        var key = separator >= 0 ? value[(separator + 2)..] : value;
        return _names.Value.TryGetValue(key, out var displayName)
            ? displayName
            : key.Replace('_', ' ');
    }

    private static IReadOnlyDictionary<string, string> Build(IGameAssetProvider assets, string objectPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var enumAsset = assets.LoadObject(objectPath);
        var properties = new UnrealPropertyReader(assets);
        foreach (var entry in properties.Map(enumAsset, "DisplayNameMap"))
        {
            var key = UnrealPropertyReader.ToStringValue(entry.Key);
            var value = UnrealPropertyReader.ToStringValue(entry.Value);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value)) result[key] = value;
        }
        return result;
    }

    private static string ReadDefaultValue(IGameAssetProvider assets, string objectPath)
    {
        var enumAsset = assets.LoadObject(objectPath) as UEnum
                        ?? throw new InvalidDataException($"Enum '{objectPath}' was not found.");
        var entry = enumAsset.Names.FirstOrDefault(item => item.Item2 == 0);
        return !entry.Item1.IsNone
            ? entry.Item1.Text
            : throw new InvalidDataException($"Enum '{objectPath}' has no zero value.");
    }
}
