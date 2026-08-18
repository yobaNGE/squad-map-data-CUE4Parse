using System.IO;
using System.Text.Json;

namespace Squad_pipeline_map_data_CUE4Parse.Configuration;

public sealed record LayerSelectionPreset(int Format, IReadOnlyList<LayerSelectionPresetItem> Layers)
{
    public const int CurrentFormat = 1;
}

public sealed record LayerSelectionPresetItem(string SourceId, string GameplayPackagePath, string GameplayObjectName);

public sealed class LayerSelectionPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public void Save(string path, LayerSelectionPreset preset) =>
        File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOptions));

    public LayerSelectionPreset Load(string path)
    {
        var preset = JsonSerializer.Deserialize<LayerSelectionPreset>(File.ReadAllText(path), JsonOptions)
                     ?? throw new InvalidDataException("Selection file is empty.");
        if (preset.Format != LayerSelectionPreset.CurrentFormat)
            throw new InvalidDataException($"Unsupported selection file format: {preset.Format}.");
        return preset;
    }
}
