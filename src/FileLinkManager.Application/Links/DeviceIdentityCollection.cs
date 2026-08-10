using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Application.Links;

internal static class DeviceIdentityCollection
{
    internal static IReadOnlyList<DeviceIdentity> Merge(
        IReadOnlyList<DeviceIdentity> existing,
        DeviceIdentity current)
    {
        var result = existing.ToArray();
        for (var index = 0; index < result.Length; index++)
        {
            if (!string.Equals(result[index].DeviceKey, current.DeviceKey, StringComparison.Ordinal))
            {
                continue;
            }

            result[index] = current;
            return result;
        }

        return result.Concat(new[] { current }).ToArray();
    }
}
