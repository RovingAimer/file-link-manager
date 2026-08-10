using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;

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

    public async Task<CreateLinkOutcome> ExecuteAsync(LinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

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
                CancellationToken.None);
            return new CreatedAndRegistered(record);
        }
        catch (Exception exception) when (exception is LinkRecordStoreException or OperationCanceledException)
        {
            return new CreatedButUnregistered(request, inspection, exception.Message);
        }
    }
}
