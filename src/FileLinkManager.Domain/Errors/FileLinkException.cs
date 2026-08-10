namespace FileLinkManager.Domain.Errors;

public sealed class FileLinkException : Exception
{
    public FileLinkException(string operation, string path, string reason)
        : base($"{operation} failed for '{path}': {reason}")
    {
        Operation = operation;
        Path = path;
        Reason = reason;
    }

    public string Operation { get; }

    public string Path { get; }

    public string Reason { get; }
}
