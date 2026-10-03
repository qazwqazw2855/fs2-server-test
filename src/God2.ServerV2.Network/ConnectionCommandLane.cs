namespace God2.ServerV2.Network;

// One instance per connection. All coordinated operations must use this lane.
// Callbacks must not recursively enter or close the same lane.
public sealed class ConnectionCommandLane
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _closeGate = new();
    private int _closing;
    private Task? _closeTask;

    public bool IsClosing => Volatile.Read(ref _closing) != 0;

    public async ValueTask<T> RunAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var lease = await EnterAsync(cancellationToken);
        return await operation(cancellationToken);
    }

    // Supports existing frame loops with continue/break/return.
    // Always dispose the lease; do not recursively enter the same lane.
    public async ValueTask<IDisposable> EnterAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfClosing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfClosing();
            return new Lease(_gate);
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _ownedGate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(ref _ownedGate, null)?.Release();
        }
    }

    // Cleanup is attempted exactly once, after the active operation finishes.
    // All callers observe the same completion or failure.
    public Task CloseAsync(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_closeGate)
        {
            if (_closeTask is not null)
                return _closeTask;

            Interlocked.Exchange(ref _closing, 1);
            _closeTask = CompleteCloseAsync(cleanup);
            return _closeTask;
        }
    }

    private async Task CompleteCloseAsync(Func<ValueTask> cleanup)
    {
        await _gate.WaitAsync();
        try
        {
            await cleanup();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfClosing()
    {
        if (IsClosing)
            throw new InvalidOperationException("Connection command lane is closing.");
    }
}
