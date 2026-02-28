using System.Security.Claims;
using System.Text;

using CdnService.Api;
using CdnService.Configuration;
using CdnService.Domain;

using Hotelier.Events;

using FluentAssertions;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

namespace CdnService.Tests;

public class AssetsControllerTests : IDisposable
{
    private readonly Mock<IAssetStore> _storeMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly AssetsController _sut;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _entityId = Guid.NewGuid();

    public AssetsControllerTests()
    {
        _storeMock = new Mock<IAssetStore>();
        _publisherMock = new Mock<IPublishEndpoint>();
        var logger = new Mock<ILogger<AssetsController>>();
        var options = Options.Create(new StorageOptions());

        _sut = new AssetsController(_storeMock.Object, options, _publisherMock.Object, logger.Object);
        SetAuthenticatedUser(_ownerId);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    // -- Upload --------------------------------------------------

    [Fact]
    public async Task Upload_WithValidFile_ReturnsCreated()
    {
        var file = CreateFormFile("test.jpg", "image/jpeg", 1024);
        SetupSaveReturns();

        var result = await _sut.Upload([file], _entityId, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Upload_WithValidFile_PublishesCdnAssetProcessedEvent()
    {
        var file = CreateFormFile("test.jpg", "image/jpeg", 1024);
        SetupSaveReturns();

        await _sut.Upload([file], _entityId, CancellationToken.None);

        _publisherMock.Verify(p =>
            p.Publish(It.IsAny<CdnAssetProcessed>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_NoFiles_ReturnsBadRequest()
    {
        var result = await _sut.Upload([], null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_FileTooLarge_ReturnsBadRequest()
    {
        var file = CreateFormFile("big.jpg", "image/jpeg", 20 * 1024 * 1024); // 20MB > default 10MB

        var result = await _sut.Upload([file], null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_DisallowedContentType_ReturnsBadRequest()
    {
        var file = CreateFormFile("test.exe", "application/octet-stream", 100);

        var result = await _sut.Upload([file], null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_Unauthenticated_ReturnsUnauthorized()
    {
        SetUnauthenticated();
        var file = CreateFormFile("test.jpg", "image/jpeg", 1024);

        var result = await _sut.Upload([file], null, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Upload_MultipleFiles_ReturnsAllInResponse()
    {
        var file1 = CreateFormFile("a.jpg", "image/jpeg", 500);
        var file2 = CreateFormFile("b.png", "image/png", 800);
        SetupSaveReturns();

        var result = await _sut.Upload([file1, file2], _entityId, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var list = created.Value.Should().BeAssignableTo<List<UploadResponse>>().Subject;
        list.Should().HaveCount(2);
    }

    // -- GetMetadata ---------------------------------------------

    [Fact]
    public async Task GetMetadata_ExistingAsset_ReturnsOk()
    {
        var asset = CreateAsset("abc.jpg");
        _storeMock.Setup(s => s.GetAsync("abc.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var result = await _sut.GetMetadata("abc.jpg", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var metadata = ok.Value.Should().BeOfType<AssetMetadata>().Subject;
        metadata.AssetId.Should().Be("abc.jpg");
    }

    [Fact]
    public async Task GetMetadata_NonExistent_ReturnsNotFound()
    {
        _storeMock.Setup(s => s.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Asset?)null);

        var result = await _sut.GetMetadata("missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // -- ListByEntity --------------------------------------------

    [Fact]
    public async Task ListByEntity_ReturnsAssets()
    {
        var assets = new List<Asset> { CreateAsset("a.jpg"), CreateAsset("b.jpg") };
        _storeMock.Setup(s => s.ListByEntityAsync(_entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets);

        var result = await _sut.ListByEntity(_entityId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<List<AssetMetadata>>().Subject;
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListByEntity_NoAssets_ReturnsEmptyList()
    {
        _storeMock.Setup(s => s.ListByEntityAsync(_entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Asset>());

        var result = await _sut.ListByEntity(_entityId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<List<AssetMetadata>>().Subject;
        list.Should().BeEmpty();
    }

    // -- ListByOwner ---------------------------------------------

    [Fact]
    public async Task ListByOwner_ReturnsAssets()
    {
        var assets = new List<Asset> { CreateAsset("c.jpg") };
        _storeMock.Setup(s => s.ListByOwnerAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets);

        var result = await _sut.ListByOwner(_ownerId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<List<AssetMetadata>>().Subject;
        list.Should().HaveCount(1);
    }

    // -- ListMine ------------------------------------------------

    [Fact]
    public async Task ListMine_ReturnsCurrentUsersAssets()
    {
        var assets = new List<Asset> { CreateAsset("mine.jpg") };
        _storeMock.Setup(s => s.ListByOwnerAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets);

        var result = await _sut.ListMine(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value.Should().BeAssignableTo<List<AssetMetadata>>().Subject;
        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListMine_Unauthenticated_ReturnsUnauthorized()
    {
        SetUnauthenticated();

        var result = await _sut.ListMine(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    // -- Delete --------------------------------------------------

    [Fact]
    public async Task Delete_OwnAsset_ReturnsNoContent()
    {
        var asset = CreateAsset("del.jpg");
        _storeMock.Setup(s => s.GetAsync("del.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        _storeMock.Setup(s => s.DeleteAsync("del.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var result = await _sut.Delete("del.jpg", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_OwnAsset_PublishesCdnAssetDeletedEvent()
    {
        var asset = CreateAsset("del.jpg");
        _storeMock.Setup(s => s.GetAsync("del.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        _storeMock.Setup(s => s.DeleteAsync("del.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        await _sut.Delete("del.jpg", CancellationToken.None);

        _publisherMock.Verify(p =>
            p.Publish(It.IsAny<CdnAssetDeleted>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsNotFound()
    {
        _storeMock.Setup(s => s.GetAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Asset?)null);

        var result = await _sut.Delete("nope", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_OtherUsersAsset_ReturnsForbid()
    {
        var asset = CreateAsset("other.jpg");
        asset.OwnerId = Guid.NewGuid(); // different owner
        _storeMock.Setup(s => s.GetAsync("other.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var result = await _sut.Delete("other.jpg", CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Delete_Unauthenticated_ReturnsUnauthorized()
    {
        SetUnauthenticated();

        var result = await _sut.Delete("any.jpg", CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    // -- helpers -------------------------------------------------

    private void SetAuthenticatedUser(Guid userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private void SetUnauthenticated()
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
    }

    private Asset CreateAsset(string assetId) => new()
    {
        AssetId = assetId,
        OwnerId = _ownerId,
        EntityId = _entityId,
        OriginalFileName = assetId,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        UploadedAt = DateTime.UtcNow,
        StoredPath = assetId
    };

    private void SetupSaveReturns()
    {
        _storeMock.Setup(s => s.SaveAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, string contentType, Guid ownerId, Guid? entityId, CancellationToken _) =>
                new Asset
                {
                    AssetId = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}",
                    OwnerId = ownerId,
                    EntityId = entityId,
                    OriginalFileName = fileName,
                    ContentType = contentType,
                    SizeBytes = 1024,
                    UploadedAt = DateTime.UtcNow,
                    StoredPath = fileName
                });
    }

    private static IFormFile CreateFormFile(string name, string contentType, int sizeBytes)
    {
        var content = new byte[sizeBytes];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, sizeBytes, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
