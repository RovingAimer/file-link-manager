namespace FileLinkManager.Domain.Links;

public sealed record LinkValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static LinkValidationResult Success { get; } = new(true, Array.Empty<string>());
}
