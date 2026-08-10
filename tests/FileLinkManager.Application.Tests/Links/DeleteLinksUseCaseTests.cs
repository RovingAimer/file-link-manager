using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Tests.Links;

public sealed class DeleteLinksUseCaseTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeletedAt = new(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
    private static readonly DeviceIdentity CurrentDevice = new("device-current", "Current device refreshed");

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("33333333-3333-3333-3333-333333333333")]
    public async Task ExecuteAsync_WhenRecordIsNotCurrentAndActive_RejectsWithoutNativeDeletion(string recordId)
    {
        var current = CreateCurrentRecord("11111111-1111-1111-1111-111111111111", @"C:\Links\Current.txt");
        var remote = CreateRecord("22222222-2222-2222-2222-222222222222", "device-remote", @"C:\Links\Remote.txt");
        var deleted = CreateCurrentRecord("33333333-3333-3333-3333-333333333333", @"C:\Links\Deleted.txt") with
        {
            DeletedUtc = DeletedAt,
            Status = LinkStatus.Deleted
        };
        var links = new FakeFileLinkService();
        var store = new FakeLinkRecordStore { Database = DatabaseWith(current, remote, deleted) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(Guid.Parse(recordId), confirmMissing: false, CancellationToken.None);

        Assert.IsType<DeleteLinkRejected>(outcome);
        Assert.Empty(links.DeletedRecords);
        Assert.Empty(links.InspectedPaths);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLinkIsMissingWithoutConfirmation_PersistsFreshMissingStateAndRequiresConfirmation()
    {
        var record = CreateCurrentRecord("44444444-4444-4444-4444-444444444444", @"C:\Links\Missing.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath, null, LinkStatus.Missing, null, "The link entry no longer exists.");
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: false, CancellationToken.None);

        var confirmation = Assert.IsType<MissingConfirmationRequired>(outcome);
        Assert.Equal(record.Id, confirmation.Record.Id);
        var saved = SavedRecord(store, record.Id);
        Assert.Equal(LinkStatus.Missing, saved.Status);
        Assert.Equal("The link entry no longer exists.", saved.StatusReason);
        Assert.Equal(DeletedAt, saved.LastCheckedUtc);
        Assert.Null(saved.DeletedUtc);
        Assert.Empty(links.DeletedRecords);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMissingIsConfirmed_MarksDeletedWithoutCallingNativeDeletion()
    {
        var record = CreateCurrentRecord("55555555-5555-5555-5555-555555555555", @"C:\Links\Missing-confirmed.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = new LinkInspection(
            record.LinkRecord.Request.LinkPath, null, LinkStatus.Missing, null, "The link entry no longer exists.");
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: true, CancellationToken.None);

        var deleted = Assert.IsType<DeletedAndRegistered>(outcome);
        Assert.Equal(record.Id, deleted.Record.Id);
        var saved = SavedRecord(store, record.Id);
        Assert.Equal(LinkStatus.Deleted, saved.Status);
        Assert.Equal(DeletedAt, saved.DeletedUtc);
        Assert.Equal(DeletedAt, saved.LastCheckedUtc);
        Assert.Equal("The link entry no longer exists.", saved.StatusReason);
        Assert.Empty(links.DeletedRecords);
        Assert.Equal(new[] { record.LinkRecord.Request.LinkPath }, links.InspectedPaths);
    }

    [Theory]
    [InlineData(LinkStatus.Conflict, false, false, LinkStatus.Conflict)]
    [InlineData(LinkStatus.Unknown, false, false, LinkStatus.Unknown)]
    [InlineData(LinkStatus.Valid, true, false, LinkStatus.Conflict)]
    [InlineData(LinkStatus.Valid, false, true, LinkStatus.Conflict)]
    public async Task ExecuteAsync_WhenInspectionIsNotValidOrTheCreationFingerprint_RefusesNativeDeletion(
        LinkStatus inspectedStatus,
        bool mismatchedType,
        bool mismatchedFingerprint,
        LinkStatus expectedStatus)
    {
        var record = CreateCurrentRecord("66666666-6666-6666-6666-666666666666", @"C:\Links\Conflict.txt");
        var inspection = MatchingInspection(record) with { Status = inspectedStatus };
        if (inspectedStatus is LinkStatus.Conflict or LinkStatus.Unknown)
        {
            inspection = inspection with { Error = "The link path is no longer a supported link." };
        }
        else if (mismatchedType)
        {
            inspection = inspection with { Type = FileLinkType.FileSymbolicLink };
        }
        else if (mismatchedFingerprint)
        {
            inspection = inspection with { Fingerprint = record.LinkRecord.CreationFingerprint with { FileIdHex = "00000000" } };
        }

        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = inspection;
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: false, CancellationToken.None);

        Assert.IsType<DeleteLinkRejected>(outcome);
        var saved = SavedRecord(store, record.Id);
        Assert.Equal(expectedStatus, saved.Status);
        Assert.NotNull(saved.StatusReason);
        Assert.Null(saved.DeletedUtc);
        Assert.Empty(links.DeletedRecords);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidAndFingerprintMatches_DeletesOnlyPersistedLinkAndPreservesOtherSnapshots()
    {
        var record = CreateCurrentRecord("77777777-7777-7777-7777-777777777777", @"C:\Links\Delete.txt");
        var remote = CreateRecord("88888888-8888-8888-8888-888888888888", "device-remote", @"C:\Links\Remote.txt");
        var historical = CreateCurrentRecord("99999999-9999-9999-9999-999999999999", @"C:\Links\Historical.txt") with
        {
            DeletedUtc = CreatedAt.AddHours(1),
            Status = LinkStatus.Deleted
        };
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = MatchingInspection(record) with
        {
            LinkPath = record.LinkRecord.Request.SourcePath
        };
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record, remote, historical) };
        using var cancellation = new CancellationTokenSource();

        var outcome = await CreateUseCase(links, store, _ => true).ExecuteAsync(record.Id, confirmMissing: false, cancellation.Token);

        var deleted = Assert.IsType<DeletedAndRegistered>(outcome);
        Assert.Null(deleted.SourceCheckReason);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
        Assert.Equal(
            new[]
            {
                record.LinkRecord.Request.LinkPath,
                record.LinkRecord.Request.SourcePath
            },
            links.InspectedPaths);
        var saved = SavedRecord(store, record.Id);
        Assert.Equal(LinkStatus.Deleted, saved.Status);
        Assert.Equal(DeletedAt, saved.DeletedUtc);
        Assert.Equal(DeletedAt, saved.LastCheckedUtc);
        Assert.Null(saved.StatusReason);
        Assert.Equal(record.LinkRecord, saved.LinkRecord);
        Assert.Equal(record.CreatedUtc, saved.CreatedUtc);
        Assert.Equal(new[] { record.Id, remote.Id, historical.Id }, store.SavedDatabase!.Records.Select(item => item.Id));
        Assert.Equal(remote, store.SavedDatabase.Records[1]);
        Assert.Equal(historical, store.SavedDatabase.Records[2]);
        Assert.Equal(DatabaseWith(record, remote, historical).Devices, store.SavedDatabase.Devices);
        Assert.Equal(cancellation.Token, links.DeleteTokens.Single());
        Assert.Equal(CancellationToken.None, store.LastSaveToken);
    }

    [Theory]
    [InlineData(FileLinkType.DirectorySymbolicLink)]
    [InlineData(FileLinkType.DirectoryJunction)]
    public async Task ExecuteAsync_WhenDirectorySourceExists_RecordsCleanSourceCheck(FileLinkType linkType)
    {
        var record = CreateDirectoryRecord("ABABABAB-ABAB-ABAB-ABAB-ABABABABABAB", linkType);
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store, _ => true).ExecuteAsync(
            record.Id,
            confirmMissing: false,
            CancellationToken.None);

        var deleted = Assert.IsType<DeletedAndRegistered>(outcome);
        Assert.Null(deleted.SourceCheckReason);
        Assert.Equal(LinkStatus.Deleted, SavedRecord(store, record.Id).Status);
        Assert.Equal(new[] { record.LinkRecord.Request.LinkPath }, links.InspectedPaths);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHardLinkSourceIsMissing_RefusesNativeDeletionBeforeLastEntryCanBeRemoved()
    {
        var record = CreateCurrentRecord("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", @"C:\Links\Source-check.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = new LinkInspection(
            record.LinkRecord.Request.SourcePath,
            null,
            LinkStatus.Missing,
            null,
            "The source could not be found before deleting the link.");
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: false, CancellationToken.None);

        var rejected = Assert.IsType<DeleteLinkRejected>(outcome);
        Assert.Contains("source", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LinkStatus.Conflict, SavedRecord(store, record.Id).Status);
        Assert.Contains("before deletion", SavedRecord(store, record.Id).StatusReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(links.DeletedRecords);
        Assert.Equal(
            new[] { record.LinkRecord.Request.LinkPath, record.LinkRecord.Request.SourcePath },
            links.InspectedPaths);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHardLinkSourceIsReplaced_RefusesNativeDeletion()
    {
        var record = CreateCurrentRecord("A1A1A1A1-A1A1-A1A1-A1A1-A1A1A1A1A1A1", @"C:\Links\Source-replaced.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = MatchingInspection(record) with
        {
            LinkPath = record.LinkRecord.Request.SourcePath,
            Fingerprint = record.LinkRecord.CreationFingerprint with { FileIdHex = "00000000" }
        };
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: false, CancellationToken.None);

        Assert.IsType<DeleteLinkRejected>(outcome);
        Assert.Equal(LinkStatus.Conflict, SavedRecord(store, record.Id).Status);
        Assert.Contains("file ID", SavedRecord(store, record.Id).StatusReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(links.DeletedRecords);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDirectorySourceDisappearsAfterDeletion_PersistsActionableReasonWithoutDeletingSource()
    {
        var record = CreateDirectoryRecord("A2A2A2A2-A2A2-A2A2-A2A2-A2A2A2A2A2A2", FileLinkType.DirectoryJunction);
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store, _ => false).ExecuteAsync(
            record.Id,
            confirmMissing: false,
            CancellationToken.None);

        var deleted = Assert.IsType<DeletedAndRegistered>(outcome);
        Assert.Contains("source", deleted.SourceCheckReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not be found or accessed", SavedRecord(store, record.Id).StatusReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
        Assert.DoesNotContain(record.LinkRecord.Request.SourcePath, links.DeletedPaths);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSavingAfterNativeDeletionFails_ReturnsExplicitUnregisteredOutcome()
    {
        var record = CreateCurrentRecord("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB", @"C:\Links\Save-failure.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = MatchingInspection(record) with
        {
            LinkPath = record.LinkRecord.Request.SourcePath
        };
        var store = new FakeLinkRecordStore
        {
            Database = DatabaseWith(record),
            SaveException = new LinkRecordStoreException("records.json", "Disk is full.")
        };

        var outcome = await CreateUseCase(links, store, _ => true).ExecuteAsync(record.Id, confirmMissing: false, CancellationToken.None);

        var pending = Assert.IsType<DeletedButUnregistered>(outcome);
        Assert.Equal(record.Id, pending.Record.Id);
        Assert.Contains("Disk is full", pending.StorageError, StringComparison.Ordinal);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_DoesNotLoadInspectOrDelete()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var record = CreateCurrentRecord("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC", @"C:\Links\Cancelled.txt");
        var links = new FakeFileLinkService();
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateUseCase(links, store).ExecuteAsync(record.Id, confirmMissing: false, cancellation.Token));

        Assert.Equal(0, store.LoadCalls);
        Assert.Empty(links.InspectedPaths);
        Assert.Empty(links.DeletedRecords);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancelsAfterNativeDeletion_CompletesRegistration()
    {
        using var cancellation = new CancellationTokenSource();
        var record = CreateCurrentRecord("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD", @"C:\Links\Cancelled-after-delete.txt");
        var links = new FakeFileLinkService { AfterDelete = _ => cancellation.Cancel() };
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = MatchingInspection(record) with
        {
            LinkPath = record.LinkRecord.Request.SourcePath
        };
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await CreateUseCase(links, store, _ => true).ExecuteAsync(
            record.Id,
            confirmMissing: false,
            cancellation.Token);

        Assert.IsType<DeletedAndRegistered>(outcome);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
        Assert.Equal(LinkStatus.Deleted, SavedRecord(store, record.Id).Status);
        Assert.Equal(1, store.SaveCalls);
        Assert.False(store.LastSaveToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPostCommitRegistrationThrowsCancellation_ReturnsDeletedButUnregistered()
    {
        var record = CreateCurrentRecord("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE", @"C:\Links\Cancelled-registration.txt");
        var links = new FakeFileLinkService();
        links.Inspections[record.LinkRecord.Request.LinkPath] = MatchingInspection(record);
        links.Inspections[record.LinkRecord.Request.SourcePath] = MatchingInspection(record) with
        {
            LinkPath = record.LinkRecord.Request.SourcePath
        };
        var store = new FakeLinkRecordStore
        {
            Database = DatabaseWith(record),
            SaveException = new OperationCanceledException("Registration cancelled.")
        };

        var outcome = await CreateUseCase(links, store, _ => true).ExecuteAsync(
            record.Id,
            confirmMissing: false,
            CancellationToken.None);

        var pending = Assert.IsType<DeletedButUnregistered>(outcome);
        Assert.Equal(record.Id, pending.Record.Id);
        Assert.Equal("Registration cancelled.", pending.StorageError);
        Assert.Equal(new[] { record.LinkRecord }, links.DeletedRecords);
        Assert.Equal(1, store.SaveCalls);
        Assert.False(store.LastSaveToken.IsCancellationRequested);
    }

    private static DeleteLinksUseCase CreateUseCase(
        FakeFileLinkService links,
        FakeLinkRecordStore store,
        Func<string, bool>? sourcePathExists = null) =>
        new(
            links,
            store,
            new FakeDeviceIdentityProvider(CurrentDevice),
            new FixedTimeProvider(DeletedAt),
            sourcePathExists);

    private static LinkDatabase DatabaseWith(params PersistedLinkRecord[] records) =>
        new(
            LinkDatabase.CurrentSchemaVersion,
            new[] { new DeviceIdentity("device-current", "Old current device"), new DeviceIdentity("device-remote", "Remote device") },
            records);

    private static PersistedLinkRecord CreateCurrentRecord(string id, string linkPath) =>
        CreateRecord(id, "device-current", linkPath);

    private static PersistedLinkRecord CreateRecord(string id, string deviceKey, string linkPath) =>
        new(
            Guid.Parse(id),
            deviceKey,
            deviceKey,
            new LinkRecord(
                new LinkRequest(@"C:\Sources\Source.txt", linkPath, FileLinkType.FileHardLink),
                new LinkFingerprint(null, @"C:\Sources\Source.txt", 42, "AABBCCDD", linkPath)),
            CreatedAt,
            CreatedAt,
            null,
            LinkStatus.Valid,
            null);

    private static PersistedLinkRecord CreateDirectoryRecord(string id, FileLinkType linkType)
    {
        var linkPath = @"C:\Links\Directory-delete.txt";
        var fingerprint = new LinkFingerprint(
            linkType == FileLinkType.DirectoryJunction ? 0xA0000003u : 0xA000000Cu,
            @"C:\Sources\Directory",
            null,
            null,
            linkPath);
        return new PersistedLinkRecord(
            Guid.Parse(id),
            "device-current",
            "device-current",
            new LinkRecord(
                new LinkRequest(@"C:\Sources\Directory", linkPath, linkType),
                fingerprint),
            CreatedAt,
            CreatedAt,
            null,
            LinkStatus.Valid,
            null);
    }

    private static LinkInspection MatchingInspection(PersistedLinkRecord record) =>
        new(
            record.LinkRecord.Request.LinkPath,
            record.LinkRecord.Request.Type,
            LinkStatus.Valid,
            record.LinkRecord.CreationFingerprint,
            null);

    private static PersistedLinkRecord SavedRecord(FakeLinkRecordStore store, Guid id) =>
        Assert.Single(store.SavedDatabase!.Records, item => item.Id == id);

    private sealed class FakeFileLinkService : IFileLinkService
    {
        public Dictionary<string, LinkInspection> Inspections { get; } = new(StringComparer.Ordinal);

        public List<string> InspectedPaths { get; } = new();

        public List<LinkRecord> DeletedRecords { get; } = new();

        public List<string> DeletedPaths => DeletedRecords.Select(record => record.Request.LinkPath).ToList();

        public List<CancellationToken> DeleteTokens { get; } = new();

        public Action<CancellationToken>? AfterDelete { get; init; }

        public LinkValidationResult Validate(LinkRequest request) => LinkValidationResult.Success;

        public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken)
        {
            InspectedPaths.Add(linkPath);
            cancellationToken.ThrowIfCancellationRequested();
            return Inspections.TryGetValue(linkPath, out var inspection)
                ? Task.FromResult(inspection)
                : throw new InvalidOperationException($"No inspection was configured for '{linkPath}'.");
        }

        public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken)
        {
            DeletedRecords.Add(record);
            DeleteTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AfterDelete?.Invoke(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore
    {
        public LinkDatabase Database { get; init; } = LinkDatabase.Empty();

        public Exception? SaveException { get; init; }

        public int LoadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public CancellationToken LastSaveToken { get; private set; }

        public LinkDatabase? SavedDatabase { get; private set; }

        public Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Database);
        }

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSaveToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            SavedDatabase = database;
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
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
