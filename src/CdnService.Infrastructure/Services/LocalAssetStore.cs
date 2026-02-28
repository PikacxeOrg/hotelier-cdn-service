using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

using CdnService.Configuration;
using CdnService.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CdnService.Infrastructure;

/// <summary>
/// Stores assets on the local filesystem and persists metadata as
/// sidecar .meta.json files alongside each uploaded file.
/// </summary>
public class LocalAssetStore : IAssetStore
{
    private readonly StorageOptions _options;
    private readonly string _basePath;

    /// <summary>
    /// In-memory index (assetId → metadata) rebuilt on first access.
    /// </summary>
    private readonly ConcurrentDictionary<string, Asset> _index = new();
    private bool _indexLoaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public LocalAssetStore(IOptions<StorageOptions> options, IWebHostEnvironment env)
    {
        _options = options.Value;
        _basePath = Path.IsPathRooted(_options.BasePath)
            ? _options.BasePath
            : Path.Combine(env.ContentRootPath, _options.BasePath);

        Directory.CreateDirectory(_basePath);
    }

    // -- IAssetStore ----------------------------------------

    public async Task<Asset> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        Guid ownerId,
        Guid? entityId,
        CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);

        var ext = Path.GetExtension(originalFileName);
        var assetId = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(_basePath, assetId);

        await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(fs, ct);
        }

        var info = new FileInfo(filePath);

        var asset = new Asset
        {
            AssetId = assetId,
            OwnerId = ownerId,
            EntityId = entityId,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            SizeBytes = info.Length,
            UploadedAt = DateTime.UtcNow,
            StoredPath = assetId
        };

        await WriteMetaAsync(asset, ct);
        _index[assetId] = asset;
        return asset;
    }

    public async Task<Asset?> GetAsync(string assetId, CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);
        return _index.GetValueOrDefault(assetId);
    }

    public async Task<Stream?> GetStreamAsync(string assetId, CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);
        if (!_index.ContainsKey(assetId)) return null;

        var filePath = Path.Combine(_basePath, assetId);
        return File.Exists(filePath)
            ? new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
    }

    public async Task<IReadOnlyList<Asset>> ListByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);
        return _index.Values.Where(a => a.EntityId == entityId).ToList();
    }

    public async Task<IReadOnlyList<Asset>> ListByOwnerAsync(Guid ownerId, CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);
        return _index.Values.Where(a => a.OwnerId == ownerId).ToList();
    }

    public async Task<Asset?> DeleteAsync(string assetId, CancellationToken ct = default)
    {
        await EnsureIndexLoaded(ct);

        if (!_index.TryRemove(assetId, out var asset))
            return null;

        var filePath = Path.Combine(_basePath, assetId);
        if (File.Exists(filePath)) File.Delete(filePath);

        var metaPath = MetaPath(assetId);
        if (File.Exists(metaPath)) File.Delete(metaPath);

        return asset;
    }

    // ------- helpers --------------------------------------

    private string MetaPath(string assetId) => Path.Combine(_basePath, $"{assetId}.meta.json");

    private async Task WriteMetaAsync(Asset asset, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(asset, JsonCtx.Default.Asset);
        await File.WriteAllTextAsync(MetaPath(asset.AssetId), json, ct);
    }

    private async Task EnsureIndexLoaded(CancellationToken ct)
    {
        if (_indexLoaded) return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_indexLoaded) return;

            foreach (var metaFile in Directory.EnumerateFiles(_basePath, "*.meta.json"))
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var asset = JsonSerializer.Deserialize(json, JsonCtx.Default.Asset);
                if (asset is not null)
                    _index[asset.AssetId] = asset;
            }

            _indexLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}

/// <summary>
/// Source-generated JSON serialization context for Asset metadata.
/// </summary>
[JsonSerializable(typeof(Asset))]
internal partial class JsonCtx : JsonSerializerContext { }
