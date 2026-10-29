using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

/// <summary>Describes the observable result of creating and registering a link.</summary>
public abstract record CreateLinkOutcome;

/// <summary>Indicates that validation rejected the request before a native link was created.</summary>
public sealed record CreateLinkValidationFailed(LinkValidationResult Validation) : CreateLinkOutcome;

/// <summary>Indicates that native creation returned an inspection that cannot be registered safely.</summary>
public sealed record CreateLinkInspectionFailed(LinkInspection Inspection) : CreateLinkOutcome;

/// <summary>Indicates that the native link was created and its record was registered.</summary>
public sealed record CreatedAndRegistered(PersistedLinkRecord Record) : CreateLinkOutcome;

/// <summary>Indicates that the native link exists but its registration can be retried with the supplied data.</summary>
public sealed record CreatedButUnregistered(
    LinkRequest Request,
    LinkInspection Inspection,
    string RegistrationError) : CreateLinkOutcome
{
    public LinkRecordMetadata Metadata { get; init; } = new();
}
