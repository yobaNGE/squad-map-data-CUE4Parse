using CUE4Parse.UE4.Assets.Exports;
using Squad_pipeline_map_data_CUE4Parse.Configuration;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

public sealed record ContentSource(string Id, string DisplayName, bool IsVanilla);
public sealed record PrimaryAssetReference(string ObjectPath, string Name);

public interface IGameAssetProvider : IDisposable
{
    ArchiveProfile Profile { get; }
    IReadOnlyCollection<string> PackagePaths { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<UObject> LoadPackageExports(string packagePath);
    IReadOnlyList<UObject> LoadPackageExportsWithScriptData(string packagePath);
    UObject? LoadObject(string objectPath);
    UObject? LoadPrimaryAsset(string primaryAssetType, string primaryAssetName);
    IReadOnlyList<PrimaryAssetReference> GetPrimaryAssets(string primaryAssetType);
    string? ResolvePackagePath(string objectPath);
    ContentSource GetContentSource(string packagePath);
    string CreateArchiveFingerprint();
}
