namespace CdnService.Domain;

/// <summary>
/// Published when a CDN asset is deleted.
/// </summary>
public record CdnAssetDeleted(
    string AssetId,
    Guid OwnerId,
    Guid? EntityId);
