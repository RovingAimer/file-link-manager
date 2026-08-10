using FileLinkManager.Application.Abstractions;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Tests.Links;

public sealed class CreateAndRetryRegistrationUseCaseTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly LinkRequest OriginalRequest = new(
        @"C:\Input\Original.txt",
        @"C:\Links\Original-Link.txt",
        FileLinkType.FileHardLink);
    private static readonly LinkRequest CanonicalRequest = new(
        @"C:\Canonical\Source.txt",
        @"C:\Canonical\Link.txt",
        FileLinkType.FileHardLink);

    [Fact]
    public async Task CreateAsync_WithInvalidValidation_ShortCircuitsBeforeNativeCreationOrStorageAccess()
    {
        var links = new FakeFileLinkService
        {
            Validation = new LinkValidationResult(false, new[] { "The source is invalid." })
        };
        var store = new FakeLinkRecordStore();
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, CancellationToken.None);

        var invalid = Assert.IsType<CreateLinkValidationFailed>(outcome);
        Assert.Equal(new[] { "The source is invalid." }, invalid.Validation.Errors);
        Assert.Equal(0, links.CreateCalls);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WithValidCreatedLink_PersistsCanonicalRecordAndRefreshesCurrentDeviceMetadata()
    {
        var links = new FakeFileLinkService { CreateInspection = CreateValidInspection() };
        var existingRecord = CreateRecord("device-remote", "Remote Device");
        var database = new LinkDatabase(
            LinkDatabase.CurrentSchemaVersion,
            new[]
            {
                new DeviceIdentity("device-current", "Old display name"),
                new DeviceIdentity("device-remote", "Remote Device")
            },
            new[] { existingRecord });
        var store = new FakeLinkRecordStore { Database = database };
        using var cancellation = new CancellationTokenSource();
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, cancellation.Token);

        var registered = Assert.IsType<CreatedAndRegistered>(outcome);
        var record = Assert.Single(store.SavedDatabase!.Records, item => item.Id == registered.Record.Id);
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("device-current", record.DeviceKey);
        Assert.Equal("Current Device", record.DeviceDisplayNameAtCreation);
        Assert.Equal(CanonicalRequest, record.LinkRecord.Request);
        Assert.Equal(CreateValidInspection().Fingerprint, record.LinkRecord.CreationFingerprint);
        Assert.Equal(Timestamp, record.CreatedUtc);
        Assert.Equal(Timestamp, record.LastCheckedUtc);
        Assert.Null(record.DeletedUtc);
        Assert.Equal(LinkStatus.Valid, record.Status);
        Assert.Null(record.StatusReason);
        Assert.Equal(
            new[]
            {
                new DeviceIdentity("device-current", "Current Device"),
                new DeviceIdentity("device-remote", "Remote Device")
            },
            store.SavedDatabase.Devices);
        Assert.Equal(new[] { existingRecord.Id, record.Id }, store.SavedDatabase.Records.Select(item => item.Id));
        Assert.Equal(cancellation.Token, links.LastCreateToken);
        Assert.Equal(CancellationToken.None, store.LastLoadToken);
        Assert.Equal(CancellationToken.None, store.LastSaveToken);
    }

    [Fact]
    public async Task CreateAsync_WhenNativeCreationThrows_DoesNotAccessStorage()
    {
        var links = new FakeFileLinkService { CreateException = new InvalidOperationException("Native create failed.") };
        var store = new FakeLinkRecordStore();
        var useCase = CreateUseCase(links, store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(OriginalRequest, CancellationToken.None));

        Assert.Equal("Native create failed.", exception.Message);
        Assert.Equal(1, links.CreateCalls);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WithUnusableInspection_DoesNotSave()
    {
        var inspection = CreateValidInspection() with { Status = LinkStatus.Conflict };
        var links = new FakeFileLinkService { CreateInspection = inspection };
        var store = new FakeLinkRecordStore();
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, CancellationToken.None);

        var failed = Assert.IsType<CreateLinkInspectionFailed>(outcome);
        Assert.Same(inspection, failed.Inspection);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenRegistrationSaveFails_ReturnsCreatedButUnregisteredWithRetryData()
    {
        var inspection = CreateValidInspection();
        var links = new FakeFileLinkService { CreateInspection = inspection };
        var store = new FakeLinkRecordStore { SaveException = new LinkRecordStoreException("data.json", "Disk full.") };
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, CancellationToken.None);

        var pending = Assert.IsType<CreatedButUnregistered>(outcome);
        Assert.Same(OriginalRequest, pending.Request);
        Assert.Same(inspection, pending.Inspection);
        Assert.Contains("Disk full", pending.RegistrationError, StringComparison.Ordinal);
        Assert.Equal(1, links.CreateCalls);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenRegistrationLoadFails_ReturnsCreatedButUnregisteredWithStorageReason()
    {
        var inspection = CreateValidInspection();
        var links = new FakeFileLinkService { CreateInspection = inspection };
        var store = new FakeLinkRecordStore
        {
            LoadException = new LinkRecordStoreException("data.json", "The data file is unavailable.")
        };
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, CancellationToken.None);

        var pending = Assert.IsType<CreatedButUnregistered>(outcome);
        Assert.Contains("unavailable", pending.RegistrationError, StringComparison.Ordinal);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenRegistrationThrowsNonStorageException_PropagatesIt()
    {
        var links = new FakeFileLinkService { CreateInspection = CreateValidInspection() };
        var store = new FakeLinkRecordStore
        {
            LoadException = new InvalidOperationException("Unexpected application failure.")
        };
        var useCase = CreateUseCase(links, store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(OriginalRequest, CancellationToken.None));

        Assert.Equal("Unexpected application failure.", exception.Message);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithValidPendingRegistration_PersistsItWithoutCallingNativeCreationAndAddsCurrentDevice()
    {
        var existingRecord = CreateRecord("device-remote", "Remote Device");
        var store = new FakeLinkRecordStore
        {
            Database = new LinkDatabase(
                LinkDatabase.CurrentSchemaVersion,
                new[] { new DeviceIdentity("device-remote", "Remote Device") },
                new[] { existingRecord })
        };
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, CreateValidInspection(), CancellationToken.None);

        var registered = Assert.IsType<RetryRegistrationSucceeded>(outcome);
        Assert.Equal(CanonicalRequest, registered.Record.LinkRecord.Request);
        Assert.Equal(new[] { "device-remote", "device-current" }, store.SavedDatabase!.Devices.Select(device => device.DeviceKey));
        Assert.Equal(new[] { existingRecord.Id, registered.Record.Id }, store.SavedDatabase.Records.Select(record => record.Id));
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, store.SaveCalls);
    }

    [Theory]
    [InlineData(LinkStatus.Missing)]
    [InlineData(LinkStatus.Valid)]
    public async Task RetryAsync_WithUnusableInspection_RejectsWithoutSaving(LinkStatus status)
    {
        var inspection = status == LinkStatus.Valid
            ? CreateValidInspection() with { Fingerprint = null }
            : CreateValidInspection() with { Status = status };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, inspection, CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("valid", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithMalformedRequest_RejectsBeforeStorageAccess()
    {
        var malformedRequest = OriginalRequest with { SourcePath = " " };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(malformedRequest, CreateValidInspection(), CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("non-empty", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithMismatchedInspectionType_RejectsWithoutSaving()
    {
        var inspection = CreateValidInspection() with { Type = FileLinkType.DirectorySymbolicLink };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, inspection, CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("type", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithMissingInspectionType_RejectsWithoutSaving()
    {
        var inspection = CreateValidInspection() with { Type = null };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, inspection, CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("type", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithInspectionPathMismatchingFingerprint_RejectsWithoutSaving()
    {
        var inspection = CreateValidInspection() with { LinkPath = @"C:\Canonical\Other-Link.txt" };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, inspection, CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("path", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WithRelativeFingerprintPath_RejectsBeforeStorageAccess()
    {
        var fingerprint = CreateValidInspection().Fingerprint! with
        {
            NormalizedTargetPath = "relative-source.txt"
        };
        var inspection = CreateValidInspection() with { Fingerprint = fingerprint };
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        var outcome = await retry.ExecuteAsync(OriginalRequest, inspection, CancellationToken.None);

        var rejected = Assert.IsType<RetryRegistrationRejected>(outcome);
        Assert.Contains("absolute", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenAlreadyCancelled_ThrowsBeforeValidationAndEverySideEffect()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var links = new FakeFileLinkService();
        var store = new FakeLinkRecordStore();
        var useCase = CreateUseCase(links, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(OriginalRequest, cancellation.Token));

        Assert.Equal(0, links.ValidateCalls);
        Assert.Equal(0, links.CreateCalls);
        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task RetryAsync_WhenAlreadyCancelled_ThrowsBeforeStorageAccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new FakeLinkRecordStore();
        var retry = CreateRetryUseCase(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => retry.ExecuteAsync(OriginalRequest, CreateValidInspection(), cancellation.Token));

        Assert.Equal(0, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenCallerCancelsAfterNativeCreation_CompletesRegistration()
    {
        using var cancellation = new CancellationTokenSource();
        var links = new FakeFileLinkService
        {
            CreateInspection = CreateValidInspection(),
            AfterCreate = _ => cancellation.Cancel()
        };
        var store = new FakeLinkRecordStore();
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, cancellation.Token);

        Assert.IsType<CreatedAndRegistered>(outcome);
        Assert.Equal(1, links.CreateCalls);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, store.SaveCalls);
        Assert.False(store.LastLoadToken.IsCancellationRequested);
        Assert.False(store.LastSaveToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CreateAsync_WhenPostCommitRegistrationThrowsCancellation_ReturnsCreatedButUnregistered()
    {
        var links = new FakeFileLinkService { CreateInspection = CreateValidInspection() };
        var store = new FakeLinkRecordStore { LoadException = new OperationCanceledException("Registration cancelled.") };
        var useCase = CreateUseCase(links, store);

        var outcome = await useCase.ExecuteAsync(OriginalRequest, CancellationToken.None);

        var pending = Assert.IsType<CreatedButUnregistered>(outcome);
        Assert.Same(OriginalRequest, pending.Request);
        Assert.Same(links.CreateInspection, pending.Inspection);
        Assert.Equal("Registration cancelled.", pending.RegistrationError);
        Assert.Equal(1, links.CreateCalls);
        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
    }

    private static CreateLinkUseCase CreateUseCase(FakeFileLinkService links, FakeLinkRecordStore store) =>
        new(links, store, new FakeDeviceIdentityProvider(new DeviceIdentity("device-current", "Current Device")), new FixedTimeProvider(Timestamp));

    private static RetryRegistrationUseCase CreateRetryUseCase(FakeLinkRecordStore store) =>
        new(store, new FakeDeviceIdentityProvider(new DeviceIdentity("device-current", "Current Device")), new FixedTimeProvider(Timestamp));

    private static LinkInspection CreateValidInspection() => new(
        CanonicalRequest.LinkPath,
        CanonicalRequest.Type,
        LinkStatus.Valid,
        new LinkFingerprint(null, CanonicalRequest.SourcePath, 1, "0123456789ABCDEF0123456789ABCDEF", CanonicalRequest.LinkPath),
        null);

    private static PersistedLinkRecord CreateRecord(string deviceKey, string displayName) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        deviceKey,
        displayName,
        new LinkRecord(CanonicalRequest, CreateValidInspection().Fingerprint!),
        Timestamp,
        Timestamp,
        null,
        LinkStatus.Valid,
        null);

    private sealed class FakeFileLinkService : IFileLinkService
    {
        public LinkValidationResult Validation { get; init; } = LinkValidationResult.Success;

        public LinkInspection CreateInspection { get; init; } = CreateValidInspection();

        public Exception? CreateException { get; init; }

        public Action<CancellationToken>? AfterCreate { get; init; }

        public int ValidateCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public CancellationToken LastCreateToken { get; private set; }

        public LinkValidationResult Validate(LinkRequest request)
        {
            ValidateCalls++;
            return Validation;
        }

        public Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastCreateToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return CreateException is not null
                ? Task.FromException<LinkInspection>(CreateException)
                : CreateAfterNativeCreation();

            Task<LinkInspection> CreateAfterNativeCreation()
            {
                AfterCreate?.Invoke(cancellationToken);
                return Task.FromResult(CreateInspection);
            }
        }

        public Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken) =>
            Task.FromResult(CreateInspection);

        public Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeLinkRecordStore : ILinkRecordStore
    {
        public LinkDatabase Database { get; init; } = LinkDatabase.Empty();

        public Exception? LoadException { get; init; }

        public Exception? SaveException { get; init; }

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
            return LoadException is not null
                ? Task.FromException<LinkDatabase>(LoadException)
                : Task.FromResult(Database);
        }

        public Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSaveToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            SavedDatabase = database;
            return SaveException is not null
                ? Task.FromException(SaveException)
                : Task.CompletedTask;
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
