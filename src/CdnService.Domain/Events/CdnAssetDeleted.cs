namespace Hotelier.Events;

/// <summary>
/// Published when a CDN asset is deleted.
/// Consumed by accommodation-service to remove picture URLs.
/// </summary>
public record CdnAssetDeleted
{
    public string AssetId { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public Guid? EntityId { get; init; }
    public string Url { get; init; } = string.Empty;
}
