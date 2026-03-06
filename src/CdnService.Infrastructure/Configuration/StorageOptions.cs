namespace CdnService.Configuration;

/// <summary>
/// Options for local file-system storage.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root directory where uploaded files are stored.
    /// Defaults to ./uploads inside the content root.
    /// </summary>
    public string BasePath { get; set; } = "uploads";

    /// <summary>
    /// Maximum file size in bytes (default: 10 MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Allowed MIME types.
    /// </summary>
    public List<string> AllowedContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    ];
}
