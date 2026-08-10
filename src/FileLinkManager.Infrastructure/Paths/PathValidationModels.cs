namespace FileLinkManager.Infrastructure.Paths;

internal sealed record NormalizedPath(string DisplayPath, string ComparisonPath);

internal enum PathValidationErrorCode
{
    RequestRequired,
    InvalidSourcePath,
    InvalidLinkPath,
    SourceDoesNotExist,
    LinkPathAlreadyExists,
    LinkParentDoesNotExist,
    SourceTypeMismatch,
    DifferentVolume,
    NetworkPathNotSupported,
    VolumeIdentityUnavailable
}

internal sealed record PathValidationError(PathValidationErrorCode Code, string Message);

internal sealed record PathNormalizationResult(NormalizedPath? Path, PathValidationError? Error)
{
    public bool IsValid => Path is not null && Error is null;
}

internal sealed record PathValidationResult(
    NormalizedPath? SourcePath,
    NormalizedPath? LinkPath,
    IReadOnlyList<PathValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
