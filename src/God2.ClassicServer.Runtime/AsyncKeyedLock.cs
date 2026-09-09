namespace God2.ClassicServer.Runtime;

internal sealed class AsyncKeyedLock<TKey>
    where TKey : notnull
{
    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class Lease : IDisposable
    {
        private AsyncKeyedLock<TKey>? _owner;
        private readonly TKey _key;
        private readonly Entry _entry;

        public Lease(AsyncKeyedLock<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_key, _entry, acquired: true);
        }
    }

    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries = [];

    public async ValueTask<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        return await AcquireCoreAsync(key, wait: true, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A blocking keyed-lock acquisition unexpectedly failed.");
    }

    public ValueTask<IDisposable?> TryAcquireAsync(TKey key, CancellationToken cancellationToken) =>
        AcquireCoreAsync(key, wait: false, cancellationToken);

    private async ValueTask<IDisposable?> AcquireCoreAsync(
        TKey key,
        bool wait,
        CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.ReferenceCount = checked(entry.ReferenceCount + 1);
        }

        try
        {
            var acquired = wait
                ? await WaitAsync(entry.Semaphore, cancellationToken).ConfigureAwait(false)
                : await entry.Semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!acquired)
            {
                Release(key, entry, acquired: false);
                return null;
            }

            return new Lease(this, key, entry);
        }
        catch
        {
            Release(key, entry, acquired: false);
            throw;
        }
    }

    private static async ValueTask<bool> WaitAsync(
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void Release(TKey key, Entry entry, bool acquired)
    {
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (_sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                _entries.TryGetValue(key, out var current) &&
                ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }
}
