using System.Security.Claims;

using CdnService.Configuration;
using CdnService.Domain;

using Hotelier.Events;

using MassTransit;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CdnService.Api;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AssetsController(
    IAssetStore store,
    IOptions<StorageOptions> storageOptions,
    IPublishEndpoint publisher,
    ILogger<AssetsController> logger) : ControllerBase
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    /// <summary>
    /// Upload one or more image files.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB total
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile> files,
        [FromForm] Guid? entityId,
        CancellationToken ct)
    {
        var ownerId = GetOwnerId();
        if (ownerId is null) return Unauthorized();

        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files provided." });

        var results = new List<UploadResponse>();

        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            if (file.Length > _storageOptions.MaxFileSizeBytes)
                return BadRequest(new { message = $"File '{file.FileName}' exceeds the maximum size of {_storageOptions.MaxFileSizeBytes / (1024 * 1024)} MB." });

            if (!_storageOptions.AllowedContentTypes.Contains(file.ContentType))
                return BadRequest(new { message = $"Content type '{file.ContentType}' is not allowed." });

            await using var stream = file.OpenReadStream();
            var asset = await store.SaveAsync(stream, file.FileName, file.ContentType, ownerId.Value, entityId, ct);

            var url = $"{Request.Scheme}://{Request.Host}/assets/{asset.AssetId}";

            await publisher.Publish(new CdnAssetProcessed
            {
                AssetId = asset.AssetId,
                OwnerId = asset.OwnerId,
                EntityId = asset.EntityId,
                Url = url,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes
            }, ct);

            logger.LogInformation("Asset {AssetId} uploaded by {OwnerId}", asset.AssetId, ownerId);

            results.Add(new UploadResponse
            {
                AssetId = asset.AssetId,
                Url = url,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                UploadedAt = asset.UploadedAt
            });
        }

        return CreatedAtAction(nameof(GetMetadata), new { assetId = results.FirstOrDefault()?.AssetId }, results);
    }

    /// <summary>
    /// Get asset metadata by ID.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{assetId}/metadata")]
    public async Task<IActionResult> GetMetadata(string assetId, CancellationToken ct)
    {
        var asset = await store.GetAsync(assetId, ct);
        if (asset is null) return NotFound();

        return Ok(new AssetMetadata
        {
            AssetId = asset.AssetId,
            OwnerId = asset.OwnerId,
            EntityId = asset.EntityId,
            OriginalFileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            UploadedAt = asset.UploadedAt
        });
    }

    /// <summary>
    /// List all assets for a given entity (e.g. accommodation).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("entity/{entityId:guid}")]
    public async Task<IActionResult> ListByEntity(Guid entityId, CancellationToken ct)
    {
        var assets = await store.ListByEntityAsync(entityId, ct);
        return Ok(MapMetadataList(assets));
    }

    /// <summary>
    /// List all assets belonging to an owner.
    /// </summary>
    [HttpGet("owner/{ownerId:guid}")]
    public async Task<IActionResult> ListByOwner(Guid ownerId, CancellationToken ct)
    {
        var assets = await store.ListByOwnerAsync(ownerId, ct);
        return Ok(MapMetadataList(assets));
    }

    /// <summary>
    /// List all assets belonging to the current user.
    /// </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var ownerId = GetOwnerId();
        if (ownerId is null) return Unauthorized();

        var assets = await store.ListByOwnerAsync(ownerId.Value, ct);
        return Ok(MapMetadataList(assets));
    }

    /// <summary>
    /// Delete an asset. Only the owner can delete.
    /// </summary>
    [HttpDelete("{assetId}")]
    public async Task<IActionResult> Delete(string assetId, CancellationToken ct)
    {
        var ownerId = GetOwnerId();
        if (ownerId is null) return Unauthorized();

        var existing = await store.GetAsync(assetId, ct);
        if (existing is null) return NotFound();

        if (existing.OwnerId != ownerId.Value)
            return Forbid();

        var deleted = await store.DeleteAsync(assetId, ct);
        if (deleted is null) return NotFound();

        var url = $"{Request.Scheme}://{Request.Host}/assets/{deleted.AssetId}";

        await publisher.Publish(new CdnAssetDeleted
        {
            AssetId = deleted.AssetId,
            OwnerId = deleted.OwnerId,
            EntityId = deleted.EntityId,
            Url = url
        }, ct);

        logger.LogInformation("Asset {AssetId} deleted by {OwnerId}", assetId, ownerId);
        return NoContent();
    }

    // -- helpers -------------------------------------------------

    private Guid? GetOwnerId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static List<AssetMetadata> MapMetadataList(IReadOnlyList<Asset> assets) =>
        assets.Select(a => new AssetMetadata
        {
            AssetId = a.AssetId,
            OwnerId = a.OwnerId,
            EntityId = a.EntityId,
            OriginalFileName = a.OriginalFileName,
            ContentType = a.ContentType,
            SizeBytes = a.SizeBytes,
            UploadedAt = a.UploadedAt
        }).ToList();
}
