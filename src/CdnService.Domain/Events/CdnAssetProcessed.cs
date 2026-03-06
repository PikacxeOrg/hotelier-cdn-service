namespace Hotelier.Events;

/// <summary>
/// Published after a CDN asset has been processed and stored.
/// Consumed by accommodation-service to update picture URLs.
/// </summary>
public record CdnAssetProcessed
{
    public string AssetId { get; init; } = string.Empty;
    public Guid OwnerId { get; init; }
    public Guid? EntityId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}
