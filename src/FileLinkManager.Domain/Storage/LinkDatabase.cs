namespace FileLinkManager.Domain.Storage;

public sealed record LinkDatabase(
    int SchemaVersion,
    IReadOnlyList<DeviceIdentity> Devices,
    IReadOnlyList<PersistedLinkRecord> Records)
{
    public const int CurrentSchemaVersion = 2;

    public IReadOnlyList<LinkRecordGroup> Groups { get; init; } = Array.Empty<LinkRecordGroup>();

    public static LinkDatabase Empty() => new(
        CurrentSchemaVersion,
        Array.Empty<DeviceIdentity>(),
        Array.Empty<PersistedLinkRecord>());
}
