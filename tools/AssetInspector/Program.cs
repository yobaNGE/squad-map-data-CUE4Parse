using System.Text.Json;
using System.Text.Json.Nodes;
using AssetInspector;
using CUE4Parse.MappingsProvider.Usmap;
using Squad_pipeline_map_data_CUE4Parse.Application;
using Squad_pipeline_map_data_CUE4Parse.Configuration;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    var options = InspectorArguments.Parse(args);
    var modDiscovery = new WorkshopModDiscovery();
    var workshopPath = modDiscovery.ResolveWorkshopPath(options.SquadPath);
    var profile = new ArchiveProfile
    {
        SquadPath = options.SquadPath,
        MappingsPath = options.MappingsPath,
        WorkshopPath = workshopPath,
        Mods = modDiscovery.Discover(workshopPath),
        ReadScriptData = false
    };

    var selectedProfile = options.SourceId.Equals("all", StringComparison.OrdinalIgnoreCase)
        ? profile
        : profile with
        {
            Mods = options.SourceId.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
                ? []
                : profile.Mods.Where(mod =>
                    mod.Id.Equals(options.SourceId, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
    using var provider = new GameAssetProvider(selectedProfile);
    await provider.InitializeAsync();

    JsonNode result = options.Command switch
    {
        "find" => Find(provider, options.Query, options.Limit),
        "inspect" => Inspect(provider, options),
        "metadata" => await ReadMetadata(provider, options.Query),
        "benchmark" => await Benchmark(provider, options.Query, options.Limit),
        "catalog" => JsonSerializer.SerializeToNode(await new LayerCatalogService(
            provider,
            options.SourceId.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? null
                : options.SourceId).ScanAsync())!,
        "schema" => InspectSchema(options.MappingsPath, options.Query),
        "schema-find" => FindSchema(options.MappingsPath, options.Query, options.Limit),
        "registry" => InspectRegistry(provider, options.Query, options.Limit),
        "primary-assets" => InspectPrimaryAssets(provider, options.Query, options.MountPoint, options.Limit),
        "unit-vehicles" => InspectUnitVehicles(provider, options),
        "commander-trace" => InspectCommander(provider, options),
        _ => throw new ArgumentException(InspectorArguments.Usage)
    };

    var json = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    if (string.IsNullOrWhiteSpace(options.OutputPath))
        Console.WriteLine(json);
    else
        await File.WriteAllTextAsync(options.OutputPath, json);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

static JsonArray Find(IGameAssetProvider provider, string query, int limit)
{
    var result = new JsonArray();
    foreach (var path in MatchingPackages(provider, query).Take(limit))
        result.Add(path);
    return result;
}

static JsonObject Inspect(IGameAssetProvider provider, InspectorArguments options)
{
    var inspector = new AssetGraphInspector(options.Depth, options.Limit);
    if (options.Query.StartsWith('/') && provider.LoadObject(options.Query) is { } directObject)
    {
        return new JsonObject
        {
            ["query"] = options.Query,
            ["object"] = inspector.Inspect(directObject)
        };
    }

    var packages = MatchingPackages(provider, options.Query).Take(options.Limit).ToArray();
    var packageNodes = new JsonArray();

    foreach (var package in packages)
    {
        var exports = new JsonArray();
        foreach (var export in provider.LoadPackageExports(package)
                     .Where(export => options.ExportType is null
                                      || export.ExportType.Equals(options.ExportType, StringComparison.OrdinalIgnoreCase))
                     .Take(options.Limit))
            exports.Add(inspector.Inspect(export));
        packageNodes.Add(new JsonObject
        {
            ["package"] = package,
            ["exports"] = exports
        });
    }

    return new JsonObject
    {
        ["query"] = options.Query,
        ["matchedPackages"] = packages.Length,
        ["packages"] = packageNodes
    };
}

static IEnumerable<string> MatchingPackages(IGameAssetProvider provider, string query)
{
    var packageQuery = query.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)
        ? "SquadGame/Content/" + query[6..].Split('.')[0]
        : query;

    return
    provider.PackagePaths
        .Where(path => path.Contains(packageQuery, StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase);
}

static async Task<JsonNode> ReadMetadata(IGameAssetProvider provider, string query)
{
    var layers = await new LayerCatalogService(provider).ScanAsync();
    var layer = layers.First(candidate =>
        candidate.GameplayObjectName.Equals(query, StringComparison.OrdinalIgnoreCase));
    var metadata = await new LayerMetadataReader(provider).ReadAsync(layer);
    return JsonSerializer.SerializeToNode(metadata)!;
}

static async Task<JsonNode> Benchmark(IGameAssetProvider provider, string query, int limit)
{
    var layers = (await new LayerCatalogService(provider).ScanAsync())
        .Where(layer => query.Equals("all", StringComparison.OrdinalIgnoreCase)
                        || layer.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        .Take(limit)
        .ToArray();
    var reader = new LayerMetadataReader(provider);
    var process = System.Diagnostics.Process.GetCurrentProcess();
    var started = System.Diagnostics.Stopwatch.StartNew();
    var peak = process.WorkingSet64;
    var failures = new JsonArray();

    foreach (var layer in layers)
    {
        try
        {
            var metadata = await reader.ReadAsync(layer);
            await JsonSerializer.SerializeAsync(Stream.Null, metadata);
        }
        catch (Exception exception)
        {
            failures.Add(new JsonObject
            {
                ["layer"] = layer.Name,
                ["message"] = exception.Message
            });
        }
        process.Refresh();
        peak = Math.Max(peak, process.WorkingSet64);
    }

    return new JsonObject
    {
        ["layers"] = layers.Length,
        ["completed"] = layers.Length - failures.Count,
        ["failed"] = failures.Count,
        ["elapsedSeconds"] = started.Elapsed.TotalSeconds,
        ["peakWorkingSetBytes"] = peak,
        ["finalWorkingSetBytes"] = process.WorkingSet64,
        ["failures"] = failures
    };
}

static JsonNode InspectUnitVehicles(IGameAssetProvider provider, InspectorArguments options)
{
    var unit = provider.LoadObject(options.Query)
               ?? throw new InvalidDataException($"Unit '{options.Query}' was not found.");
    var properties = new UnrealPropertyReader(provider);
    var levelPath = options.LevelId
                    ?? throw new ArgumentException("unit-vehicles requires --level <level-object-path>.");
    var level = provider.LoadObject(levelPath)
                ?? throw new InvalidDataException($"Level '{levelPath}' was not found.");
    var biome = new LevelBiomeResolver(provider, properties).ResolveLevel(level);
    var vehicles = new UnitVehicleReader(provider, properties).Read(unit, biome);
    return new JsonObject
    {
        ["level"] = levelPath,
        ["biome"] = biome,
        ["vehicles"] = JsonSerializer.SerializeToNode(vehicles)
    };
}

static JsonNode InspectCommander(IGameAssetProvider provider, InspectorArguments options)
{
    var unit = provider.LoadObject(options.Query)
               ?? throw new InvalidDataException($"Unit '{options.Query}' was not found.");
    var worldPath = options.LevelId
                    ?? throw new ArgumentException("commander-trace requires --level <world-object-path>.");
    var world = provider.LoadObject(worldPath)
                ?? throw new InvalidDataException($"World '{worldPath}' was not found.");
    var properties = new UnrealPropertyReader(provider);

    var availability = new JsonArray();
    foreach (var reference in properties.ArrayInherited(unit, "Actions"))
    {
        var item = properties.ResolveObject(reference);
        var setting = properties.ObjectInherited(item, "Setting");
        var firstVersion = properties.ArrayInherited(setting, "ActionVersions")
            .Select(UnrealPropertyReader.Unwrap)
            .OfType<CUE4Parse.UE4.Assets.Exports.IPropertyHolder>()
            .FirstOrDefault();
        availability.Add(new JsonObject
        {
            ["availability"] = item?.GetPathName(),
            ["setting"] = setting?.GetPathName(),
            ["actionActor"] = firstVersion is null ? null : properties.ResolveObject(
                properties.RawStartingWith(firstVersion, "ActionActor_"))?.GetPathName()
        });
    }

    var worldSettings = world.Owner?.GetExports().FirstOrDefault(export =>
        export.ExportType.Equals("SQWorldSettings", StringComparison.OrdinalIgnoreCase));
    var gameMode = properties.ObjectInherited(worldSettings, "DefaultGameMode");
    var teamClass = properties.ObjectInherited(gameMode, "TeamClass");
    var commanderManager = properties.ObjectInherited(teamClass, "CommanderManager");
    var table = properties.ObjectInherited(commanderManager, "TeamCommands") as CUE4Parse.UE4.Assets.Exports.Engine.UDataTable;
    var commands = new JsonArray();
    if (table is not null)
    {
        foreach (var pair in table.RowMap.Take(options.Limit))
        {
            var command = properties.Object(pair.Value, "CommandData");
            commands.Add(new JsonObject
            {
                ["row"] = pair.Key.Text,
                ["command"] = command?.GetPathName(),
                ["commandActor"] = properties.ObjectInherited(command, "CommandActor")?.GetPathName(),
                ["displayName"] = properties.StringInherited(command, string.Empty, "DisplayName"),
                ["icon"] = properties.ObjectInherited(command, "Texture", "Icon")?.Name,
                ["cooldown"] = properties.DoubleInherited(command, 0, "CooldownDuration"),
                ["teams"] = JsonSerializer.SerializeToNode(properties.Array(pair.Value, "Team")
                    .Select(UnrealPropertyReader.ToStringValue).ToArray())
            });
        }
    }

    return new JsonObject
    {
        ["worldSettings"] = worldSettings?.GetPathName(),
        ["gameMode"] = gameMode?.GetPathName(),
        ["teamClass"] = teamClass?.GetPathName(),
        ["commanderManager"] = commanderManager?.GetPathName(),
        ["teamCommands"] = table?.GetPathName(),
        ["availability"] = availability,
        ["commands"] = commands
    };
}

static JsonNode InspectSchema(string mappingsPath, string query)
{
    var mappings = new FileUsmapTypeMappingsProvider(mappingsPath).MappingsForGame
                   ?? throw new InvalidDataException("Mappings file did not contain type mappings.");
    if (!mappings.Types.TryGetValue(query, out var type))
        throw new InvalidDataException($"Type mapping '{query}' was not found.");

    var hierarchy = new JsonArray();
    for (var current = type; current is not null; current = current.Super.Value)
    {
        var properties = new JsonArray();
        foreach (var property in current.Properties.OrderBy(entry => entry.Key).Select(entry => entry.Value).Distinct())
        {
            properties.Add(new JsonObject
            {
                ["name"] = property.Name,
                ["type"] = property.MappingType.ToString(),
                ["arraySize"] = property.ArraySize
            });
        }
        hierarchy.Add(new JsonObject
        {
            ["name"] = current.Name,
            ["superType"] = current.SuperType,
            ["properties"] = properties
        });
    }
    return hierarchy;
}

static JsonNode InspectRegistry(GameAssetProvider provider, string query, int limit)
{
    var result = new JsonArray();
    foreach (var asset in provider.ReadAssetRegistryAssets().Where(asset =>
                 asset.ObjectPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                 || asset.AssetClass.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                 || asset.TagsAndValues.Any(tag => tag.Key.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                   || tag.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))
             .Take(limit))
    {
        var tags = new JsonObject();
        foreach (var tag in asset.TagsAndValues)
            tags[tag.Key.Text] = tag.Value;
        result.Add(new JsonObject
        {
            ["objectPath"] = asset.ObjectPath,
            ["assetClass"] = asset.AssetClass.Text,
            ["tags"] = tags
        });
    }
    return result;
}

static JsonNode InspectPrimaryAssets(GameAssetProvider provider, string primaryAssetType, string? mountPoint, int limit)
{
    var matched = provider.ReadAssetRegistryAssets()
        .Where(asset => HasRegistryTag(asset, "PrimaryAssetType", primaryAssetType)
                        && (string.IsNullOrWhiteSpace(mountPoint)
                            || asset.ObjectPath.StartsWith(mountPoint, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(asset => asset.ObjectPath, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var assets = new JsonArray();
    foreach (var asset in matched.Take(limit))
    {
        assets.Add(new JsonObject
        {
            ["objectPath"] = asset.ObjectPath,
            ["assetClass"] = asset.AssetClass.Text,
            ["primaryAssetName"] = RegistryTag(asset, "PrimaryAssetName")
        });
    }
    return new JsonObject
    {
        ["count"] = matched.Length,
        ["assets"] = assets
    };
}

static bool HasRegistryTag(CUE4Parse.UE4.AssetRegistry.Objects.FAssetData asset, string key, string value) =>
    asset.TagsAndValues.Any(tag => tag.Key.Text.Equals(key, StringComparison.OrdinalIgnoreCase)
                                   && tag.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

static string? RegistryTag(CUE4Parse.UE4.AssetRegistry.Objects.FAssetData asset, string key) =>
    asset.TagsAndValues.FirstOrDefault(tag => tag.Key.Text.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

static JsonNode FindSchema(string mappingsPath, string query, int limit)
{
    var mappings = new FileUsmapTypeMappingsProvider(mappingsPath).MappingsForGame
                   ?? throw new InvalidDataException("Mappings file did not contain type mappings.");
    var result = new JsonArray();
    foreach (var type in mappings.Types.Values.OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase))
    foreach (var property in type.Properties.Values.Distinct())
    {
        if (!type.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !property.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
        result.Add(new JsonObject
        {
            ["type"] = type.Name,
            ["superType"] = type.SuperType,
            ["property"] = property.Name,
            ["propertyType"] = property.MappingType.ToString()
        });
        if (result.Count >= limit) return result;
    }
    return result;
}
