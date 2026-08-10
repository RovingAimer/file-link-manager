using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public abstract record RemoveDeletedLinkRecordOutcome;

public sealed record DeletedLinkRecordRemoved(
    LinkDatabase Database,
    PersistedLinkRecord RemovedRecord) : RemoveDeletedLinkRecordOutcome;

public sealed record RemoveDeletedLinkRecordRejected(
    LinkDatabase Database,
    string Reason) : RemoveDeletedLinkRecordOutcome;
