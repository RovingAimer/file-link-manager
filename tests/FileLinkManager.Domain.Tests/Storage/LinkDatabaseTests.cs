using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Domain.Tests.Storage;

public sealed class LinkDatabaseTests
{
    [Fact]
    public void Empty_CreatesCurrentSchemaDatabaseWithoutDevicesOrRecords()
    {
        var database = LinkDatabase.Empty();

        Assert.Equal(LinkDatabase.CurrentSchemaVersion, database.SchemaVersion);
        Assert.Empty(database.Devices);
        Assert.Empty(database.Records);
    }

    [Fact]
    public void PersistedRecord_PreservesDeviceAndLifecycleMetadata()
    {
        var request = new LinkRequest(@"C:\\source.txt", @"C:\\links\\source.txt", FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(null, null, 42, "A1B2C3D4", @"C:\\links\\source.txt");
        var createdUtc = new DateTimeOffset(2026, 8, 8, 9, 30, 0, TimeSpan.Zero);
        var checkedUtc = createdUtc.AddMinutes(5);
        var deletedUtc = checkedUtc.AddMinutes(2);
        var record = new PersistedLinkRecord(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "device-key",
            "Portable PC",
            new LinkRecord(request, fingerprint),
            createdUtc,
            checkedUtc,
            deletedUtc,
            LinkStatus.Deleted,
            "Removed by the user.");

        Assert.Equal("device-key", record.DeviceKey);
        Assert.Equal("Portable PC", record.DeviceDisplayNameAtCreation);
        Assert.Equal(deletedUtc, record.DeletedUtc);
        Assert.Equal(LinkStatus.Deleted, record.Status);
        Assert.Equal("Removed by the user.", record.StatusReason);
        Assert.Equal(@"C:\\links\\source.txt", record.LinkRecord.Request.LinkPath);
    }
}
