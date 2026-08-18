using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;
using System.Diagnostics;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed record CachedLayerMetadataReadResult(
    LayerMetadata Metadata,
    bool CacheHit,
    long CacheReadMilliseconds,
    long ReaderInitializationMilliseconds,
    long RawMetadataMilliseconds,
    long CacheWriteMilliseconds,
    IReadOnlyDictionary<string, long> MetadataPhases);

internal sealed class CachedLayerMetadataReader(
    LayerCacheStore cache,
    Func<LayerDescriptor, string> environmentKey,
    Func<LayerDescriptor, CancellationToken, Task<ILayerMetadataReader>> innerFactory) : ILayerMetadataReader
{
    public async Task<LayerMetadata> ReadAsync(
        LayerDescriptor layer,
        CancellationToken cancellationToken = default) =>
        (await ReadProfiledAsync(layer, cancellationToken)).Metadata;

    public async Task<CachedLayerMetadataReadResult> ReadProfiledAsync(
        LayerDescriptor layer,
        CancellationToken cancellationToken = default)
    {
        var layerEnvironmentKey = environmentKey(layer);
        var cacheReadStopwatch = Stopwatch.StartNew();
        var cacheHit = cache.TryReadMetadata(layer, layerEnvironmentKey, out var cached) && cached is not null;
        var cacheReadMilliseconds = cacheReadStopwatch.ElapsedMilliseconds;
        if (cacheHit)
            return new CachedLayerMetadataReadResult(
                cached!,
                true,
                cacheReadMilliseconds,
                0,
                0,
                0,
                new Dictionary<string, long>());

        var readerStopwatch = Stopwatch.StartNew();
        var inner = await innerFactory(layer, cancellationToken);
        var readerInitializationMilliseconds = readerStopwatch.ElapsedMilliseconds;
        var rawMetadataStopwatch = Stopwatch.StartNew();
        var read = inner is LayerMetadataReader raw
            ? await raw.ReadProfiledAsync(layer, cancellationToken)
            : new LayerMetadataReadResult(
                await inner.ReadAsync(layer, cancellationToken),
                new Dictionary<string, long>());
        var rawMetadataMilliseconds = rawMetadataStopwatch.ElapsedMilliseconds;
        var cacheWriteStopwatch = Stopwatch.StartNew();
        await cache.WriteMetadataAsync(layer, layerEnvironmentKey, read.Metadata, cancellationToken);
        return new CachedLayerMetadataReadResult(
            read.Metadata,
            false,
            cacheReadMilliseconds,
            readerInitializationMilliseconds,
            rawMetadataMilliseconds,
            cacheWriteStopwatch.ElapsedMilliseconds,
            read.Phases);
    }

    public bool TryGetArtifact(LayerDescriptor layer, out string? artifactPath) =>
        cache.TryGetMetadataArtifact(layer, environmentKey(layer), out artifactPath);
}
