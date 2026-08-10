using System.Collections;
using FileLinkManager.Application.Links;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Tests.Links;

public sealed class LinkRecordQueryServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 8, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void LinkRecordFilter_ExposesOnlyDeviceStatusAndSearchText()
    {
        var properties = typeof(LinkRecordFilter)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name);

        Assert.Equal(new[] { "DeviceKey", "SearchText", "Status" }, properties);
    }

    [Fact]
    public void Query_WithEmptyFilter_ReturnsEveryRecordInDatabaseOrderWithoutChangingDatabase()
    {
        var database = CreateDatabase();
        var originalRecords = database.Records;
        var service = new LinkRecordQueryService();

        var result = service.Query(database, new LinkRecordFilter());

        Assert.Equal(
            new[] { RecordOne, RecordTwo, RecordThree, RecordFour, RecordFive, RecordSix },
            result.Select(record => record.Id));
        Assert.Same(originalRecords, database.Records);
        var mutableResult = Assert.IsAssignableFrom<IList<PersistedLinkRecord>>(result);
        Assert.Throws<NotSupportedException>(() => mutableResult.Add(database.Records[0]));
    }

    [Fact]
    public void Query_WithDeviceStatusAndSearchText_ReturnsOnlyRecordsMatchingEveryCriterion()
    {
        var database = CreateDatabase();
        var filter = new LinkRecordFilter(
            DeviceKey: "device-a",
            Status: LinkStatus.Valid,
            SearchText: "REPORT");

        var result = new LinkRecordQueryService().Query(database, filter);

        Assert.Equal(new[] { RecordOne }, result.Select(record => record.Id));
    }

    [Fact]
    public void Query_WithSearchText_MatchesEitherSourceOrLinkIgnoringCase()
    {
        var database = CreateDatabase();

        var result = new LinkRecordQueryService().Query(
            database,
            new LinkRecordFilter(SearchText: "REPORT"));

        Assert.Equal(new[] { RecordOne, RecordTwo, RecordThree, RecordFour }, result.Select(record => record.Id));
    }

    [Fact]
    public void Query_WithBlankSearchText_DoesNotFilter()
    {
        var database = CreateDatabase();

        var result = new LinkRecordQueryService().Query(
            database,
            new LinkRecordFilter(SearchText: "  "));

        Assert.Equal(
            new[] { RecordOne, RecordTwo, RecordThree, RecordFour, RecordFive, RecordSix },
            result.Select(record => record.Id));
    }

    [Fact]
    public void Query_WithSearchTextThatMatchesNeitherPath_ReturnsNoRecords()
    {
        var database = CreateDatabase();

        var result = new LinkRecordQueryService().Query(
            database,
            new LinkRecordFilter(SearchText: "NO-SUCH-PATH"));

        Assert.Empty(result);
    }

    [Fact]
    public void Query_WithUnknownDeviceKey_ReturnsNoRecords()
    {
        var database = CreateDatabase();
        var service = new LinkRecordQueryService();

        var result = service.Query(database, new LinkRecordFilter(DeviceKey: "device-unknown"));
        var caseVariantResult = service.Query(database, new LinkRecordFilter(DeviceKey: "DEVICE-A"));

        Assert.Empty(result);
        Assert.Empty(caseVariantResult);
        Assert.Equal(new LinkStatistics(HistoricalTotal: 0, CurrentValid: 0), service.GetStatistics(database, "DEVICE-A"));
    }

    [Fact]
    public void GetStatistics_CountsHistoricalAndCurrentValidRecordsGloballyAndForOneDevice()
    {
        var database = CreateDatabase();
        var service = new LinkRecordQueryService();

        var global = service.GetStatistics(database);
        var deviceA = service.GetStatistics(database, "device-a");
        var deviceB = service.GetStatistics(database, "device-b");
        var blankScope = service.GetStatistics(database, "  ");

        Assert.Equal(new LinkStatistics(HistoricalTotal: 6, CurrentValid: 2), global);
        Assert.Equal(new LinkStatistics(HistoricalTotal: 4, CurrentValid: 1), deviceA);
        Assert.Equal(new LinkStatistics(HistoricalTotal: 2, CurrentValid: 1), deviceB);
        Assert.Equal(global, blankScope);
    }

    [Fact]
    public void GetStatistics_EnumeratesRecordsOnlyOnce()
    {
        var database = CreateDatabase();
        var records = new SingleEnumerationList<PersistedLinkRecord>(database.Records);
        var singlePassDatabase = database with { Records = records };

        var result = new LinkRecordQueryService().GetStatistics(singlePassDatabase, "device-a");

        Assert.Equal(new LinkStatistics(HistoricalTotal: 4, CurrentValid: 1), result);
        Assert.Equal(1, records.EnumerationCount);
    }

    private static LinkDatabase CreateDatabase() => new(
        LinkDatabase.CurrentSchemaVersion,
        new[]
        {
            new DeviceIdentity("device-a", "Device A"),
            new DeviceIdentity("device-b", "Device B")
        },
        new[]
        {
            CreateRecord(RecordOne, "device-a", @"C:\Reports\2026\Annual.txt", @"C:\Archive\Report-Link.txt", LinkStatus.Valid, null),
            CreateRecord(RecordTwo, "device-b", @"C:\Reports\2025\Budget.txt", @"C:\Archive\Budget-Link.txt", LinkStatus.Valid, null),
            CreateRecord(RecordThree, "device-a", @"C:\Other\Input.txt", @"C:\Archive\Report-Old.txt", LinkStatus.Deleted, Timestamp),
            CreateRecord(RecordFour, "device-a", @"C:\Reports\2026\Draft.txt", @"C:\Work\Draft-Link.txt", LinkStatus.Conflict, null),
            CreateRecord(RecordFive, "device-a", @"C:\Other\Deleted.txt", @"C:\Trash\Deleted-Link.txt", LinkStatus.Valid, Timestamp),
            CreateRecord(RecordSix, "device-b", @"C:\Missing\Source.txt", @"C:\Missing\Link.txt", LinkStatus.Missing, null)
        });

    private static PersistedLinkRecord CreateRecord(
        Guid id,
        string deviceKey,
        string sourcePath,
        string linkPath,
        LinkStatus status,
        DateTimeOffset? deletedUtc)
    {
        var request = new LinkRequest(sourcePath, linkPath, FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(null, sourcePath, 1, "0123456789ABCDEF0123456789ABCDEF", linkPath);
        var linkRecord = new LinkRecord(request, fingerprint);

        return new PersistedLinkRecord(
            id,
            deviceKey,
            deviceKey,
            linkRecord,
            Timestamp,
            Timestamp,
            deletedUtc,
            status,
            null);
    }

    private static readonly Guid RecordOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecordTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecordThree = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RecordFour = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RecordFive = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecordSix = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed class SingleEnumerationList<T>(IReadOnlyList<T> items) : IReadOnlyList<T>
    {
        public int EnumerationCount { get; private set; }

        public int Count => items.Count;

        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The records collection was enumerated more than once.");
            }

            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
