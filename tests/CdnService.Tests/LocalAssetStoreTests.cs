using CdnService.Configuration;
using CdnService.Domain;
using CdnService.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

using Moq;

namespace CdnService.Tests;

public class LocalAssetStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalAssetStore _sut;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _entityId = Guid.NewGuid();

    public LocalAssetStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cdn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new StorageOptions { BasePath = _tempDir });
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempDir);

        _sut = new LocalAssetStore(options, env.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -- SaveAsync -----------------------------------------------

    [Fact]
    public async Task Save_CreatesFileOnDisk()
    {
        var asset = await SaveTestAsset("photo.jpg");

        var filePath = Path.Combine(_tempDir, asset.AssetId);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_CreatesMetadataFile()
    {
        var asset = await SaveTestAsset("photo.jpg");

        var metaPath = Path.Combine(_tempDir, $"{asset.AssetId}.meta.json");
        File.Exists(metaPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_ReturnsCorrectMetadata()
    {
        var asset = await SaveTestAsset("photo.jpg");

        asset.OwnerId.Should().Be(_ownerId);
        asset.EntityId.Should().Be(_entityId);
        asset.OriginalFileName.Should().Be("photo.jpg");
        asset.ContentType.Should().Be("image/jpeg");
        asset.SizeBytes.Should().BeGreaterThan(0);
        asset.AssetId.Should().EndWith(".jpg");
    }

    // -- GetAsync ------------------------------------------------

    [Fact]
    public async Task Get_ExistingAsset_ReturnsMetadata()
    {
        var saved = await SaveTestAsset("get.jpg");

        var retrieved = await _sut.GetAsync(saved.AssetId);

        retrieved.Should().NotBeNull();
        retrieved!.AssetId.Should().Be(saved.AssetId);
        retrieved.OriginalFileName.Should().Be("get.jpg");
    }

    [Fact]
    public async Task Get_NonExistentAsset_ReturnsNull()
    {
        var result = await _sut.GetAsync("nonexistent.jpg");
        result.Should().BeNull();
    }

    // -- GetStreamAsync ------------------------------------------

    [Fact]
    public async Task GetStream_ExistingAsset_ReturnsReadableStream()
    {
        var saved = await SaveTestAsset("stream.jpg");

        await using var stream = await _sut.GetStreamAsync(saved.AssetId);

        stream.Should().NotBeNull();
        stream!.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetStream_NonExistentAsset_ReturnsNull()
    {
        var result = await _sut.GetStreamAsync("nonexistent.jpg");
        result.Should().BeNull();
    }

    // -- ListByEntityAsync ---------------------------------------

    [Fact]
    public async Task ListByEntity_ReturnsMatchingAssets()
    {
        await SaveTestAsset("e1.jpg");
        await SaveTestAsset("e2.jpg");

        var result = await _sut.ListByEntityAsync(_entityId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListByEntity_ExcludesOtherEntities()
    {
        await SaveTestAsset("e1.jpg");
        await SaveTestAsset("e2.jpg", entityId: Guid.NewGuid());

        var result = await _sut.ListByEntityAsync(_entityId);

        result.Should().HaveCount(1);
    }

    // -- ListByOwnerAsync ----------------------------------------

    [Fact]
    public async Task ListByOwner_ReturnsMatchingAssets()
    {
        await SaveTestAsset("o1.jpg");
        await SaveTestAsset("o2.jpg");

        var result = await _sut.ListByOwnerAsync(_ownerId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListByOwner_ExcludesOtherOwners()
    {
        await SaveTestAsset("o1.jpg");
        await SaveTestAsset("o2.jpg", ownerId: Guid.NewGuid());

        var result = await _sut.ListByOwnerAsync(_ownerId);

        result.Should().HaveCount(1);
    }

    // -- DeleteAsync ---------------------------------------------

    [Fact]
    public async Task Delete_RemovesFileFromDisk()
    {
        var asset = await SaveTestAsset("del.jpg");

        await _sut.DeleteAsync(asset.AssetId);

        File.Exists(Path.Combine(_tempDir, asset.AssetId)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RemovesMetadataFromDisk()
    {
        var asset = await SaveTestAsset("del.jpg");

        await _sut.DeleteAsync(asset.AssetId);

        File.Exists(Path.Combine(_tempDir, $"{asset.AssetId}.meta.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ReturnsDeletedAssetMetadata()
    {
        var asset = await SaveTestAsset("del.jpg");

        var deleted = await _sut.DeleteAsync(asset.AssetId);

        deleted.Should().NotBeNull();
        deleted!.AssetId.Should().Be(asset.AssetId);
    }

    [Fact]
    public async Task Delete_NonExistentAsset_ReturnsNull()
    {
        var result = await _sut.DeleteAsync("nonexistent.jpg");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovedFromIndex()
    {
        var asset = await SaveTestAsset("idx.jpg");

        await _sut.DeleteAsync(asset.AssetId);

        var retrieved = await _sut.GetAsync(asset.AssetId);
        retrieved.Should().BeNull();
    }

    // -- Index rebuild -------------------------------------------

    [Fact]
    public async Task NewInstance_RebuildIndexFromMetaFiles()
    {
        // Save via original store
        var asset = await SaveTestAsset("rebuild.jpg");

        // Create a fresh instance that must rebuild from disk
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempDir);
        var freshStore = new LocalAssetStore(
            Options.Create(new StorageOptions { BasePath = _tempDir }),
            env.Object);

        var retrieved = await freshStore.GetAsync(asset.AssetId);

        retrieved.Should().NotBeNull();
        retrieved!.OriginalFileName.Should().Be("rebuild.jpg");
    }

    // -- helpers -------------------------------------------------

    private async Task<Asset> SaveTestAsset(
        string fileName,
        Guid? ownerId = null,
        Guid? entityId = null)
    {
        var content = new MemoryStream(new byte[128]);
        return await _sut.SaveAsync(
            content,
            fileName,
            "image/jpeg",
            ownerId ?? _ownerId,
            entityId ?? _entityId);
    }
}
