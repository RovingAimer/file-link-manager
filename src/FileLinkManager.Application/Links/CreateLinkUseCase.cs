using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

public sealed class CreateLinkUseCase
{
    private readonly IFileLinkService fileLinkService;
    private readonly ILinkRecordStore recordStore;
    private readonly IDeviceIdentityProvider deviceIdentityProvider;
    private readonly TimeProvider timeProvider;

    public CreateLinkUseCase(
        IFileLinkService fileLinkService,
        ILinkRecordStore recordStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider? timeProvider = null)
    {
        this.fileLinkService = fileLinkService ?? throw new ArgumentNullException(nameof(fileLinkService));
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<CreateLinkOutcome> ExecuteAsync(LinkRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request, new LinkRecordMetadata(), cancellationToken);

    public async Task<CreateLinkOutcome> ExecuteAsync(LinkRequest request, LinkRecordMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            metadata = metadata.Normalize();
            if (metadata.GroupId.HasValue)
            {
                var database = await recordStore.LoadAsync(cancellationToken);
                if (database.SchemaVersion > LinkDatabase.CurrentSchemaVersion ||
                    recordStore is ILinkRecordStoreDiagnostics { LastLoadDiagnostics.IsReadOnly: true })
                {
                    return new CreateLinkValidationFailed(new LinkValidationResult(false, new[] { "记录存储处于只读模式。" }));
                }
                metadata = LinkRecordOrganizationService.ValidateMetadata(database, metadata);
            }
        }
        catch (ArgumentException exception)
        {
            return new CreateLinkValidationFailed(new LinkValidationResult(false, new[] { exception.Message }));
        }

        var validation = fileLinkService.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!validation.IsValid)
        {
            return new CreateLinkValidationFailed(validation);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var inspection = await fileLinkService.CreateAsync(request, cancellationToken);
        if (!LinkRegistration.IsUsable(request, inspection))
        {
            return new CreateLinkInspectionFailed(inspection);
        }

        try
        {
            var record = await LinkRegistration.RegisterAsync(
                request,
                inspection,
                recordStore,
                deviceIdentityProvider,
                timeProvider,
                metadata,
                CancellationToken.None);
            return new CreatedAndRegistered(record);
        }
        catch (Exception exception) when (exception is LinkRecordStoreException or OperationCanceledException)
        {
            return new CreatedButUnregistered(request, inspection, exception.Message) { Metadata = metadata };
        }
    }
}
