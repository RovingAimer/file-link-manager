using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Tests.Links;

public sealed class RemoveDeletedLinkRecordUseCaseTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_RemovesOnlyExactDeviceAndRecordIdentity()
    {
        var sharedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var target = CreateRecord(sharedId, "Device-A", LinkStatus.Deleted, deleted: true);
        var caseVariant = CreateRecord(sharedId, "device-a", LinkStatus.Deleted, deleted: true);
        var otherId = CreateRecord(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Device-A",
            LinkStatus.Deleted,
            deleted: true);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(target, caseVariant, otherId) };

        var outcome = await new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
            "Device-A",
            sharedId,
            CancellationToken.None);

        var removed = Assert.IsType<DeletedLinkRecordRemoved>(outcome);
        Assert.Equal(target, removed.RemovedRecord);
        Assert.Equal(new[] { caseVariant, otherId }, store.Database.Records);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsDeletedUtcEvenWhenLegacyStatusIsNotDeleted()
    {
        var record = CreateRecord(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Device-A",
            LinkStatus.Valid,
            deleted: true);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
            record.DeviceKey,
            record.Id,
            CancellationToken.None);

        Assert.IsType<DeletedLinkRecordRemoved>(outcome);
        Assert.Empty(store.Database.Records);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsActiveRecordWithoutSaving()
    {
        var record = CreateRecord(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Device-A",
            LinkStatus.Valid,
            deleted: false);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
            record.DeviceKey,
            record.Id,
            CancellationToken.None);

        var rejected = Assert.IsType<RemoveDeletedLinkRecordRejected>(outcome);
        Assert.Contains("已删除", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(new[] { record }, rejected.Database.Records);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdentityDoesNotMatchCaseExactly_RejectsWithoutSaving()
    {
        var record = CreateRecord(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Device-A",
            LinkStatus.Deleted,
            deleted: true);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        var outcome = await new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
            "device-a",
            record.Id,
            CancellationToken.None);

        var rejected = Assert.IsType<RemoveDeletedLinkRecordRejected>(outcome);
        Assert.Contains("找不到", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_DoesNotLoadOrSave()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var record = CreateRecord(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Device-A",
            LinkStatus.Deleted,
            deleted: true);
        var store = new FakeLinkRecordStore { Database = DatabaseWith(record) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
                record.DeviceKey,
                record.Id,
                cancellation.Token));

        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSaveFails_PropagatesStorageFailureAndKeepsDatabase()
    {
        var record = CreateRecord(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "Device-A",
            LinkStatus.Deleted,
            deleted: true);
        var store = new FakeLinkRecordStore
        {
            Database = DatabaseWith(record),
            SaveException = new LinkRecordStoreException("records.json", "保存失败。")
        };

        await Assert.ThrowsAsync<LinkRecordStoreException>(
            () => new RemoveDeletedLinkRecordUseCase(store).ExecuteAsync(
                record.DeviceKey,
                record.Id,
                CancellationToken.None));

        Assert.Equal(new[] { record }, store.Database.Records);
        Assert.Equal(1, store.SaveCount);
    }

    private static LinkDatabase DatabaseWith(params PersistedLinkRecord[] records) => new(
        LinkDatabase.CurrentSchemaVersion,
        new[]
        {
            new DeviceIdentity("Device-A", "Device A"),
            new DeviceIdentity("device-a", "Case Variant")
        },
        records);

    private static PersistedLinkRecord CreateRecord(
        Guid id,
        string deviceKey,
        LinkStatus status,
        bool deleted)
    {
        var request = new LinkRequest(
            $@"C:\Sources\{id:N}.txt",
            $@"C:\Links\{deviceKey}\{id:N}.txt",
            FileLinkType.FileHardLink);
        return new PersistedLinkRecord(
            id,
            deviceKey,
            deviceKey,
            new LinkRecord(request, new LinkFingerprint(null, request.SourcePath, 1, id.ToString("N"), request.LinkPath)),
            Timestamp,
            Timestamp,
            deleted ? Timestamp : null,
            status,
            status == LinkStatus.Valid ? null : "Stored status reason.");
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore
    {
        public LinkDatabase Database { get; set; } = LinkDatabase.Empty();

        public Exception? SaveException { get; set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(Database);
        }

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            Database = database;
            return Task.CompletedTask;
        }
    }
}
