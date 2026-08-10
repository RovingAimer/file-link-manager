using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Links;

namespace FileLinkManager.Application.Links;

public sealed class RetryRegistrationUseCase
{
    private readonly ILinkRecordStore recordStore;
    private readonly IDeviceIdentityProvider deviceIdentityProvider;
    private readonly TimeProvider timeProvider;

    public RetryRegistrationUseCase(
        ILinkRecordStore recordStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        TimeProvider? timeProvider = null)
    {
        this.recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        this.deviceIdentityProvider = deviceIdentityProvider ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RetryRegistrationOutcome> ExecuteAsync(
        LinkRequest request,
        LinkInspection inspection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(inspection);
        cancellationToken.ThrowIfCancellationRequested();

        var usabilityError = LinkRegistration.GetUsabilityError(request, inspection);
        if (usabilityError is not null)
        {
            return new RetryRegistrationRejected(inspection, usabilityError);
        }

        var record = await LinkRegistration.RegisterAsync(
            request,
            inspection,
            recordStore,
            deviceIdentityProvider,
            timeProvider,
            cancellationToken);
        return new RetryRegistrationSucceeded(record);
    }
}
