using FileLinkManager.Domain.Links;

namespace FileLinkManager.Application.Abstractions;

public interface IFileLinkService
{
    LinkValidationResult Validate(LinkRequest request);

    Task<LinkInspection> CreateAsync(LinkRequest request, CancellationToken cancellationToken);

    Task<LinkInspection> InspectAsync(string linkPath, CancellationToken cancellationToken);

    Task DeleteAsync(LinkRecord record, CancellationToken cancellationToken);
}
