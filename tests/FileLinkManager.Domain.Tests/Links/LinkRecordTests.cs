using FileLinkManager.Domain.Links;

namespace FileLinkManager.Domain.Tests.Links;

public sealed class LinkRecordTests
{
    [Fact]
    public void Constructor_PreservesCanonicalRequestAndCreationFingerprint()
    {
        var request = new LinkRequest(@"C:\source.txt", @"C:\links\link.txt", FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(
            null,
            request.SourcePath,
            1,
            "0123456789ABCDEF0123456789ABCDEF",
            request.LinkPath);

        var record = new LinkRecord(request, fingerprint);

        Assert.Equal(request, record.Request);
        Assert.Equal(fingerprint, record.CreationFingerprint);
    }

    [Fact]
    public void ConstructorPreservesRelativeRequestForStorageBoundaryValidation()
    {
        var request = new LinkRequest("source.txt", "link.txt", FileLinkType.FileHardLink);
        var fingerprint = new LinkFingerprint(null, null, 1, "0123456789ABCDEF0123456789ABCDEF");

        var record = new LinkRecord(request, fingerprint);

        Assert.Equal(request, record.Request);
        Assert.Equal(fingerprint, record.CreationFingerprint);
    }
}
