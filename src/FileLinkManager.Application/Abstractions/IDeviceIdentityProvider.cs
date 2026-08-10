using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Abstractions;

public interface IDeviceIdentityProvider
{
    DeviceIdentity GetCurrent();
}
