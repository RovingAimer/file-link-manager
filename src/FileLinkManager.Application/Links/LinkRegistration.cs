using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

internal static class LinkRegistration
{
    internal const string UnusableInspectionReason =
        "A valid inspection with a fingerprint and canonical source and link paths is required for registration.";

    internal static bool IsUsable(LinkRequest request, LinkInspection inspection) =>
        GetUsabilityError(request, inspection) is null;

    internal static string? GetUsabilityError(LinkRequest request, LinkInspection inspection)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) ||
            string.IsNullOrWhiteSpace(request.LinkPath) ||
            !Enum.IsDefined(request.Type))
        {
            return "A non-empty source path, link path, and defined link type are required for registration.";
        }

        if (inspection.Status != LinkStatus.Valid ||
            inspection.Fingerprint is not
            {
                NormalizedTargetPath: { Length: > 0 },
                NormalizedLinkPath: { Length: > 0 }
            })
        {
            return UnusableInspectionReason;
        }

        if (inspection.Type is null || inspection.Type.Value != request.Type)
        {
            return "The inspection type must match the requested link type before registration.";
        }

        if (!Path.IsPathFullyQualified(inspection.Fingerprint.NormalizedTargetPath) ||
            !Path.IsPathFullyQualified(inspection.Fingerprint.NormalizedLinkPath) ||
            string.IsNullOrWhiteSpace(inspection.LinkPath) ||
            !Path.IsPathFullyQualified(inspection.LinkPath) ||
            !string.Equals(
                inspection.LinkPath,
                inspection.Fingerprint.NormalizedLinkPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return "The inspection must contain absolute canonical source and link paths that agree with its fingerprint.";
        }

        return null;
    }

    internal static Task<PersistedLinkRecord> RegisterAsync(
        LinkRequest request,
        LinkInspection inspection,
        ILinkRecordStore store,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        RegisterAsync(request, inspection, store, deviceIdentityProvider, timeProvider, new LinkRecordMetadata(), cancellationToken);

    internal static async Task<PersistedLinkRecord> RegisterAsync(
        LinkRequest request,
        LinkInspection inspection,
        ILinkRecordStore store,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider timeProvider,
        LinkRecordMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = deviceIdentityProvider.GetCurrent();
        cancellationToken.ThrowIfCancellationRequested();

        var timestamp = timeProvider.GetUtcNow().ToUniversalTime();
        var canonicalRequest = request with
        {
            SourcePath = inspection.Fingerprint!.NormalizedTargetPath!,
            LinkPath = inspection.Fingerprint.NormalizedLinkPath!
        };
        var record = new PersistedLinkRecord(
            Guid.NewGuid(),
            device.DeviceKey,
            device.DisplayName,
            new LinkRecord(canonicalRequest, inspection.Fingerprint),
            timestamp,
            timestamp,
            null,
            inspection.Status,
            inspection.Error);

        cancellationToken.ThrowIfCancellationRequested();
        var database = await store.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            metadata = LinkRecordOrganizationService.ValidateMetadata(database, metadata);
        }
        catch (ArgumentException exception)
        {
            throw new LinkRecordStoreException("link-records.json", exception.Message, exception);
        }
        // Stable ordering also preserves the original order of schema-1 records with default zero.
        var siblings = database.Records.Where(item => item.GroupId == metadata.GroupId).OrderBy(item => item.SortOrder).ToArray();
        record = record with { CustomName = metadata.CustomName, GroupId = metadata.GroupId, SortOrder = siblings.Length };
        var orders = siblings.Select((item, index) => (item.Id, index)).ToDictionary(item => item.Id, item => item.index);

        var devices = DeviceIdentityCollection.Merge(database.Devices, device);
        var updatedDatabase = database with
        {
            Devices = devices,
            Records = database.Records.Select(item => orders.TryGetValue(item.Id, out var order)
                ? item with { SortOrder = order } : item).Append(record).ToArray()
        };

        cancellationToken.ThrowIfCancellationRequested();
        await store.SaveAsync(updatedDatabase, cancellationToken);
        return record;
    }

}
