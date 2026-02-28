namespace CdnService.Api;

public class AssetMetadata
{
    public string AssetId { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }

    /// <summary>
    /// Linked entity (e.g. accommodation ID).
    /// </summary>
    public Guid? EntityId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
