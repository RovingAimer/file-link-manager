using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Infrastructure.Storage;

public sealed class JsonLinkRecordStore : ILinkRecordStore, ILinkRecordStoreDiagnostics
{
    private const string PrimaryFileName = "link-records.json";
    private const string BackupFileName = "link-records.json.bak";
    private const int MutexRetryMilliseconds = 50;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Converters = { new LinkRecordJsonConverter() }
    };

    private readonly string _dataRoot;
    private readonly string _lockKey;
    private readonly Action<string, string> _atomicInstaller;

    public JsonLinkRecordStore(string dataRoot)
        : this(dataRoot, atomicInstaller: null)
    {
    }

    internal JsonLinkRecordStore(string dataRoot, Action<string, string>? atomicInstaller)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("A data root directory is required.", nameof(dataRoot));
        }

        _dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        _lockKey = _dataRoot.ToUpperInvariant();
        _atomicInstaller = atomicInstaller ?? InstallAtomically;
    }

    public LinkRecordStoreLoadDiagnostics LastLoadDiagnostics { get; private set; } = LinkRecordStoreLoadDiagnostics.Clean;

    public async Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var primaryPath = GetPrimaryPath();

        if (!File.Exists(primaryPath))
        {
            SetWritable();
            return LinkDatabase.Empty();
        }

        try
        {
            var database = await ReadDatabaseAsync(primaryPath, cancellationToken);
            if (database.SchemaVersion > LinkDatabase.CurrentSchemaVersion)
            {
                SetReadOnly($"The data uses schema version {database.SchemaVersion}, which is newer than this application supports.");
            }
            else
            {
                SetWritable();
            }

            return database;
        }
        catch (InvalidDataException primaryException)
        {
            return await RecoverFromBackupAsync(primaryPath, primaryException, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetReadOnly("The primary file could not be accessed; storage remains read-only.");
            throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, exception);
        }
        catch (IOException exception)
        {
            SetReadOnly("The primary file could not be read; storage remains read-only.");
            throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, exception);
        }
    }

    public async Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        cancellationToken.ThrowIfCancellationRequested();

        var primaryPath = GetPrimaryPath();
        if (LastLoadDiagnostics.IsReadOnly)
        {
            throw new LinkRecordStoreException(primaryPath, "Storage is read-only until the existing data issue is resolved.");
        }

        try
        {
            ValidateDatabase(database, requireCurrentWritableSchema: true);
        }
        catch (InvalidDataException exception)
        {
            throw new LinkRecordStoreException(primaryPath, "The database to save is semantically invalid.", exception);
        }

        var saveLock = SaveLocks.GetOrAdd(_lockKey, static _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(
                () =>
                {
                    using var crossProcessMutex = new Mutex(initiallyOwned: false, GetMutexName());
                    var mutexAcquired = false;
                    try
                    {
                        mutexAcquired = WaitForMutex(crossProcessMutex, cancellationToken);
                        SaveUnderLockAsync(database, primaryPath, cancellationToken).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        if (mutexAcquired)
                        {
                            crossProcessMutex.ReleaseMutex();
                        }
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LinkRecordStoreException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            SetReadOnly("The data location could not be accessed; storage remains read-only.");
            throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, exception);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            SetReadOnly("The data location could not be written; storage remains read-only.");
            throw new LinkRecordStoreException(primaryPath, "The data file could not be saved without replacing the existing primary file.", exception);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task SaveUnderLockAsync(LinkDatabase database, string primaryPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataRoot);
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(primaryPath))
        {
            LinkDatabase existingDatabase;
            try
            {
                existingDatabase = await ReadDatabaseAsync(primaryPath, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                SetReadOnly("The existing primary file is invalid and was left unchanged.");
                throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, exception);
            }
            if (existingDatabase.SchemaVersion > LinkDatabase.CurrentSchemaVersion)
            {
                SetReadOnly($"The data uses schema version {existingDatabase.SchemaVersion}, which is newer than this application supports.");
                throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!);
            }
        }

        string? primaryTemporaryPath = null;
        string? backupTemporaryPath = null;
        try
        {
            primaryTemporaryPath = GetTemporaryPath(PrimaryFileName);
            await WriteDatabaseAsync(primaryTemporaryPath, database, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(primaryPath))
            {
                var backupPath = GetBackupPath();
                backupTemporaryPath = GetTemporaryPath(BackupFileName);
                File.Copy(primaryPath, backupTemporaryPath);
                FlushFileToDisk(backupTemporaryPath);
                _atomicInstaller(backupTemporaryPath, backupPath);
                backupTemporaryPath = null;
            }

            _atomicInstaller(primaryTemporaryPath, primaryPath);
            primaryTemporaryPath = null;
        }
        finally
        {
            if (primaryTemporaryPath is not null)
            {
                TryDeleteTemporaryFile(primaryTemporaryPath);
            }

            if (backupTemporaryPath is not null)
            {
                TryDeleteTemporaryFile(backupTemporaryPath);
            }
        }
    }

    private async Task<LinkDatabase> RecoverFromBackupAsync(
        string primaryPath,
        InvalidDataException primaryException,
        CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath();
        if (!File.Exists(backupPath))
        {
            SetReadOnly("The primary file is invalid and no backup is available. The primary file was left unchanged.");
            throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, primaryException);
        }

        try
        {
            var backup = await ReadDatabaseAsync(backupPath, cancellationToken);
            SetReadOnly(
                backup.SchemaVersion > LinkDatabase.CurrentSchemaVersion
                    ? "The primary file is invalid and a newer-schema backup was loaded; storage remains read-only."
                    : "The primary file is invalid. A valid backup was loaded; storage remains read-only to preserve the primary file.",
                recoveredFromBackup: true);
            return backup;
        }
        catch (Exception backupException) when (backupException is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetReadOnly("The primary file is invalid and the backup could not be used. Both files were left unchanged.");
            throw new LinkRecordStoreException(primaryPath, LastLoadDiagnostics.Message!, new AggregateException(primaryException, backupException));
        }
    }

    private static async Task<LinkDatabase> ReadDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                FileOptions.Asynchronous);
            var database = await JsonSerializer.DeserializeAsync<LinkDatabase>(stream, SerializerOptions, cancellationToken);
            if (database is null)
            {
                throw new InvalidDataException("The JSON data does not contain a database.");
            }

            ValidateDatabase(database, requireCurrentWritableSchema: false);
            return database;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The JSON data is malformed.", exception);
        }
    }

    private static void ValidateDatabase(LinkDatabase database, bool requireCurrentWritableSchema)
    {
        if (database.SchemaVersion < 1 || (requireCurrentWritableSchema && database.SchemaVersion != LinkDatabase.CurrentSchemaVersion))
        {
            throw new InvalidDataException("The JSON data does not contain a supported schema version.");
        }

        if (database.Devices is null || database.Records is null)
        {
            throw new InvalidDataException("The JSON data does not contain the required collections.");
        }

        var devices = new Dictionary<string, DeviceIdentity>(StringComparer.Ordinal);
        foreach (var device in database.Devices)
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceKey) || string.IsNullOrWhiteSpace(device.DisplayName))
            {
                throw new InvalidDataException("The JSON data contains invalid device metadata.");
            }

            if (!devices.TryAdd(device.DeviceKey, device))
            {
                throw new InvalidDataException("The JSON data contains duplicate device keys.");
            }
        }

        var recordIds = new HashSet<Guid>();
        foreach (var record in database.Records)
        {
            if (record is null || record.Id == Guid.Empty || !recordIds.Add(record.Id) ||
                string.IsNullOrWhiteSpace(record.DeviceKey) || string.IsNullOrWhiteSpace(record.DeviceDisplayNameAtCreation) ||
                !devices.ContainsKey(record.DeviceKey))
            {
                throw new InvalidDataException("The JSON data contains invalid record identity or device metadata.");
            }

            if (!IsUtc(record.CreatedUtc) || !IsUtc(record.LastCheckedUtc) ||
                (record.DeletedUtc.HasValue && !IsUtc(record.DeletedUtc.Value)) ||
                record.CreatedUtc == default || record.LastCheckedUtc == default || record.CreatedUtc > record.LastCheckedUtc ||
                (record.DeletedUtc.HasValue && record.DeletedUtc.Value < record.CreatedUtc) ||
                (record.Status == LinkStatus.Deleted) != record.DeletedUtc.HasValue ||
                !Enum.IsDefined(record.Status))
            {
                throw new InvalidDataException("The JSON data contains invalid record lifecycle metadata.");
            }

            ValidateLinkRecord(record.LinkRecord);
        }
    }

    private static void ValidateLinkRecord(LinkRecord? linkRecord)
    {
        if (linkRecord?.Request is null || linkRecord.CreationFingerprint is null ||
            !Enum.IsDefined(linkRecord.Request.Type) ||
            !IsAbsolutePath(linkRecord.Request.SourcePath) || !IsAbsolutePath(linkRecord.Request.LinkPath))
        {
            throw new InvalidDataException("The JSON data contains an invalid link request.");
        }

        var fingerprint = linkRecord.CreationFingerprint;
        if (!IsAbsolutePath(fingerprint.NormalizedTargetPath) || !IsAbsolutePath(fingerprint.NormalizedLinkPath) ||
            !PathsMatch(linkRecord.Request.SourcePath, fingerprint.NormalizedTargetPath) ||
            !PathsMatch(linkRecord.Request.LinkPath, fingerprint.NormalizedLinkPath))
        {
            throw new InvalidDataException("The JSON data contains a fingerprint that is inconsistent with the effective link request.");
        }

    }

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool IsAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    private static bool PathsMatch(string left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private async Task WriteDatabaseAsync(string temporaryPath, LinkDatabase database, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, database, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void FlushFileToDisk(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    private static void InstallAtomically(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }

    private static bool WaitForMutex(Mutex mutex, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (mutex.WaitOne(MutexRetryMilliseconds))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }

    private string GetPrimaryPath() => Path.Combine(_dataRoot, PrimaryFileName);

    private string GetBackupPath() => Path.Combine(_dataRoot, BackupFileName);

    private string GetTemporaryPath(string fileName) => Path.Combine(_dataRoot, $".{fileName}.{Guid.NewGuid():N}.tmp");

    private string GetMutexName() => GetMutexNameForRoot(_dataRoot);

    internal static string GetMutexNameForTesting(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("A data root directory is required.", nameof(dataRoot));
        }

        return GetMutexNameForRoot(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)));
    }

    private static string GetMutexNameForRoot(string dataRoot)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dataRoot.ToUpperInvariant()));
        return $"Local\\FileLinkManager.JsonLinkRecordStore.{Convert.ToHexString(hash)}";
    }

    private void SetReadOnly(string message, bool recoveredFromBackup = false)
    {
        LastLoadDiagnostics = new LinkRecordStoreLoadDiagnostics(recoveredFromBackup, true, message);
    }

    private void SetWritable() =>
        LastLoadDiagnostics = LinkRecordStoreLoadDiagnostics.Clean;

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class LinkRecordJsonConverter : JsonConverter<LinkRecord>
    {
        public override LinkRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var payload = JsonSerializer.Deserialize<LinkRecordPayload>(ref reader, options)
                ?? throw new JsonException("A link record payload is required.");

            if (payload.Request is null || payload.CreationFingerprint is null)
            {
                throw new JsonException("A link record requires a request and creation fingerprint.");
            }

            if (payload.CanonicalRequest is { } canonicalRequest &&
                (!Enum.IsDefined(payload.Request.Type) ||
                 !Enum.IsDefined(canonicalRequest.Type) ||
                 canonicalRequest.Type != payload.Request.Type ||
                 !IsAbsolutePath(canonicalRequest.SourcePath) ||
                 !IsAbsolutePath(canonicalRequest.LinkPath) ||
                 !PathsMatch(canonicalRequest.SourcePath, payload.CreationFingerprint.NormalizedTargetPath) ||
                 !PathsMatch(canonicalRequest.LinkPath, payload.CreationFingerprint.NormalizedLinkPath)))
            {
                throw new JsonException("A legacy canonical link request is inconsistent with its request or fingerprint.");
            }

            var effectiveRequest = payload.CanonicalRequest ?? payload.Request;
            if (payload.CanonicalRequest is null &&
                payload.CreationFingerprint is
                {
                    NormalizedTargetPath: { Length: > 0 } source,
                    NormalizedLinkPath: { Length: > 0 } link
                })
            {
                effectiveRequest = payload.Request with
                {
                    SourcePath = source,
                    LinkPath = link
                };
            }

            return new LinkRecord(effectiveRequest, payload.CreationFingerprint);
        }

        public override void Write(Utf8JsonWriter writer, LinkRecord value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(
                writer,
                new LinkRecordWritePayload(value.Request, value.CreationFingerprint),
                options);
        }
    }

    private sealed record LinkRecordPayload(
        LinkRequest? Request,
        LinkFingerprint? CreationFingerprint,
        LinkRequest? CanonicalRequest);

    private sealed record LinkRecordWritePayload(
        LinkRequest Request,
        LinkFingerprint CreationFingerprint);
}
