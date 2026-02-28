namespace CdnService.Domain;

/// <summary>
/// Published after a CDN asset has been processed and stored.
/// Consumed by accommodation-service to update picture URLs.
/// </summary>
public record CdnAssetProcessed(
    string AssetId,
    Guid OwnerId,
    Guid? EntityId,
    string Url,
    string ContentType,
    long SizeBytes);
