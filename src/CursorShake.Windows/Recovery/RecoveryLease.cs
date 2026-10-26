namespace CursorShake.Windows.Recovery;

internal sealed class RecoveryLease : IDisposable
{
    private readonly Mutex _mutex;
    public RecoveryLease(RecoveryManifest manifest)
    {
        _mutex = new Mutex(false, manifest.LedgerName + ".RecoveryLock");
        bool acquired;
        try { acquired = _mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { _mutex.Dispose(); throw new IOException("Another recovery operation is still running."); }
    }
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
