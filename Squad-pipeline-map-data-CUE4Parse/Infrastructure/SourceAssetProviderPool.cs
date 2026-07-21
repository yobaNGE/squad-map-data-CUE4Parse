using System.Collections.Concurrent;
using Squad_pipeline_map_data_CUE4Parse.Configuration;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

public sealed class SourceAssetProviderPool(ArchiveProfile profile) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<GameAssetProvider>>> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<GameAssetProvider> GetAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var provider = await _providers.GetOrAdd(
                sourceId,
                id => new Lazy<Task<GameAssetProvider>>(
                    () => CreateAsync(id),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value.WaitAsync(cancellationToken);
        return provider;
    }

    private async Task<GameAssetProvider> CreateAsync(string sourceId)
    {
        var isVanilla = sourceId.Equals("vanilla", StringComparison.OrdinalIgnoreCase);
        var isSdk = profile.ResolveContentLayout().IsEditorSdk;
        var mod = isVanilla
            ? null
            : profile.Mods.FirstOrDefault(candidate =>
                candidate.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
        var sdkPlugin = isVanilla
            ? null
            : profile.SdkPlugins.FirstOrDefault(candidate =>
                candidate.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
        if (!isVanilla && mod is null && sdkPlugin is null)
            throw new InvalidOperationException($"Content source '{sourceId}' is not present in the profile.");

        IGameAssetProvider? vanillaFallback = null;
        if (!isVanilla)
            vanillaFallback = await GetAsync("vanilla", _lifetime.Token);

        var sourceProfile = profile with
        {
            Mods = !isSdk && mod is not null ? [mod with { Enabled = true }] : [],
            SdkPlugins = isSdk && sdkPlugin is not null ? [sdkPlugin with { Enabled = true }] : [],
            ModDirectories = []
        };
        var provider = new GameAssetProvider(sourceProfile, vanillaFallback);
        try
        {
            await provider.InitializeAsync(_lifetime.Token);
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public async ValueTask ReleaseAsync(string sourceId)
    {
        if (!_providers.TryRemove(sourceId, out var provider) || !provider.IsValueCreated) return;
        try
        {
            (await provider.Value).Dispose();
        }
        catch
        {
            // Provider creation already reports its own failure to the caller.
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        foreach (var provider in _providers.Values)
        {
            if (!provider.IsValueCreated || !provider.Value.IsCompletedSuccessfully) continue;
            provider.Value.Result.Dispose();
        }
        _providers.Clear();
        _lifetime.Dispose();
    }
}
