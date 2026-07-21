using Squad_pipeline_map_data_CUE4Parse.Domain;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class CachedLayerMetadataReader(
    LayerCacheStore cache,
    Func<LayerDescriptor, string> environmentKey,
    Func<LayerDescriptor, CancellationToken, Task<ILayerMetadataReader>> innerFactory) : ILayerMetadataReader
{
    public async Task<LayerMetadata> ReadAsync(
        LayerDescriptor layer,
        CancellationToken cancellationToken = default)
    {
        var layerEnvironmentKey = environmentKey(layer);
        if (cache.TryReadMetadata(layer, layerEnvironmentKey, out var cached) && cached is not null)
            return cached;

        var inner = await innerFactory(layer, cancellationToken);
        var metadata = await inner.ReadAsync(layer, cancellationToken);
        await cache.WriteMetadataAsync(layer, layerEnvironmentKey, metadata, cancellationToken);
        return metadata;
    }

    public bool TryGetArtifact(LayerDescriptor layer, out string? artifactPath) =>
        cache.TryGetMetadataArtifact(layer, environmentKey(layer), out artifactPath);
}
