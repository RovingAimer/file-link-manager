using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

/// <summary>Describes the observable result of retrying registration for an already-created link.</summary>
public abstract record RetryRegistrationOutcome;

/// <summary>Indicates that the supplied creation data was registered successfully.</summary>
public sealed record RetryRegistrationSucceeded(PersistedLinkRecord Record) : RetryRegistrationOutcome;

/// <summary>Indicates that the supplied inspection cannot be registered safely.</summary>
public sealed record RetryRegistrationRejected(LinkInspection Inspection, string Reason) : RetryRegistrationOutcome;
