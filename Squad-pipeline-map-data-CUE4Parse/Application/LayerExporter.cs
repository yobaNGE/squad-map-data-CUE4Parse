using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Squad_pipeline_map_data_CUE4Parse.Domain;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

public sealed record LayerExportProgress(
    int Completed,
    int Total,
    string LayerName,
    int Failed,
    int Cached,
    long WorkingSetBytes,
    long PeakWorkingSetBytes);

public sealed record LayerExportFailure(string LayerName, string SourceId, string Message);

public sealed record LayerExportProfile(
    [property: JsonPropertyName("layer")] string LayerName,
    [property: JsonPropertyName("source")] string SourceId,
    [property: JsonPropertyName("cached")] bool Cached,
    [property: JsonPropertyName("artifactLookupMilliseconds")] long ArtifactLookupMilliseconds,
    [property: JsonPropertyName("cacheReadMilliseconds")] long CacheReadMilliseconds,
    [property: JsonPropertyName("readerInitializationMilliseconds")] long ReaderInitializationMilliseconds,
    [property: JsonPropertyName("metadataMilliseconds")] long MetadataMilliseconds,
    [property: JsonPropertyName("cacheWriteMilliseconds")] long CacheWriteMilliseconds,
    [property: JsonPropertyName("metadataPhases")] IReadOnlyDictionary<string, long> MetadataPhases,
    [property: JsonPropertyName("outputMilliseconds")] long OutputMilliseconds,
    [property: JsonPropertyName("outputOperation")] string OutputOperation,
    [property: JsonPropertyName("totalMilliseconds")] long TotalMilliseconds,
    [property: JsonPropertyName("workingSetBytes")] long WorkingSetBytes,
    [property: JsonPropertyName("error")] string? Error);

public sealed record LayerExportProfileReport(
    [property: JsonPropertyName("exported")] int Exported,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("cached")] int Cached,
    [property: JsonPropertyName("totalMilliseconds")] long TotalMilliseconds,
    [property: JsonPropertyName("peakWorkingSetBytes")] long PeakWorkingSetBytes,
    [property: JsonPropertyName("layers")] IReadOnlyList<LayerExportProfile> Layers);

public sealed record LayerExportReport(
    int Exported,
    int Failed,
    int Cached,
    TimeSpan Elapsed,
    long PeakWorkingSetBytes,
    IReadOnlyList<LayerExportFailure> Failures);

public sealed class LayerExporter
{
    private readonly ILayerMetadataReader _metadataReader;
    private readonly int _maxParallelLayers;

    public LayerExporter(ILayerMetadataReader metadataReader, int maxParallelLayers = 2)
    {
        _metadataReader = metadataReader;
        _maxParallelLayers = Math.Clamp(maxParallelLayers, 1, 8);
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<LayerExportReport> ExportAsync(
        IReadOnlyList<LayerDescriptor> layers,
        string outputDirectory,
        IProgress<LayerExportProgress>? progress = null,
        Func<string, ValueTask>? sourceCompleted = null,
        CancellationToken cancellationToken = default,
        bool writeProfile = false)
    {
        Directory.CreateDirectory(outputDirectory);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new ConcurrentQueue<LayerExportFailure>();
        var profiles = new ConcurrentQueue<LayerExportProfile>();
        var completed = 0;
        var exported = 0;
        var cachedCount = 0;
        var stopwatch = Stopwatch.StartNew();
        var peakWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var items = layers.Select(layer => new ExportItem(layer, AllocateOutputPath(layer))).ToArray();

        foreach (var sourceGroup in items.GroupBy(item => item.Layer.Source.Id, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await Parallel.ForEachAsync(
                    sourceGroup,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxParallelLayers,
                        CancellationToken = cancellationToken
                    },
                    async (item, token) =>
                {
                    var layer = item.Layer;
                    var layerStopwatch = Stopwatch.StartNew();
                    var artifactLookupMilliseconds = 0L;
                    var cacheReadMilliseconds = 0L;
                    var readerInitializationMilliseconds = 0L;
                    var metadataMilliseconds = 0L;
                    var cacheWriteMilliseconds = 0L;
                    IReadOnlyDictionary<string, long> metadataPhases = new Dictionary<string, long>();
                    var outputMilliseconds = 0L;
                    var outputOperation = "none";
                    var wasCached = false;
                    string? error = null;
                    try
                    {
                        var cacheReader = _metadataReader as CachedLayerMetadataReader;
                        var artifactLookupStopwatch = Stopwatch.StartNew();
                        string? artifactPath = null;
                        var hasArtifact = cacheReader is not null &&
                                          cacheReader.TryGetArtifact(layer, out artifactPath);
                        artifactLookupMilliseconds = artifactLookupStopwatch.ElapsedMilliseconds;
                        if (hasArtifact)
                        {
                            var outputStopwatch = Stopwatch.StartNew();
                            await CopyAsync(artifactPath!, item.OutputPath, token);
                            outputMilliseconds = outputStopwatch.ElapsedMilliseconds;
                            outputOperation = "copy";
                            wasCached = true;
                            Interlocked.Increment(ref cachedCount);
                        }
                        else
                        {
                            LayerMetadata metadata;
                            if (cacheReader is not null)
                            {
                                var read = await cacheReader.ReadProfiledAsync(layer, token);
                                metadata = read.Metadata;
                                cacheReadMilliseconds = read.CacheReadMilliseconds;
                                readerInitializationMilliseconds = read.ReaderInitializationMilliseconds;
                                metadataMilliseconds = read.RawMetadataMilliseconds;
                                cacheWriteMilliseconds = read.CacheWriteMilliseconds;
                                metadataPhases = read.MetadataPhases;
                            }
                            else
                            {
                                var metadataStopwatch = Stopwatch.StartNew();
                                metadata = await _metadataReader.ReadAsync(layer, token);
                                metadataMilliseconds = metadataStopwatch.ElapsedMilliseconds;
                            }
                            var outputStopwatch = Stopwatch.StartNew();
                            if (cacheReader is not null && cacheReader.TryGetArtifact(layer, out artifactPath))
                            {
                                await CopyAsync(artifactPath!, item.OutputPath, token);
                                outputOperation = "copy";
                            }
                            else
                            {
                                await SerializeAsync(metadata, item.OutputPath, token);
                                outputOperation = "serialize";
                            }
                            outputMilliseconds = outputStopwatch.ElapsedMilliseconds;
                        }
                        Interlocked.Increment(ref exported);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        error = exception.Message;
                        failures.Enqueue(new LayerExportFailure(layer.Name, layer.Source.Id, exception.Message));
                    }

                    if (writeProfile)
                        profiles.Enqueue(new LayerExportProfile(
                            layer.Name,
                            layer.Source.Id,
                            wasCached,
                            artifactLookupMilliseconds,
                            cacheReadMilliseconds,
                            readerInitializationMilliseconds,
                            metadataMilliseconds,
                            cacheWriteMilliseconds,
                            metadataPhases,
                            outputMilliseconds,
                            outputOperation,
                            layerStopwatch.ElapsedMilliseconds,
                            Process.GetCurrentProcess().WorkingSet64,
                            error));

                    var completedCount = Interlocked.Increment(ref completed);
                    var workingSet = Process.GetCurrentProcess().WorkingSet64;
                    UpdateMaximum(ref peakWorkingSet, workingSet);
                    progress?.Report(new LayerExportProgress(
                        completedCount,
                        layers.Count,
                        layer.Name,
                        failures.Count,
                        Volatile.Read(ref cachedCount),
                        workingSet,
                        Interlocked.Read(ref peakWorkingSet)));
                });
            }
            finally
            {
                if (sourceCompleted is not null) await sourceCompleted(sourceGroup.Key);
            }
        }

        var report = new LayerExportReport(
            exported,
            failures.Count,
            cachedCount,
            stopwatch.Elapsed,
            peakWorkingSet,
            failures.ToArray());
        if (writeProfile)
            await WriteProfileAsync(outputDirectory, report, profiles, cancellationToken);
        return report;

        string AllocateOutputPath(LayerDescriptor layer)
        {
            var baseName = SanitizeFileName(layer.Name);
            if (usedNames.Add(baseName)) return Path.Combine(outputDirectory, baseName + ".json");

            var sourceName = $"{baseName}__{SanitizeFileName(layer.Source.Id)}";
            var fileName = sourceName;
            var suffix = 2;
            while (!usedNames.Add(fileName)) fileName = $"{sourceName}_{suffix++}";
            return Path.Combine(outputDirectory, fileName + ".json");
        }
    }

    private static void UpdateMaximum(ref long target, long candidate)
    {
        var current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static async Task CopyAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var temporaryPath = outputPath + ".tmp";
        try
        {
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(output, cancellationToken);
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task SerializeAsync(
        LayerMetadata metadata,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = outputPath + ".tmp";
        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
                await JsonSerializer.SerializeAsync(output, metadata, JsonOptions, cancellationToken);
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static Task WriteProfileAsync(
        string outputDirectory,
        LayerExportReport report,
        IEnumerable<LayerExportProfile> profiles,
        CancellationToken cancellationToken) => File.WriteAllTextAsync(
        Path.Combine(outputDirectory, "export-profile.json"),
        JsonSerializer.Serialize(new LayerExportProfileReport(
            report.Exported,
            report.Failed,
            report.Cached,
            (long)report.Elapsed.TotalMilliseconds,
            report.PeakWorkingSetBytes,
            profiles.OrderByDescending(profile => profile.TotalMilliseconds).ToArray()), JsonOptions),
        cancellationToken);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record ExportItem(LayerDescriptor Layer, string OutputPath);
}
