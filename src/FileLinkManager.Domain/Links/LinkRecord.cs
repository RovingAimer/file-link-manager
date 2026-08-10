namespace FileLinkManager.Domain.Links;

public sealed record LinkRecord
{
    public LinkRecord(LinkRequest request, LinkFingerprint creationFingerprint)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        CreationFingerprint = creationFingerprint ?? throw new ArgumentNullException(nameof(creationFingerprint));
    }

    public LinkRequest Request { get; }

    public LinkFingerprint CreationFingerprint { get; }
}
