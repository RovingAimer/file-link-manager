namespace FileLinkManager.Domain.Storage;

public sealed record LinkDatabase(
    int SchemaVersion,
    IReadOnlyList<DeviceIdentity> Devices,
    IReadOnlyList<PersistedLinkRecord> Records)
{
    public const int CurrentSchemaVersion = 1;

    public static LinkDatabase Empty() => new(
        CurrentSchemaVersion,
        Array.Empty<DeviceIdentity>(),
        Array.Empty<PersistedLinkRecord>());
}
