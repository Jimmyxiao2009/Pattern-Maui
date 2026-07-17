namespace Pattern.Maui.Services;

/// <summary>Prevents two desktop Agent runtimes from competing for the same data and relay outbox.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private Mutex? _mutex;
    public bool IsPrimary { get; private set; }

    public void Acquire()
    {
        if (OperatingSystem.IsAndroid()) { IsPrimary = true; return; }
        try
        {
            _mutex = new Mutex(true, "Local\\Pattern.Maui.SingleInstance", out var created);
            IsPrimary = created;
            if (!created) _mutex.Dispose();
        }
        catch { IsPrimary = true; }
    }

    public void Dispose()
    {
        if (!IsPrimary || _mutex is null) return;
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
        _mutex = null;
    }
}
