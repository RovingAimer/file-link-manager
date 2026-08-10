using FileLinkManager.Infrastructure.Runtime;

namespace FileLinkManager.Infrastructure.Tests.Runtime;

public sealed class WindowsApplicationInstanceGuardTests
{
    [Fact]
    public async Task NamedGuard_AllowsOnlyOneOwnerUntilReleased()
    {
        var mutexName = $"Local\\FileLinkManager.Tests.{Guid.NewGuid():N}";
        using var first = new WindowsApplicationInstanceGuard(mutexName);
        using var second = new WindowsApplicationInstanceGuard(mutexName);

        var firstAcquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstThread = Task.Run(() =>
        {
            firstAcquired.SetResult(first.TryAcquire());
            releaseFirst.Task.GetAwaiter().GetResult();
            first.Dispose();
        });

        Assert.True(await firstAcquired.Task);
        Assert.False(await Task.Run(() => second.TryAcquire()));
        releaseFirst.SetResult();
        await firstThread;

        var acquiredAfterRelease = await Task.Run(() =>
        {
            var acquired = second.TryAcquire();
            second.Dispose();
            return acquired;
        });
        Assert.True(acquiredAfterRelease);
    }
}
