using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Tests.Links;

public sealed class InspectLinksUseCaseTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CheckedAt = new(2026, 8, 8, 13, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity CurrentDevice = new("device-current", "Current device refreshed");

    [Fact]
    public async Task ExecuteAsync_WithMatchingHardLinkAndReparseFingerprints_UpdatesCheckDataWithoutReplacingCreationIdentity()
    {
        var hardLink = CreateRecord(
            "11111111-1111-1111-1111-111111111111",
            "device-current",
            CreateRequest(@"C:\Canonical\Hard-source.txt", @"C:\Canonical\Hard-link.txt", FileLinkType.FileHardLink),
            new LinkFingerprint(null, @"C:\Canonical\Hard-source.txt", 42, "AABBCCDD", @"C:\Canonical\Hard-link.txt"));
        var symbolicLink = CreateRecord(
            "22222222-2222-2222-2222-222222222222",
            "device-current",
            CreateRequest(@"C:\Canonical\Target", @"C:\Canonical\Directory-link", FileLinkType.DirectorySymbolicLink),
            new LinkFingerprint(0xA000000C, @"C:\Canonical\Target", null, null, @"C:\Canonical\Directory-link"));
        var links = new FakeFileLinkService();
        links.InspectionsByPath[hardLink.LinkRecord.Request.LinkPath] = new LinkInspection(
            hardLink.LinkRecord.Request.LinkPath,
            FileLinkType.FileHardLink,
            LinkStatus.Valid,
            hardLink.LinkRecord.CreationFingerprint with { NormalizedTargetPath = @"C:\INSPECTED\SOURCE.TXT" },
            null);
        links.InspectionsByPath[symbolicLink.LinkRecord.Request.LinkPath] = new LinkInspection(
            symbolicLink.LinkRecord.Request.LinkPath,
            FileLinkType.DirectorySymbolicLink,
            LinkStatus.Valid,
            symbolicLink.LinkRecord.CreationFingerprint with { NormalizedTargetPath = @"c:\canonical\target" },
            null);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(hardLink, symbolicLink) };
        var useCase = CreateUseCase(links, store);

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, result.InspectedCount);
        Assert.Equal(new[] { @"C:\Canonical\Hard-link.txt", @"C:\Canonical\Directory-link" }, links.InspectedPaths);
        var savedHardLink = Assert.Single(result.Database.Records, record => record.Id == hardLink.Id);
        var savedSymbolicLink = Assert.Single(result.Database.Records, record => record.Id == symbolicLink.Id);
        Assert.Equal(LinkStatus.Valid, savedHardLink.Status);
        Assert.Equal(LinkStatus.Valid, savedSymbolicLink.Status);
        Assert.Equal(CheckedAt, savedHardLink.LastCheckedUtc);
        Assert.Equal(CheckedAt, savedSymbolicLink.LastCheckedUtc);
        Assert.Equal(hardLink.LinkRecord.CreationFingerprint, savedHardLink.LinkRecord.CreationFingerprint);
        Assert.Equal(symbolicLink.LinkRecord.CreationFingerprint, savedSymbolicLink.LinkRecord.CreationFingerprint);
        Assert.Equal(hardLink.LinkRecord.Request, savedHardLink.LinkRecord.Request);
        Assert.Equal(symbolicLink.LinkRecord.Request, savedSymbolicLink.LinkRecord.Request);
        Assert.Equal(hardLink.CreatedUtc, savedHardLink.CreatedUtc);
        Assert.Null(savedHardLink.DeletedUtc);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsMissingUnknownAndConflictInspectionReasons()
    {
        var missing = CreateCurrentRecord("33333333-3333-3333-3333-333333333333", @"C:\Links\Missing.txt");
        var unknown = CreateCurrentRecord("44444444-4444-4444-4444-444444444444", @"C:\Links\Unknown.txt");
        var conflict = CreateCurrentRecord("55555555-5555-5555-5555-555555555555", @"C:\Links\Conflict.txt");
        var links = new FakeFileLinkService();
        links.InspectionsByPath[missing.LinkRecord.Request.LinkPath] = new LinkInspection(missing.LinkRecord.Request.LinkPath, null, LinkStatus.Missing, null, "File no longer exists.");
        links.InspectionsByPath[unknown.LinkRecord.Request.LinkPath] = new LinkInspection(unknown.LinkRecord.Request.LinkPath, null, LinkStatus.Unknown, null, "Access was denied.");
        links.InspectionsByPath[conflict.LinkRecord.Request.LinkPath] = new LinkInspection(conflict.LinkRecord.Request.LinkPath, FileLinkType.FileHardLink, LinkStatus.Conflict, null, "Path is a regular file.");
        var store = new FakeLinkRecordStore { Database = DatabaseWith(missing, unknown, conflict) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        Assert.Equal((LinkStatus.Missing, "File no longer exists."), RecordState(result.Database, missing.Id));
        Assert.Equal((LinkStatus.Unknown, "Access was denied."), RecordState(result.Database, unknown.Id));
        Assert.Equal((LinkStatus.Conflict, "Path is a regular file."), RecordState(result.Database, conflict.Id));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneInspectionThrows_MarksItUnknownAndContinuesTheBatch()
    {
        var failed = CreateCurrentRecord("56565656-5656-5656-5656-565656565656", @"C:\Links\Throws.txt");
        var healthy = CreateCurrentRecord("57575757-5757-5757-5757-575757575757", @"C:\Links\Healthy.txt");
        var links = new FakeFileLinkService();
        links.ExceptionsByPath[failed.LinkRecord.Request.LinkPath] = new InvalidOperationException("The link inspection failed unexpectedly.");
        links.InspectionsByPath[healthy.LinkRecord.Request.LinkPath] = MatchingInspection(healthy);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(failed, healthy) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, result.InspectedCount);
        Assert.Equal(new[] { failed.LinkRecord.Request.LinkPath, healthy.LinkRecord.Request.LinkPath }, links.InspectedPaths);
        Assert.Equal(1, store.SaveCalls);

        var failedResult = Assert.Single(result.Database.Records, record => record.Id == failed.Id);
        Assert.Equal(LinkStatus.Unknown, failedResult.Status);
        Assert.Equal("The link inspection failed unexpectedly.", failedResult.StatusReason);
        Assert.Equal(CheckedAt, failedResult.LastCheckedUtc);
        Assert.Equal(failed.LinkRecord.Request, failedResult.LinkRecord.Request);
        Assert.Equal(failed.LinkRecord.CreationFingerprint, failedResult.LinkRecord.CreationFingerprint);

        var healthyResult = Assert.Single(result.Database.Records, record => record.Id == healthy.Id);
        Assert.Equal(LinkStatus.Valid, healthyResult.Status);
        Assert.Equal(CheckedAt, healthyResult.LastCheckedUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WithValidHardLinkWhoseVolumeOrFileIdDoesNotMatch_PersistsConflict(bool volumeDoesNotMatch)
    {
        var record = CreateCurrentRecord("66666666-6666-6666-6666-666666666666", @"C:\Links\Hard.txt");
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath,
            FileLinkType.FileHardLink,
            LinkStatus.Valid,
            record.LinkRecord.CreationFingerprint with
            {
                VolumeSerialNumber = volumeDoesNotMatch ? 99UL : 42UL,
                FileIdHex = volumeDoesNotMatch ? "AABBCCDD" : "00000000"
            },
            null);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        var inspected = Assert.Single(result.Database.Records);
        Assert.Equal(LinkStatus.Conflict, inspected.Status);
        Assert.Contains("volume", inspected.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(record.LinkRecord.CreationFingerprint, inspected.LinkRecord.CreationFingerprint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WithValidReparseLinkWhoseTagOrTargetDoesNotMatch_PersistsConflict(bool tagDoesNotMatch)
    {
        var record = CreateRecord(
            "77777777-7777-7777-7777-777777777777",
            "device-current",
            CreateRequest(@"C:\Targets\Original", @"C:\Links\Junction", FileLinkType.DirectoryJunction),
            new LinkFingerprint(0xA0000003, @"C:\Targets\Original", null, null, @"C:\Links\Junction"));
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath,
            FileLinkType.DirectoryJunction,
            LinkStatus.Valid,
            record.LinkRecord.CreationFingerprint with
            {
                ReparseTag = tagDoesNotMatch ? 0xA000000C : 0xA0000003,
                NormalizedTargetPath = tagDoesNotMatch ? @"C:\Targets\Original" : @"C:\Targets\Other"
            },
            null);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        var inspected = Assert.Single(result.Database.Records);
        Assert.Equal(LinkStatus.Conflict, inspected.Status);
        Assert.Contains("reparse", inspected.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(record.LinkRecord.CreationFingerprint, inspected.LinkRecord.CreationFingerprint);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInspectionWhoseTypeDoesNotMatch_PersistsConflict()
    {
        var record = CreateCurrentRecord("F0F0F0F0-F0F0-F0F0-F0F0-F0F0F0F0F0F0", @"C:\Links\Type.txt");
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath,
            FileLinkType.FileSymbolicLink,
            LinkStatus.Valid,
            record.LinkRecord.CreationFingerprint,
            null);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        var inspected = Assert.Single(result.Database.Records);
        Assert.Equal(LinkStatus.Conflict, inspected.Status);
        Assert.Contains("type", inspected.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(record.LinkRecord.CreationFingerprint, inspected.LinkRecord.CreationFingerprint);
    }

    [Fact]
    public async Task ExecuteAsync_InspectsOnlyCurrentNonDeletedRecordsAndPreservesSkippedSnapshotsInOrder()
    {
        var current = CreateCurrentRecord("88888888-8888-8888-8888-888888888888", @"C:\Links\Current.txt");
        var remote = CreateRecord("99999999-9999-9999-9999-999999999999", "device-remote", CreateRequest(@"C:\Remote\Source.txt", @"C:\Remote\Link.txt", FileLinkType.FileHardLink), HardFingerprint(@"C:\Remote\Source.txt", @"C:\Remote\Link.txt"));
        var deletedByTimestamp = CreateCurrentRecord("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", @"C:\Links\Deleted-time.txt") with { DeletedUtc = CreatedAt.AddHours(1) };
        var logicallyDeleted = CreateCurrentRecord("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB", @"C:\Links\Deleted-status.txt") with { Status = LinkStatus.Deleted };
        var caseVariantDevice = CreateRecord("B0B0B0B0-B0B0-B0B0-B0B0-B0B0B0B0B0B0", "DEVICE-CURRENT", CreateRequest(@"C:\Case\Source.txt", @"C:\Case\Link.txt", FileLinkType.FileHardLink), HardFingerprint(@"C:\Case\Source.txt", @"C:\Case\Link.txt"));
        var links = new FakeFileLinkService();
        links.InspectionsByPath[current.LinkRecord.Request.LinkPath] = MatchingInspection(current);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(current, remote, deletedByTimestamp, logicallyDeleted, caseVariantDevice) };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.InspectedCount);
        Assert.Equal(new[] { current.LinkRecord.Request.LinkPath }, links.InspectedPaths);
        Assert.Equal(new[] { current.Id, remote.Id, deletedByTimestamp.Id, logicallyDeleted.Id, caseVariantDevice.Id }, result.Database.Records.Select(record => record.Id));
        Assert.Equal(remote, result.Database.Records[1]);
        Assert.Equal(deletedByTimestamp, result.Database.Records[2]);
        Assert.Equal(logicallyDeleted, result.Database.Records[3]);
        Assert.Equal(caseVariantDevice, result.Database.Records[4]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_RefreshesOrAppendsCurrentDeviceMetadataWithoutReorderingOtherDevices(bool currentDeviceExists)
    {
        var record = CreateCurrentRecord("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC", @"C:\Links\Metadata.txt");
        var existingDevices = currentDeviceExists
            ? new[] { new DeviceIdentity("device-before", "Before"), new DeviceIdentity("device-current", "Old name"), new DeviceIdentity("device-after", "After") }
            : new[] { new DeviceIdentity("device-before", "Before"), new DeviceIdentity("device-after", "After") };
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore
        {
            Database = new LinkDatabase(LinkDatabase.CurrentSchemaVersion, existingDevices, new[] { record })
        };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        var expected = currentDeviceExists
            ? new[] { new DeviceIdentity("device-before", "Before"), CurrentDevice, new DeviceIdentity("device-after", "After") }
            : new[] { new DeviceIdentity("device-before", "Before"), new DeviceIdentity("device-after", "After"), CurrentDevice };
        Assert.Equal(expected, result.Database.Devices);
        Assert.Equal(result.Database, store.SavedDatabase);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMergeCaseVariantDeviceKeys()
    {
        var record = CreateCurrentRecord("D0D0D0D0-D0D0-D0D0-D0D0-D0D0D0D0D0D0", @"C:\Links\Case-device.txt");
        var caseVariantDevice = new DeviceIdentity("DEVICE-CURRENT", "Case variant");
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore
        {
            Database = new LinkDatabase(
                LinkDatabase.CurrentSchemaVersion,
                new[] { new DeviceIdentity("device-before", "Before"), caseVariantDevice },
                new[] { record })
        };

        var result = await CreateUseCase(links, store).ExecuteAsync(CancellationToken.None);

        Assert.Equal(
            new[] { new DeviceIdentity("device-before", "Before"), caseVariantDevice, CurrentDevice },
            result.Database.Devices);
    }

    [Fact]
    public async Task ExecuteAsync_PassesCanonicalPathAndCallerCancellationTokenToEachInspection()
    {
        var record = CreateRecord(
            "DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD",
            "device-current",
            CreateRequest(@"C:\Canonical\Source.txt", @"C:\Canonical\Link.txt", FileLinkType.FileHardLink),
            HardFingerprint(@"C:\Canonical\Source.txt", @"C:\Canonical\Link.txt"));
        var links = new FakeFileLinkService();
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };
        using var cancellation = new CancellationTokenSource();

        await CreateUseCase(links, store).ExecuteAsync(cancellation.Token);

        Assert.Equal(new[] { @"C:\Canonical\Link.txt" }, links.InspectedPaths);
        Assert.Equal(new[] { cancellation.Token }, links.InspectionTokens);
        Assert.Equal(cancellation.Token, store.LastLoadToken);
        Assert.Equal(cancellation.Token, store.LastSaveToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_DoesNotLoadOrSave()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var links = new FakeFileLinkService();
        var store = new FakeLinkRecordStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateUseCase(links, store).ExecuteAsync(cancellation.Token));

        Assert.Empty(links.InspectedPaths);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledAfterInspection_DoesNotSavePartialUpdate()
    {
        using var cancellation = new CancellationTokenSource();
        var record = CreateCurrentRecord("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE", @"C:\Links\Cancelled.txt");
        var links = new FakeFileLinkService
        {
            AfterInspection = _ => cancellation.Cancel()
        };
        links.InspectionsByPath[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateUseCase(links, store).ExecuteAsync(cancellation.Token));

        Assert.Equal(new[] { record.LinkRecord.Request.LinkPath }, links.InspectedPaths);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
        Assert.Null(store.SavedDatabase);
    }

    private static InspectLinksUseCase CreateUseCase(FakeFileLinkService links, FakeLinkRecordStore store) =>
        new(links, store, new FakeDeviceIdentityProvider(CurrentDevice), new FixedTimeProvider(CheckedAt));

    private static LinkDatabase DatabaseWith(params PersistedLinkRecord[] records) =>
        new(LinkDatabase.CurrentSchemaVersion, new[] { new DeviceIdentity("device-current", "Old current device"), new DeviceIdentity("device-remote", "Remote device") }, records);

    private static PersistedLinkRecord CreateCurrentRecord(string id, string linkPath) =>
        CreateRecord(id, "device-current", CreateRequest(@"C:\Sources\Source.txt", linkPath, FileLinkType.FileHardLink), HardFingerprint(@"C:\Sources\Source.txt", linkPath));

    private static PersistedLinkRecord CreateRecord(string id, string deviceKey, LinkRequest request, LinkFingerprint fingerprint) =>
        new(Guid.Parse(id), deviceKey, deviceKey, new LinkRecord(request, fingerprint), CreatedAt, CreatedAt, null, LinkStatus.Valid, null);

    private static LinkRequest CreateRequest(string sourcePath, string linkPath, FileLinkType type) => new(sourcePath, linkPath, type);

    private static LinkFingerprint HardFingerprint(string sourcePath, string linkPath) =>
        new(null, sourcePath, 42, "AABBCCDD", linkPath);

    private static LinkInspection MatchingInspection(PersistedLinkRecord record) =>
        new(record.LinkRecord.Request.LinkPath, record.LinkRecord.Request.Type, LinkStatus.Valid, record.LinkRecord.CreationFingerprint, null);

    private static (LinkStatus Status, string? Reason) RecordState(LinkDatabase database, Guid id)
    {
        var record = Assert.Single(database.Records, item => item.Id == id);
        return (record.Status, record.StatusReason);
    }

    private sealed class FakeFileLinkService : IFileLinkService
    {
        public Dictionary<string, LinkInspection> InspectionsByPath { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Exception> ExceptionsByPath { get; } = new(StringComparer.Ordinal);

        public List<string> InspectedPaths { get; } = new();

        public List<CancellationToken> InspectionTokens { get; } = new();

        public Action<CancellationToken>? AfterInspection { get; init; }

        public LinkValidationResult Validate(LinkRequest request) => LinkValidationResult.Success;

        public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken)
        {
            InspectedPaths.Add(linkPath);
            InspectionTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (ExceptionsByPath.TryGetValue(linkPath, out var exception))
            {
                throw exception;
            }

            if (!InspectionsByPath.TryGetValue(linkPath, out var inspection))
            {
                throw new InvalidOperationException($"No inspection was configured for '{linkPath}'.");
            }

            AfterInspection?.Invoke(cancellationToken);
            return Task.FromResult(inspection);
        }

        public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore
    {
        public LinkDatabase Database { get; init; } = LinkDatabase.Empty();

        public int LoadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public CancellationToken LastLoadToken { get; private set; }

        public CancellationToken LastSaveToken { get; private set; }

        public LinkDatabase? SavedDatabase { get; private set; }

        public Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            LastLoadToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Database);
        }

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSaveToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            SavedDatabase = database;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeviceIdentityProvider(DeviceIdentity identity) : IDeviceIdentityProvider
    {
        public DeviceIdentity GetCurrent() => identity;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
