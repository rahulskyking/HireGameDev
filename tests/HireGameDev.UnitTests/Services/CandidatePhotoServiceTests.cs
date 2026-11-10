using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Media;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class CandidatePhotoServiceTests
{
    [Fact]
    public async Task UploadReplaceAndRemove_UsesRelativeObjectKeys_UpdatesCompleteness_AndCleansUpOldObjects()
    {
        await using var harness = await PhotoTestHarness.CreateAsync(
            initialCompleteness: 20,
            isDiscoverable: false);

        await using var firstPng = await CreateSamplePngStreamAsync(200, 200);
        var uploadResult = await harness.PhotoService.UploadPhotoAsync(new UploadCandidatePhotoCommand(
            UserId: harness.OwnerUserId,
            Content: firstPng,
            FileName: "headshot.png",
            DeclaredContentType: "image/png"));

        Assert.True(uploadResult.IsSuccess, uploadResult.Error.Message);
        var firstKey = uploadResult.Value.PhotoObjectKey!;
        Assert.StartsWith("avatars/", firstKey, StringComparison.Ordinal);
        Assert.EndsWith(".webp", firstKey, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", firstKey, StringComparison.Ordinal);
        Assert.True(ObjectKeyRules.TryNormalize(firstKey, out _, out _));

        // Headline (+5) + City (+5) + Bio (+10) = 20 -> +5 Photo = 25%.
        Assert.Equal(25, uploadResult.Value.Completeness);
        Assert.True(harness.FakeStorage.ContainsPrivateObject(firstKey));
        Assert.Equal(0, harness.FakeStorage.PublicObjectCount);

        // Replacing photo writes a new key and deletes the previous key from storage.
        await using var secondPng = await CreateSamplePngStreamAsync(300, 300);
        var replaceResult = await harness.PhotoService.UploadPhotoAsync(new UploadCandidatePhotoCommand(
            UserId: harness.OwnerUserId,
            Content: secondPng,
            FileName: "updated.png",
            DeclaredContentType: "image/png"));

        Assert.True(replaceResult.IsSuccess, replaceResult.Error.Message);
        var secondKey = replaceResult.Value.PhotoObjectKey!;
        Assert.NotEqual(firstKey, secondKey);
        Assert.False(harness.FakeStorage.ContainsPrivateObject(firstKey));
        Assert.True(harness.FakeStorage.ContainsPrivateObject(secondKey));
        Assert.Equal(25, replaceResult.Value.Completeness);

        // Removing photo clears DB column, subtracts 5% completeness, and deletes the stored object.
        var removeResult = await harness.PhotoService.RemovePhotoAsync(harness.OwnerUserId);
        Assert.True(removeResult.IsSuccess, removeResult.Error.Message);
        Assert.Null(removeResult.Value.PhotoObjectKey);
        Assert.Equal(20, removeResult.Value.Completeness);
        Assert.False(harness.FakeStorage.ContainsPrivateObject(secondKey));

        var auditActions = await harness.DbContext.AuditLogs
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains("candidate.profile.photo_uploaded", auditActions);
        Assert.Contains("candidate.profile.photo_replaced", auditActions);
        Assert.Contains("candidate.profile.photo_removed", auditActions);
    }

    [Fact]
    public async Task OpenAuthorizedAvatarAsync_EnforcesOwnerAdminAndStudioDiscoverabilityRules()
    {
        await using var harness = await PhotoTestHarness.CreateAsync(
            initialCompleteness: 65,
            isDiscoverable: false);

        await using var png = await CreateSamplePngStreamAsync(240, 240);
        var uploaded = await harness.PhotoService.UploadPhotoAsync(new UploadCandidatePhotoCommand(
            UserId: harness.OwnerUserId,
            Content: png,
            FileName: "owner.png",
            DeclaredContentType: "image/png"));
        Assert.True(uploaded.IsSuccess);

        // Set controlled completeness/discoverability states for authorization assertions.
        var profile = await harness.DbContext.CandidateProfiles.SingleAsync(p => p.Id == harness.CandidateId);
        profile.Completeness = 65;
        profile.IsDiscoverable = false;
        await harness.DbContext.SaveChangesAsync();

        // 1. Anonymous viewer -> Unauthorized.
        var anonResult = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: false, UserId: null, IsAdmin: false, IsStudio: false));
        Assert.True(anonResult.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, anonResult.Error.Type);

        // 2. Owner viewer -> Allowed even while undiscoverable and < 70%.
        var ownerResult = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: harness.OwnerUserId, IsAdmin: false, IsStudio: false));
        Assert.True(ownerResult.IsSuccess);
        await ownerResult.Value.DisposeAsync();

        // 3. Admin viewer -> Allowed.
        var adminResult = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: true, IsStudio: false));
        Assert.True(adminResult.IsSuccess);
        await adminResult.Value.DisposeAsync();

        // 4. Another Candidate -> Forbidden.
        var otherCandidateResult = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: false, IsStudio: false));
        Assert.True(otherCandidateResult.IsFailure);
        Assert.Equal(ErrorType.Forbidden, otherCandidateResult.Error.Type);

        // 5. Studio when candidate is not discoverable -> Forbidden.
        var studioUndiscoverable = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: false, IsStudio: true));
        Assert.True(studioUndiscoverable.IsFailure);
        Assert.Equal(ErrorType.Forbidden, studioUndiscoverable.Error.Type);

        // 6. Studio when IsDiscoverable = true but Completeness = 65 (< 70) -> Forbidden.
        profile.IsDiscoverable = true;
        profile.Completeness = 65;
        await harness.DbContext.SaveChangesAsync();

        var studioBelowThreshold = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: false, IsStudio: true));
        Assert.True(studioBelowThreshold.IsFailure);
        Assert.Equal(ErrorType.Forbidden, studioBelowThreshold.Error.Type);

        // 7. Studio when IsDiscoverable = true and Completeness = 75 (>= 70) -> Allowed!
        profile.Completeness = 75;
        await harness.DbContext.SaveChangesAsync();

        var studioAllowed = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: false, IsStudio: true));
        Assert.True(studioAllowed.IsSuccess);
        await studioAllowed.Value.DisposeAsync();

        // 8. Soft-deleted candidate -> NotFound for everyone.
        profile.DeletedAt = DateTimeOffset.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        var deletedResult = await harness.PhotoService.OpenAuthorizedAvatarAsync(
            harness.CandidateId,
            new CandidateMediaViewer(IsAuthenticated: true, UserId: harness.OwnerUserId, IsAdmin: true, IsStudio: true));
        Assert.True(deletedResult.IsFailure);
        Assert.Equal(ErrorType.NotFound, deletedResult.Error.Type);
    }

    private static async Task<MemoryStream> CreateSamplePngStreamAsync(int width, int height)
    {
        var ms = new MemoryStream();
        using var img = new Image<Rgba32>(width, height, new Rgba32(60, 120, 200));
        await img.SaveAsync(ms, new PngEncoder());
        ms.Position = 0;
        return ms;
    }

    private sealed class PhotoTestHarness : IAsyncDisposable
    {
        private PhotoTestHarness(
            AppDbContext dbContext,
            InMemoryObjectStorageFake fakeStorage,
            CandidatePhotoService photoService,
            Guid ownerUserId,
            Guid candidateId)
        {
            DbContext = dbContext;
            FakeStorage = fakeStorage;
            PhotoService = photoService;
            OwnerUserId = ownerUserId;
            CandidateId = candidateId;
        }

        public AppDbContext DbContext { get; }
        public InMemoryObjectStorageFake FakeStorage { get; }
        public CandidatePhotoService PhotoService { get; }
        public Guid OwnerUserId { get; }
        public Guid CandidateId { get; }

        public static async Task<PhotoTestHarness> CreateAsync(short initialCompleteness, bool isDiscoverable)
        {
            var clock = new FixedTestClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"hgd-photo-tests-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var dbContext = new AppDbContext(options, clock);

            dbContext.Cities.Add(new City
            {
                Id = 1,
                Name = "Bengaluru",
                Slug = "bengaluru",
                State = "Karnataka",
                StateCode = "KA",
                CountryCode = "IN",
                IsHub = true,
                SortOrder = 1,
                IsActive = true
            });

            var ownerUserId = Guid.NewGuid();
            var candidateId = Guid.NewGuid();

            dbContext.CandidateProfiles.Add(new CandidateProfile
            {
                Id = candidateId,
                UserId = ownerUserId,
                FullName = "Meera Nair",
                Headline = "Technical Artist · Shaders & VFX",
                Bio = "Building stylized real-time shaders for PC and console titles.",
                PrimaryCategory = DisciplineCategory.Art,
                CityId = 1,
                Seniority = SeniorityLevel.Mid,
                YearsExperience = 4.0m,
                Availability = CandidateAvailability.ActivelyLooking,
                Completeness = initialCompleteness,
                OnboardingStep = 2,
                IsDiscoverable = isDiscoverable,
                LastActiveAt = clock.UtcNow
            });

            await dbContext.SaveChangesAsync();

            var fakeStorage = new InMemoryObjectStorageFake();
            var imageProcessor = new ImageSharpImageProcessor();
            var completenessCalculator = new CandidateCompletenessCalculator(dbContext);
            var auditLogger = new AuditLogger(dbContext, new NullIpAccessor(), clock);

            var photoService = new CandidatePhotoService(
                dbContext,
                imageProcessor,
                fakeStorage,
                completenessCalculator,
                auditLogger,
                clock,
                NullLogger<CandidatePhotoService>.Instance);

            return new PhotoTestHarness(
                dbContext,
                fakeStorage,
                photoService,
                ownerUserId,
                candidateId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class InMemoryObjectStorageFake : IFileStorageService
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _privateObjects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _publicObjects = new(StringComparer.Ordinal);

        public string ProviderName => "InMemoryR2CompatibleFake";

        public int PublicObjectCount => _publicObjects.Count;

        public bool ContainsPrivateObject(string key) => _privateObjects.ContainsKey(key);

        public async Task PutAsync(FileStoragePutRequest request, CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(request.ObjectKey, out var normalized, out var err))
            {
                throw new ArgumentException(err, nameof(request));
            }

            using var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, cancellationToken);
            var store = request.Visibility == StorageVisibility.Public ? _publicObjects : _privateObjects;
            store[normalized] = (ms.ToArray(), request.ContentType);
        }

        public Task<StoredObjectReadResult?> OpenReadAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out _))
            {
                return Task.FromResult<StoredObjectReadResult?>(null);
            }

            var store = visibility == StorageVisibility.Public ? _publicObjects : _privateObjects;
            if (!store.TryGetValue(normalized, out var entry))
            {
                return Task.FromResult<StoredObjectReadResult?>(null);
            }

            Stream stream = new MemoryStream(entry.Bytes, writable: false);
            return Task.FromResult<StoredObjectReadResult?>(new StoredObjectReadResult(
                Content: stream,
                ContentType: entry.ContentType,
                ContentLength: entry.Bytes.Length,
                ETag: $"\"{entry.Bytes.Length:x}\"",
                LastModifiedUtc: DateTimeOffset.UtcNow));
        }

        public Task<bool> DeleteIfExistsAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out _))
            {
                return Task.FromResult(false);
            }

            var store = visibility == StorageVisibility.Public ? _publicObjects : _privateObjects;
            return Task.FromResult(store.Remove(normalized));
        }

        public string ResolvePublicUrl(string objectKey)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out var err))
            {
                throw new ArgumentException(err, nameof(objectKey));
            }

            return $"https://cdn.hiregamedev.test/{normalized}";
        }
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => null;
    }
}
