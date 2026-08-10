namespace FileLinkManager.Infrastructure.Runtime;

public sealed class WindowsApplicationInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\FileLinkManager.App.SingleInstance.v1";

    private readonly Mutex mutex;
    private bool ownsMutex;
    private bool isDisposed;

    public WindowsApplicationInstanceGuard(string mutexName = DefaultMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException("A mutex name is required.", nameof(mutexName));
        }

        mutex = new Mutex(initiallyOwned: false, mutexName);
    }

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (ownsMutex)
        {
            return true;
        }

        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }

        mutex.Dispose();
    }
}
