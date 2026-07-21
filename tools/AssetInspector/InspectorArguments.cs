using Squad_pipeline_map_data_CUE4Parse.Configuration;

namespace AssetInspector;

internal sealed record InspectorArguments(
    string SquadPath,
    string MappingsPath,
    string Command,
    string Query,
    int Depth,
    int Limit,
    string? ExportType,
    string? MountPoint,
    string? LevelId,
    string SourceId,
    string? OutputPath)
{
    public static InspectorArguments Parse(string[] args)
    {
        var values = new List<string>();
        string? squadPath = Environment.GetEnvironmentVariable("SQUAD_PATH");
        string? mappingsPath = Environment.GetEnvironmentVariable("SQUAD_MAPPINGS");
        string? outputPath = null;
        string? exportType = null;
        string? mountPoint = null;
        string? levelId = null;
        var sourceId = "all";
        var depth = 2;
        var limit = 100;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--squad": squadPath = args[++index]; break;
                case "--mappings": mappingsPath = args[++index]; break;
                case "--depth": depth = int.Parse(args[++index]); break;
                case "--limit": limit = int.Parse(args[++index]); break;
                case "--type": exportType = args[++index]; break;
                case "--mount": mountPoint = args[++index]; break;
                case "--level": levelId = args[++index]; break;
                case "--source": sourceId = args[++index]; break;
                case "--output": outputPath = args[++index]; break;
                default: values.Add(args[index]); break;
            }
        }

        if (string.IsNullOrWhiteSpace(squadPath) || values.Count < 2)
            throw new ArgumentException(Usage);
        var mappingsCommand = values[0].Equals("schema", StringComparison.OrdinalIgnoreCase)
                              || values[0].Equals("schema-find", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(mappingsPath)
            && (!ContentLayoutDetector.Detect(squadPath).IsEditorSdk || mappingsCommand))
            throw new ArgumentException(Usage);

        return new InspectorArguments(
            squadPath,
            mappingsPath ?? string.Empty,
            values[0].ToLowerInvariant(),
            values[1],
            Math.Max(0, depth),
            Math.Max(1, limit),
            exportType,
            mountPoint,
            levelId,
            sourceId,
            outputPath);
    }

    public const string Usage = """
        AssetInspector usage:
          AssetInspector --squad <path> [--mappings <usmap>] find <text> [--limit 100]
          AssetInspector --squad <path> [--mappings <usmap>] inspect <package-or-object> [--type ExportType] [--depth 2] [--limit 100] [--output file]
          AssetInspector --squad <path> [--mappings <usmap>] inspect-script <package> [--type Function] [--depth 2] [--limit 100]
          AssetInspector --squad <path> [--mappings <usmap>] metadata <layer-name> [--source vanilla|mod-id] [--output file]
          AssetInspector --squad <path> [--mappings <usmap>] benchmark <layer-name-filter|all> [--limit 300] [--source vanilla|mod-id] [--output file]
          AssetInspector --squad <path> [--mappings <usmap>] catalog all
          AssetInspector --squad <path> --mappings <usmap> schema <type-name>
          AssetInspector --squad <path> --mappings <usmap> schema-find <text> [--limit 100]
          AssetInspector --squad <path> [--mappings <usmap>] registry <text> [--limit 100]
          AssetInspector --squad <path> [--mappings <usmap>] primary-assets <primary-asset-type> [--mount /Plugin/] [--limit 100]
          AssetInspector --squad <path> [--mappings <usmap>] unit-vehicles <unit-object-path> --level <level-object-path>
          AssetInspector --squad <path> [--mappings <usmap>] commander-trace <unit-object-path> --level <world-object-path>

        SQUAD_PATH and SQUAD_MAPPINGS environment variables can replace the path options.
        Mappings are optional for uncooked Squad SDK assets and required for cooked game content.
        """;
}
