namespace FileLinkManager.Application.Abstractions;

public sealed class LinkRecordStoreException : IOException
{
    public LinkRecordStoreException(string storagePath, string reason, Exception? innerException = null)
        : base($"Link record storage failed for '{storagePath}': {reason}", innerException)
    {
        StoragePath = storagePath;
        Reason = reason;
    }

    public string StoragePath { get; }

    public string Reason { get; }
}
