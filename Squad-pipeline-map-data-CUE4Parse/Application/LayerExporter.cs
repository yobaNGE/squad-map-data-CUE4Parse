using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
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
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new ConcurrentQueue<LayerExportFailure>();
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
                    try
                    {
                        if (_metadataReader is CachedLayerMetadataReader cached &&
                            cached.TryGetArtifact(layer, out var artifactPath))
                        {
                            await CopyAsync(artifactPath!, item.OutputPath, token);
                            Interlocked.Increment(ref cachedCount);
                        }
                        else
                        {
                            var metadata = await _metadataReader.ReadAsync(layer, token);
                            if (_metadataReader is CachedLayerMetadataReader materialized &&
                                materialized.TryGetArtifact(layer, out artifactPath))
                            {
                                await CopyAsync(artifactPath!, item.OutputPath, token);
                            }
                            else
                            {
                                await SerializeAsync(metadata, item.OutputPath, token);
                            }
                        }
                        Interlocked.Increment(ref exported);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(new LayerExportFailure(layer.Name, layer.Source.Id, exception.Message));
                    }

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

        return new LayerExportReport(
            exported,
            failures.Count,
            cachedCount,
            stopwatch.Elapsed,
            peakWorkingSet,
            failures.ToArray());

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

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record ExportItem(LayerDescriptor Layer, string OutputPath);
}
