using System.Text.Json;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;
using FileLinkManager.Infrastructure.Storage;

namespace FileLinkManager.Infrastructure.Tests.Storage;

public sealed class JsonLinkRecordStoreTests
{
    [Fact]
    public async Task Load_LiteralLegacyCanonicalRequestWinsWhenConsistentWithFingerprint()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "link-records.json"),
                LegacyCanonicalRequestJson);

            var loaded = await new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None);
            var request = Assert.Single(loaded.Records).LinkRecord.Request;

            Assert.Equal(new LinkRequest(
                @"C:\canonical\source.txt",
                @"C:\canonical\link.txt",
                FileLinkType.FileHardLink), request);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_LiteralLegacyWithoutCanonicalRequestUsesCompleteAbsoluteFingerprintPaths()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "link-records.json"),
                LegacyWithoutCanonicalRequestJson);

            var loaded = await new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None);
            var request = Assert.Single(loaded.Records).LinkRecord.Request;

            Assert.Equal(@"C:\canonical\source.txt", request.SourcePath);
            Assert.Equal(@"C:\canonical\link.txt", request.LinkPath);
            Assert.Equal(FileLinkType.FileHardLink, request.Type);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_LiteralLegacyRelativeRequestWithoutCanonicalEvidenceIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "link-records.json"),
                LegacyRelativeWithoutCanonicalJson);

            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_LiteralLegacyConflictingCanonicalRequestIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "link-records.json"),
                LegacyConflictingCanonicalRequestJson);

            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_WritesSingleRequestThatOldReaderCanValidate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new JsonLinkRecordStore(root);
            await store.SaveAsync(CreateDatabaseWithCanonicalRequest(), CancellationToken.None);
            var json = await File.ReadAllTextAsync(Path.Combine(root, "link-records.json"));

            Assert.DoesNotContain("\"canonicalRequest\"", json, StringComparison.OrdinalIgnoreCase);
            var oldPayload = JsonSerializer.Deserialize<FrozenLegacyDatabase>(json, LegacyOptions);
            Assert.NotNull(oldPayload);
            Assert.Equal(LinkDatabase.CurrentSchemaVersion, oldPayload.SchemaVersion);
            var saved = Assert.Single(oldPayload.Records).LinkRecord;

            Assert.Equal(
                saved.Request.LinkPath,
                saved.CreationFingerprint.NormalizedLinkPath,
                ignoreCase: true);
            Assert.Equal(
                saved.Request.SourcePath,
                saved.CreationFingerprint.NormalizedTargetPath,
                ignoreCase: true);
            Assert.Null(saved.CanonicalRequest);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsSingleCanonicalRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var expected = CreateDatabase("first");
            var store = new JsonLinkRecordStore(root);

            await store.SaveAsync(expected, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);
            var json = await File.ReadAllTextAsync(Path.Combine(root, "link-records.json"));

            var record = Assert.Single(loaded.Records);
            Assert.False(store.LastLoadDiagnostics.IsReadOnly);
            Assert.Equal(expected.Devices, loaded.Devices);
            Assert.Equal(expected.Records, loaded.Records);
            Assert.Equal(@"C:\\canonical\\source.txt", record.LinkRecord.Request.SourcePath);
            Assert.Equal(@"C:\\canonical\\link.txt", record.LinkRecord.Request.LinkPath);
            Assert.DoesNotContain("\"canonicalRequest\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2026-08-08T09:30:00+00:00", json, StringComparison.Ordinal);

            await store.SaveAsync(CreateDatabase("normal-primary"), CancellationToken.None);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_ReplacingPrimary_CreatesBackupWithPreviousValidJson()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new JsonLinkRecordStore(root);
            await store.SaveAsync(CreateDatabase("first"), CancellationToken.None);
            var originalJson = await File.ReadAllTextAsync(Path.Combine(root, "link-records.json"));

            await store.SaveAsync(CreateDatabase("second"), CancellationToken.None);

            var backupJson = await File.ReadAllTextAsync(Path.Combine(root, "link-records.json.bak"));
            Assert.Equal(originalJson, backupJson);
            Assert.Contains("second", await File.ReadAllTextAsync(Path.Combine(root, "link-records.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_ConcurrentStoreInstances_LeavesPrimaryAndBackupAsReadableDatabases()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var saves = Enumerable.Range(0, 24)
                .Select(index => new JsonLinkRecordStore(root).SaveAsync(CreateDatabase($"concurrent-{index}"), CancellationToken.None));

            await Task.WhenAll(saves);

            var primary = await new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None);
            var backupRoot = Path.Combine(root, "backup-verification");
            Directory.CreateDirectory(backupRoot);
            File.Copy(Path.Combine(root, "link-records.json.bak"), Path.Combine(backupRoot, "link-records.json"));
            var backup = await new JsonLinkRecordStore(backupRoot).LoadAsync(CancellationToken.None);

            Assert.Single(primary.Records);
            Assert.Single(backup.Records);
            Assert.StartsWith("concurrent-", primary.Records[0].StatusReason, StringComparison.Ordinal);
            Assert.StartsWith("concurrent-", backup.Records[0].StatusReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_WhenNamedRootMutexIsHeld_CancellationIsObserved_AndSubsequentSaveSucceeds()
    {
        var root = CreateTemporaryDirectory();
        var ownerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerTask = Task.Run(() =>
        {
            using var rootMutex = new Mutex(initiallyOwned: false, JsonLinkRecordStore.GetMutexNameForTesting(root));
            if (!rootMutex.WaitOne(TimeSpan.FromSeconds(5)))
            {
                ownerReady.SetException(new TimeoutException("The test mutex could not be acquired."));
                return;
            }

            ownerReady.SetResult(true);
            try
            {
                ownerRelease.Task.GetAwaiter().GetResult();
            }
            finally
            {
                rootMutex.ReleaseMutex();
            }
        });

        try
        {
            await ownerReady.Task;
            var store = new JsonLinkRecordStore(root);
            using var cancellation = new CancellationTokenSource();
            var pendingSave = store.SaveAsync(CreateDatabase("blocked"), cancellation.Token);

            await Task.Delay(200);
            Assert.False(pendingSave.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingSave);
        }
        finally
        {
            ownerRelease.TrySetResult(true);
            await ownerTask;
        }

        try
        {
            var followUpStore = new JsonLinkRecordStore(root);
            await followUpStore.SaveAsync(CreateDatabase("after-cancellation"), CancellationToken.None);
            Assert.Equal(
                "after-cancellation",
                (await followUpStore.LoadAsync(CancellationToken.None)).Records[0].StatusReason);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_AllowsAtomicReplacementWhilePrimaryIsOpenForRead()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new JsonLinkRecordStore(root);
            await store.SaveAsync(CreateDatabase("original"), CancellationToken.None);
            var primary = Path.Combine(root, "link-records.json");

            await using (var readHandle = new FileStream(
                primary,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete))
            {
                await store.SaveAsync(CreateDatabase("replacement"), CancellationToken.None);
            }

            Assert.Contains("replacement", await File.ReadAllTextAsync(primary), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_WhenBackupCannotBeAtomicallyInstalled_PreservesPrimaryAndBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new JsonLinkRecordStore(root);
            await store.SaveAsync(CreateDatabase("original"), CancellationToken.None);
            await store.SaveAsync(CreateDatabase("current"), CancellationToken.None);
            var primary = Path.Combine(root, "link-records.json");
            var backup = Path.Combine(root, "link-records.json.bak");
            var originalPrimary = await File.ReadAllTextAsync(primary);
            var originalBackup = await File.ReadAllTextAsync(backup);

            await using (var backupLock = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Assert.ThrowsAsync<LinkRecordStoreException>(() => store.SaveAsync(CreateDatabase("replacement"), CancellationToken.None));
            }

            Assert.Equal(originalPrimary, await File.ReadAllTextAsync(primary));
            Assert.Equal(originalBackup, await File.ReadAllTextAsync(backup));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_WhenBackupInstallFaultIsInjected_PreservesBothExistingFilesAndCleansTemps()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var setupStore = new JsonLinkRecordStore(root);
            await setupStore.SaveAsync(CreateDatabase("original"), CancellationToken.None);
            await setupStore.SaveAsync(CreateDatabase("current"), CancellationToken.None);

            var primary = Path.Combine(root, "link-records.json");
            var backup = Path.Combine(root, "link-records.json.bak");
            var originalPrimary = await File.ReadAllTextAsync(primary);
            var originalBackup = await File.ReadAllTextAsync(backup);
            var failBackupInstallation = true;
            var faultingStore = new JsonLinkRecordStore(
                root,
                (temporaryPath, destinationPath) =>
                {
                    if (failBackupInstallation &&
                        string.Equals(destinationPath, backup, StringComparison.OrdinalIgnoreCase))
                    {
                        failBackupInstallation = false;
                        throw new IOException("Injected backup installation failure.");
                    }

                    File.Move(temporaryPath, destinationPath, overwrite: true);
                });

            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => faultingStore.SaveAsync(CreateDatabase("replacement"), CancellationToken.None));

            Assert.Equal(originalPrimary, await File.ReadAllTextAsync(primary));
            Assert.Equal(originalBackup, await File.ReadAllTextAsync(backup));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
            Assert.True(faultingStore.LastLoadDiagnostics.IsReadOnly);
            Assert.Contains("read-only", faultingStore.LastLoadDiagnostics.Message, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => faultingStore.SaveAsync(CreateDatabase("still-read-only"), CancellationToken.None));

            var loaded = await faultingStore.LoadAsync(CancellationToken.None);
            Assert.Equal("current", Assert.Single(loaded.Records).StatusReason);
            Assert.False(faultingStore.LastLoadDiagnostics.IsReadOnly);

            await faultingStore.SaveAsync(CreateDatabase("after-clean-load"), CancellationToken.None);
            Assert.Contains("after-clean-load", await File.ReadAllTextAsync(primary), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_RejectsDatabaseWithSemanticIntegrityViolations()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var valid = CreateDatabase("valid");
            var invalid = valid with
            {
                SchemaVersion = LinkDatabase.CurrentSchemaVersion + 1,
                Devices = new[] { new DeviceIdentity("", "") },
                Records = new[]
                {
                    valid.Records[0] with
                    {
                        Id = Guid.Empty,
                        DeviceKey = "",
                        DeviceDisplayNameAtCreation = "",
                        CreatedUtc = new DateTimeOffset(2026, 8, 8, 9, 30, 0, TimeSpan.FromHours(8)),
                        DeletedUtc = null,
                        Status = LinkStatus.Deleted
                    }
                }
            };

            await Assert.ThrowsAsync<LinkRecordStoreException>(() => new JsonLinkRecordStore(root).SaveAsync(invalid, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(root, "link-records.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_RejectsRecordWithRelativePathsAndInconsistentFingerprint()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var request = new LinkRequest("relative-source.txt", @"C:\\canonical\\link.txt", FileLinkType.FileHardLink);
            var fingerprint = new LinkFingerprint(null, @"C:\\canonical\\source.txt", 42, "A1B2C3D4", @"C:\\canonical\\link.txt");
            var invalid = CreateDatabase("invalid") with
            {
                Records = new[]
                {
                    CreateDatabase("invalid").Records[0] with
                    {
                        LinkRecord = new LinkRecord(request, fingerprint)
                    }
                }
            };

            await Assert.ThrowsAsync<LinkRecordStoreException>(() => new JsonLinkRecordStore(root).SaveAsync(invalid, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(root, "link-records.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_RejectsNonUtcDeletedLifecycleAndMismatchedRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var valid = CreateDatabase("invalid-lifecycle");
            var record = valid.Records[0];
            var request = record.LinkRecord.Request with { SourcePath = @"C:\\canonical\\different.txt" };
            var invalid = valid with
            {
                Records = new[]
                {
                    record with
                    {
                        LinkRecord = new LinkRecord(request, record.LinkRecord.CreationFingerprint),
                        DeletedUtc = new DateTimeOffset(2026, 8, 8, 9, 36, 0, TimeSpan.FromHours(8)),
                        Status = LinkStatus.Deleted
                    }
                }
            };

            await Assert.ThrowsAsync<LinkRecordStoreException>(() => new JsonLinkRecordStore(root).SaveAsync(invalid, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(root, "link-records.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("schema-below-current")]
    [InlineData("device-empty-key")]
    [InlineData("device-empty-display")]
    [InlineData("device-duplicate-key")]
    [InlineData("record-empty-id")]
    [InlineData("record-empty-device-key")]
    [InlineData("record-empty-device-display")]
    [InlineData("record-unknown-device")]
    [InlineData("record-relative-source")]
    [InlineData("record-relative-link")]
    [InlineData("request-unknown-type")]
    [InlineData("status-unknown")]
    [InlineData("created-non-utc")]
    [InlineData("last-checked-non-utc")]
    [InlineData("deleted-non-utc")]
    [InlineData("deleted-without-timestamp")]
    [InlineData("active-with-timestamp")]
    [InlineData("deleted-before-created")]
    [InlineData("missing-fingerprint")]
    [InlineData("fingerprint-source-mismatch")]
    [InlineData("fingerprint-link-mismatch")]
    [InlineData("schema-future")]
    public async Task Save_RejectsEachSemanticInvariantIndividually(string variant)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => new JsonLinkRecordStore(root).SaveAsync(CreateInvalidDatabase(variant), CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(root, "link-records.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("schema-below-current")]
    [InlineData("device-empty-key")]
    [InlineData("device-empty-display")]
    [InlineData("device-duplicate-key")]
    [InlineData("record-empty-id")]
    [InlineData("record-empty-device-key")]
    [InlineData("record-empty-device-display")]
    [InlineData("record-unknown-device")]
    [InlineData("request-unknown-type")]
    [InlineData("status-unknown")]
    [InlineData("created-non-utc")]
    [InlineData("last-checked-non-utc")]
    [InlineData("deleted-non-utc")]
    [InlineData("deleted-without-timestamp")]
    [InlineData("active-with-timestamp")]
    [InlineData("deleted-before-created")]
    [InlineData("missing-fingerprint")]
    public async Task Load_RejectsEachSemanticInvariantIndividuallyWithoutOverwritingSource(string variant)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var primary = Path.Combine(root, "link-records.json");
            var invalidJson = JsonSerializer.Serialize(CreateInvalidDatabase(variant), JsonOptions);
            await File.WriteAllTextAsync(primary, invalidJson);

            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None));

            Assert.Equal(invalidJson, await File.ReadAllTextAsync(primary));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_SemanticallyMalformedPrimary_RejectsWithoutOverwritingSource()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var primary = Path.Combine(root, "link-records.json");
            await new JsonLinkRecordStore(root).SaveAsync(CreateDatabase("valid"), CancellationToken.None);
            var malformedJson = (await File.ReadAllTextAsync(primary)).Replace("device-key", string.Empty, StringComparison.Ordinal);
            await File.WriteAllTextAsync(primary, malformedJson);

            await Assert.ThrowsAsync<LinkRecordStoreException>(() => new JsonLinkRecordStore(root).LoadAsync(CancellationToken.None));

            Assert.Equal(malformedJson, await File.ReadAllTextAsync(primary));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_MalformedPrimaryWithValidBackup_PreservesPrimaryAndReportsReadOnlyRecovery()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var primary = Path.Combine(root, "link-records.json");
            var backup = Path.Combine(root, "link-records.json.bak");
            const string malformedJson = "{ malformed";
            await File.WriteAllTextAsync(primary, malformedJson);
            await File.WriteAllTextAsync(backup, JsonSerializer.Serialize(CreateDatabase("backup"), JsonOptions));
            var store = new JsonLinkRecordStore(root);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal("backup", Assert.Single(loaded.Records).StatusReason);
            var diagnostics = Assert.IsAssignableFrom<ILinkRecordStoreDiagnostics>(store).LastLoadDiagnostics;
            Assert.True(diagnostics.RecoveredFromBackup);
            Assert.True(diagnostics.IsReadOnly);
            Assert.Equal(malformedJson, await File.ReadAllTextAsync(primary));
            await Assert.ThrowsAsync<LinkRecordStoreException>(() => store.SaveAsync(CreateDatabase("replacement"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_FutureSchema_ReturnsDatabaseReadOnlyWithoutOverwritingPrimary()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var primary = Path.Combine(root, "link-records.json");
            const string futureJson = "{\"schemaVersion\":999,\"devices\":[],\"records\":[]}";
            await File.WriteAllTextAsync(primary, futureJson);
            var store = new JsonLinkRecordStore(root);

            var loaded = await store.LoadAsync(CancellationToken.None);
            var diagnostics = Assert.IsAssignableFrom<ILinkRecordStoreDiagnostics>(store).LastLoadDiagnostics;

            Assert.Equal(999, loaded.SchemaVersion);
            Assert.True(diagnostics.IsReadOnly);
            Assert.Contains("newer", diagnostics.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(futureJson, await File.ReadAllTextAsync(primary));
            await Assert.ThrowsAsync<LinkRecordStoreException>(() => store.SaveAsync(CreateDatabase("replacement"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_WhenPrimaryCannotBeRead_ReportsReadOnlyStorageDiagnostics()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var primary = Path.Combine(root, "link-records.json");
            await new JsonLinkRecordStore(root).SaveAsync(CreateDatabase("valid"), CancellationToken.None);
            var store = new JsonLinkRecordStore(root);

            await using (var primaryLock = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAsync<LinkRecordStoreException>(() => store.LoadAsync(CancellationToken.None));
            }

            Assert.True(store.LastLoadDiagnostics.IsReadOnly);
            Assert.NotNull(store.LastLoadDiagnostics.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Load_MissingPrimaryReturnsEmptyAndSaveUsesInjectedRoot()
    {
        var parent = CreateTemporaryDirectory();
        var root = Path.Combine(parent, "injected-data-root");
        try
        {
            var store = new JsonLinkRecordStore(root);
            SetLoadDiagnosticsForTest(
                store,
                new LinkRecordStoreLoadDiagnostics(false, true, "Injected read-only diagnostic."));

            await Assert.ThrowsAsync<LinkRecordStoreException>(
                () => store.SaveAsync(CreateDatabase("diagnostic-read-only"), CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, "link-records.json")));

            var empty = await store.LoadAsync(CancellationToken.None);
            Assert.False(store.LastLoadDiagnostics.IsReadOnly);
            await store.SaveAsync(CreateDatabase("root"), CancellationToken.None);

            Assert.Equal(LinkDatabase.CurrentSchemaVersion, empty.SchemaVersion);
            Assert.Empty(empty.Records);
            Assert.True(File.Exists(Path.Combine(root, "link-records.json")));
            Assert.False(File.Exists(Path.Combine(parent, "link-records.json")));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task LoadAndSave_CancelledToken_ThrowOperationCanceledException()
    {
        var parent = CreateTemporaryDirectory();
        var root = Path.Combine(parent, "cancelled-data-root");
        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var store = new JsonLinkRecordStore(root);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(CreateDatabase("cancel"), cancelled.Token));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task Save_WhenReplacementFails_PreservesPrimaryAndCleansTemporaryFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = new JsonLinkRecordStore(root);
            await store.SaveAsync(CreateDatabase("original"), CancellationToken.None);
            var primary = Path.Combine(root, "link-records.json");
            var originalJson = await File.ReadAllTextAsync(primary);
            await using (var lockHandle = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Assert.ThrowsAsync<LinkRecordStoreException>(() => store.SaveAsync(CreateDatabase("replacement"), CancellationToken.None));

                Assert.Equal(originalJson, await File.ReadAllTextAsync(primary));
                Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static LinkDatabase CreateDatabase(string statusReason)
    {
        var request = new LinkRequest(@"C:\\canonical\\source.txt", @"C:\\canonical\\link.txt", FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(null, @"C:\\canonical\\source.txt", 42, "A1B2C3D4", @"C:\\canonical\\link.txt");
        var record = new PersistedLinkRecord(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "device-key",
            "Portable PC",
            new LinkRecord(request, fingerprint),
            new DateTimeOffset(2026, 8, 8, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 8, 9, 35, 0, TimeSpan.Zero),
            null,
            LinkStatus.Valid,
            statusReason);

        return new LinkDatabase(
            LinkDatabase.CurrentSchemaVersion,
            new[] { new DeviceIdentity("device-key", "Portable PC") },
            new[] { record });
    }

    private static LinkDatabase CreateDatabaseWithCanonicalRequest() => new(
        LinkDatabase.CurrentSchemaVersion,
        new[] { new DeviceIdentity("device-key", "Portable PC") },
        new[]
        {
            new PersistedLinkRecord(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "device-key",
                "Portable PC",
                new LinkRecord(
                    new LinkRequest(
                        @"C:\\canonical\\source.txt",
                        @"C:\\canonical\\link.txt",
                        FileLinkType.FileHardLink),
                    new LinkFingerprint(
                        null,
                        @"C:\\canonical\\source.txt",
                        42,
                        "A1B2C3D4",
                        @"C:\\canonical\\link.txt")),
                new DateTimeOffset(2026, 8, 8, 9, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 8, 9, 35, 0, TimeSpan.Zero),
                null,
                LinkStatus.Valid,
                "canonical")
        });

    private static LinkDatabase CreateInvalidDatabase(string variant)
    {
        var valid = CreateDatabase(variant);
        var record = valid.Records[0];
        var linkRecord = record.LinkRecord;

        return variant switch
        {
            "schema-below-current" => valid with { SchemaVersion = 0 },
            "schema-future" => valid with { SchemaVersion = LinkDatabase.CurrentSchemaVersion + 1 },
            "device-empty-key" => valid with { Devices = new[] { new DeviceIdentity("", "Portable PC") } },
            "device-empty-display" => valid with { Devices = new[] { new DeviceIdentity("device-key", "") } },
            "device-duplicate-key" => valid with
            {
                Devices = new[]
                {
                    new DeviceIdentity("device-key", "Portable PC"),
                    new DeviceIdentity("device-key", "Second PC")
                }
            },
            "record-empty-id" => WithRecord(valid, record with { Id = Guid.Empty }),
            "record-empty-device-key" => WithRecord(valid, record with { DeviceKey = "" }),
            "record-empty-device-display" => WithRecord(valid, record with { DeviceDisplayNameAtCreation = "" }),
            "record-unknown-device" => WithRecord(valid, record with { DeviceKey = "other-device" }),
            "record-relative-source" => WithLinkRecord(
                valid,
                new LinkRecord(
                    linkRecord.Request with { SourcePath = "relative-source.txt" },
                    linkRecord.CreationFingerprint)),
            "record-relative-link" => WithLinkRecord(
                valid,
                new LinkRecord(
                    linkRecord.Request with { LinkPath = "relative-link.txt" },
                    linkRecord.CreationFingerprint)),
            "request-unknown-type" => WithLinkRecord(
                valid,
                new LinkRecord(
                    linkRecord.Request with { Type = (FileLinkType)999 },
                    linkRecord.CreationFingerprint)),
            "status-unknown" => WithRecord(valid, record with { Status = (LinkStatus)999 }),
            "created-non-utc" => WithRecord(
                valid,
                record with { CreatedUtc = new DateTimeOffset(2026, 8, 8, 9, 30, 0, TimeSpan.FromHours(8)) }),
            "last-checked-non-utc" => WithRecord(
                valid,
                record with { LastCheckedUtc = new DateTimeOffset(2026, 8, 8, 9, 35, 0, TimeSpan.FromHours(8)) }),
            "deleted-non-utc" => WithRecord(
                valid,
                record with
                {
                    Status = LinkStatus.Deleted,
                    DeletedUtc = new DateTimeOffset(2026, 8, 8, 9, 36, 0, TimeSpan.FromHours(8))
                }),
            "deleted-without-timestamp" => WithRecord(valid, record with { Status = LinkStatus.Deleted }),
            "active-with-timestamp" => WithRecord(
                valid,
                record with
                {
                    DeletedUtc = new DateTimeOffset(2026, 8, 8, 9, 36, 0, TimeSpan.Zero)
                }),
            "deleted-before-created" => WithRecord(
                valid,
                record with
                {
                    Status = LinkStatus.Deleted,
                    DeletedUtc = new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero)
                }),
            "missing-fingerprint" => WithRecord(valid, record with { LinkRecord = null! }),
            "fingerprint-source-mismatch" => WithLinkRecord(
                valid,
                new LinkRecord(
                    linkRecord.Request,
                    linkRecord.CreationFingerprint with
                    {
                        NormalizedTargetPath = @"C:\\canonical\\different-source.txt"
                    })),
            "fingerprint-link-mismatch" => WithLinkRecord(
                valid,
                new LinkRecord(
                    linkRecord.Request,
                    linkRecord.CreationFingerprint with
                    {
                        NormalizedLinkPath = @"C:\\canonical\\different-link.txt"
                    })),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown invalid database variant.")
        };
    }

    private static LinkDatabase WithRecord(LinkDatabase database, PersistedLinkRecord record) =>
        database with { Records = new[] { record } };

    private static LinkDatabase WithLinkRecord(LinkDatabase database, LinkRecord linkRecord) =>
        WithRecord(database, database.Records[0] with { LinkRecord = linkRecord });

    private static void SetLoadDiagnosticsForTest(
        JsonLinkRecordStore store,
        LinkRecordStoreLoadDiagnostics diagnostics)
    {
        var setter = typeof(JsonLinkRecordStore)
            .GetProperty(nameof(JsonLinkRecordStore.LastLoadDiagnostics))!
            .GetSetMethod(nonPublic: true)!;
        setter.Invoke(store, new object[] { diagnostics });
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = false
    };

    private static JsonSerializerOptions LegacyOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record FrozenLegacyDatabase(
        int SchemaVersion,
        IReadOnlyList<FrozenLegacyDevice> Devices,
        IReadOnlyList<FrozenLegacyPersistedLinkRecord> Records);

    private sealed record FrozenLegacyDevice(string DeviceKey, string DisplayName);

    private sealed record FrozenLegacyPersistedLinkRecord(
        Guid Id,
        string DeviceKey,
        string DeviceDisplayNameAtCreation,
        FrozenLegacyLinkRecord LinkRecord,
        DateTimeOffset CreatedUtc,
        DateTimeOffset LastCheckedUtc,
        DateTimeOffset? DeletedUtc,
        LinkStatus Status,
        string? StatusReason);

    private sealed record FrozenLegacyLinkRecord(
        FrozenLegacyLinkRequest Request,
        FrozenLegacyLinkFingerprint CreationFingerprint,
        FrozenLegacyLinkRequest? CanonicalRequest);

    private sealed record FrozenLegacyLinkRequest(string SourcePath, string LinkPath, FileLinkType Type);

    private sealed record FrozenLegacyLinkFingerprint(
        uint? ReparseTag,
        string? NormalizedTargetPath,
        ulong? VolumeSerialNumber,
        string? FileIdHex,
        string? NormalizedLinkPath);

    private const string LegacyCanonicalRequestJson = """
        {
          "schemaVersion": 1,
          "devices": [
            { "deviceKey": "device-key", "displayName": "Portable PC" }
          ],
          "records": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "deviceKey": "device-key",
              "deviceDisplayNameAtCreation": "Portable PC",
              "linkRecord": {
                "request": {
                  "sourcePath": "C:\\legacy\\source.txt",
                  "linkPath": "C:\\legacy\\link.txt",
                  "type": 2
                },
                "creationFingerprint": {
                  "reparseTag": null,
                  "normalizedTargetPath": "C:\\canonical\\source.txt",
                  "volumeSerialNumber": 42,
                  "fileIdHex": "A1B2C3D4",
                  "normalizedLinkPath": "C:\\canonical\\link.txt"
                },
                "canonicalRequest": {
                  "sourcePath": "C:\\canonical\\source.txt",
                  "linkPath": "C:\\canonical\\link.txt",
                  "type": 2
                }
              },
              "createdUtc": "2026-08-08T09:30:00+00:00",
              "lastCheckedUtc": "2026-08-08T09:35:00+00:00",
              "deletedUtc": null,
              "status": 0,
              "statusReason": "legacy-canonical"
            }
          ]
        }
        """;

    private const string LegacyWithoutCanonicalRequestJson = """
        {
          "schemaVersion": 1,
          "devices": [
            { "deviceKey": "device-key", "displayName": "Portable PC" }
          ],
          "records": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "deviceKey": "device-key",
              "deviceDisplayNameAtCreation": "Portable PC",
              "linkRecord": {
                "request": {
                  "sourcePath": "C:\\legacy\\source.txt",
                  "linkPath": "C:\\legacy\\link.txt",
                  "type": 2
                },
                "creationFingerprint": {
                  "reparseTag": null,
                  "normalizedTargetPath": "C:\\canonical\\source.txt",
                  "volumeSerialNumber": 42,
                  "fileIdHex": "A1B2C3D4",
                  "normalizedLinkPath": "C:\\canonical\\link.txt"
                }
              },
              "createdUtc": "2026-08-08T09:30:00+00:00",
              "lastCheckedUtc": "2026-08-08T09:35:00+00:00",
              "deletedUtc": null,
              "status": 0,
              "statusReason": "legacy-no-canonical"
            }
          ]
        }
        """;

    private const string LegacyRelativeWithoutCanonicalJson = """
        {
          "schemaVersion": 1,
          "devices": [
            { "deviceKey": "device-key", "displayName": "Portable PC" }
          ],
          "records": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "deviceKey": "device-key",
              "deviceDisplayNameAtCreation": "Portable PC",
              "linkRecord": {
                "request": {
                  "sourcePath": "relative-source.txt",
                  "linkPath": "relative-link.txt",
                  "type": 2
                },
                "creationFingerprint": {
                  "reparseTag": null,
                  "normalizedTargetPath": null,
                  "volumeSerialNumber": 42,
                  "fileIdHex": "A1B2C3D4",
                  "normalizedLinkPath": null
                }
              },
              "createdUtc": "2026-08-08T09:30:00+00:00",
              "lastCheckedUtc": "2026-08-08T09:35:00+00:00",
              "deletedUtc": null,
              "status": 0,
              "statusReason": "legacy-relative"
            }
          ]
        }
        """;

    private const string LegacyConflictingCanonicalRequestJson = """
        {
          "schemaVersion": 1,
          "devices": [
            { "deviceKey": "device-key", "displayName": "Portable PC" }
          ],
          "records": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "deviceKey": "device-key",
              "deviceDisplayNameAtCreation": "Portable PC",
              "linkRecord": {
                "request": {
                  "sourcePath": "C:\\canonical\\source.txt",
                  "linkPath": "C:\\canonical\\link.txt",
                  "type": 2
                },
                "creationFingerprint": {
                  "reparseTag": null,
                  "normalizedTargetPath": "C:\\canonical\\source.txt",
                  "volumeSerialNumber": 42,
                  "fileIdHex": "A1B2C3D4",
                  "normalizedLinkPath": "C:\\canonical\\link.txt"
                },
                "canonicalRequest": {
                  "sourcePath": "C:\\different\\source.txt",
                  "linkPath": "C:\\canonical\\link.txt",
                  "type": 2
                }
              },
              "createdUtc": "2026-08-08T09:30:00+00:00",
              "lastCheckedUtc": "2026-08-08T09:35:00+00:00",
              "deletedUtc": null,
              "status": 0,
              "statusReason": "legacy-conflict"
            }
          ]
        }
        """;

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FileLinkManager.StorageTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
