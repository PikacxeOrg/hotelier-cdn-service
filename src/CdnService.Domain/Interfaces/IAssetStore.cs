namespace CdnService.Domain;

/// <summary>
/// Abstraction for persisting and retrieving CDN assets.
/// </summary>
public interface IAssetStore
{
    /// <summary>
    /// Save a new asset to storage and return its metadata.
    /// </summary>
    Task<Asset> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        Guid ownerId,
        Guid? entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Get asset metadata by ID.
    /// </summary>
    Task<Asset?> GetAsync(string assetId, CancellationToken ct = default);

    /// <summary>
    /// Get the file stream for an asset.
    /// </summary>
    Task<Stream?> GetStreamAsync(string assetId, CancellationToken ct = default);

    /// <summary>
    /// List assets belonging to an entity (e.g. accommodation).
    /// </summary>
    Task<IReadOnlyList<Asset>> ListByEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// List assets belonging to an owner.
    /// </summary>
    Task<IReadOnlyList<Asset>> ListByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// Delete an asset and return its metadata (null if not found).
    /// </summary>
    Task<Asset?> DeleteAsync(string assetId, CancellationToken ct = default);
}
