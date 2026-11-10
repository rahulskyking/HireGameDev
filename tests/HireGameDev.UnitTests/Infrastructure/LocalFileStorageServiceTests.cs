using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalFileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"hgd-storage-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task PutOpenAndDelete_IsolatesPrivateAndPublicTreesOutsideWwwroot()
    {
        var contentRoot = Path.Combine(_tempRoot, "app");
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        Directory.CreateDirectory(webRoot);

        var storage = new LocalFileStorageService(
            new StorageOptions
            {
                Provider = "Local",
                Local = new LocalStorageOptions
                {
                    RootPath = "App_Data/uploads",
                    PublicRoutePrefix = "/Media/Public"
                }
            },
            contentRoot,
            webRoot,
            NullLogger<LocalFileStorageService>.Instance);

        byte[] avatarBytes = [1, 2, 3, 4, 5, 6];
        const string avatarKey = "avatars/candidate_01.webp";

        await using (var putStream = new MemoryStream(avatarBytes))
        {
            await storage.PutAsync(new FileStoragePutRequest(
                ObjectKey: avatarKey,
                Content: putStream,
                ContentType: "image/webp",
                Visibility: StorageVisibility.Private));
        }

        // Private object must NEVER be readable through Public visibility.
        var wrongVisibilityRead = await storage.OpenReadAsync(avatarKey, StorageVisibility.Public);
        Assert.Null(wrongVisibilityRead);

        // Private object reads cleanly under Private visibility.
        var privateRead = await storage.OpenReadAsync(avatarKey, StorageVisibility.Private);
        Assert.NotNull(privateRead);
        await using (privateRead)
        {
            Assert.Equal("image/webp", privateRead!.ContentType);
            Assert.Equal(avatarBytes.Length, privateRead.ContentLength);
            Assert.False(string.IsNullOrWhiteSpace(privateRead.ETag));

            using var copy = new MemoryStream();
            await privateRead.Content.CopyToAsync(copy);
            Assert.Equal(avatarBytes, copy.ToArray());
        }

        // Verify nothing was written inside wwwroot.
        Assert.Empty(Directory.GetFileSystemEntries(webRoot));

        var deleted = await storage.DeleteIfExistsAsync(avatarKey, StorageVisibility.Private);
        Assert.True(deleted);
        Assert.Null(await storage.OpenReadAsync(avatarKey, StorageVisibility.Private));
    }

    [Fact]
    public void Constructor_WhenConfiguredInsideWwwroot_ThrowsInvalidOperationException()
    {
        var contentRoot = Path.Combine(_tempRoot, "misconfigured-app");
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        Directory.CreateDirectory(webRoot);

        Assert.Throws<InvalidOperationException>(() =>
            new LocalFileStorageService(
                new StorageOptions
                {
                    Local = new LocalStorageOptions
                    {
                        RootPath = "wwwroot/uploads"
                    }
                },
                contentRoot,
                webRoot,
                NullLogger<LocalFileStorageService>.Instance));
    }

    [Theory]
    [InlineData("../outside.webp")]
    [InlineData("avatars/../../outside.webp")]
    [InlineData("/avatars/rooted.webp")]
    [InlineData("avatars\\backslash.webp")]
    public async Task TraversalOrRootedKeys_AreBlockedAcrossPutReadAndDelete(string maliciousKey)
    {
        var contentRoot = Path.Combine(_tempRoot, "traversal-app");
        Directory.CreateDirectory(contentRoot);

        var storage = new LocalFileStorageService(
            new StorageOptions(),
            contentRoot,
            Path.Combine(contentRoot, "wwwroot"),
            NullLogger<LocalFileStorageService>.Instance);

        await using var data = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.PutAsync(new FileStoragePutRequest(
                maliciousKey,
                data,
                "image/webp",
                StorageVisibility.Private)));

        Assert.Null(await storage.OpenReadAsync(maliciousKey, StorageVisibility.Private));
        Assert.False(await storage.DeleteIfExistsAsync(maliciousKey, StorageVisibility.Private));
    }

    [Fact]
    public void ResolvePublicUrl_NormalizesObjectKeyForPublicAssets()
    {
        var contentRoot = Path.Combine(_tempRoot, "public-url-app");
        Directory.CreateDirectory(contentRoot);

        var storage = new LocalFileStorageService(
            new StorageOptions
            {
                Local = new LocalStorageOptions
                {
                    RootPath = "App_Data/uploads",
                    PublicRoutePrefix = "/Media/Public"
                }
            },
            contentRoot,
            null,
            NullLogger<LocalFileStorageService>.Instance);

        var resolved = storage.ResolvePublicUrl("Logos/Studio_42.webp");
        Assert.Equal("/Media/Public/logos/studio_42.webp", resolved);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup race in CI.
        }
    }
}
