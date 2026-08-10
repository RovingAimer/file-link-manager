using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Abstractions;

public interface ILinkRecordStore
{
    Task<LinkDatabase> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(LinkDatabase database, CancellationToken cancellationToken);
}
