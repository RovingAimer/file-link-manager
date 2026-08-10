namespace FileLinkManager.Domain.Links;

public sealed record LinkRequest(string SourcePath, string LinkPath, FileLinkType Type);
