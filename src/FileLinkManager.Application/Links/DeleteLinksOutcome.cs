using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public abstract record DeleteLinksOutcome;

public sealed record DeleteLinkRejected(string Reason) : DeleteLinksOutcome;

public sealed record MissingConfirmationRequired(PersistedLinkRecord Record) : DeleteLinksOutcome;

public sealed record DeletedAndRegistered(
    PersistedLinkRecord Record,
    string? SourceCheckReason) : DeleteLinksOutcome;

public sealed record DeletedButUnregistered(
    PersistedLinkRecord Record,
    string StorageError,
    string? SourceCheckReason) : DeleteLinksOutcome;
