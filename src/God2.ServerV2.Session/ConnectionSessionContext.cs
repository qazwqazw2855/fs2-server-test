namespace God2.ServerV2.Session;

public sealed class ConnectionSessionContext : IDisposable
{
    private readonly object _gate = new();
    private readonly SessionRegistry _registry;
    private string? _accountName;
    private bool _disposed;

    public ConnectionSessionContext(
        long connectionId,
        SessionRegistry registry)
    {
        if (connectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        }

        ConnectionId = connectionId;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public long ConnectionId { get; }

    public string? AccountName
    {
        get
        {
            lock (_gate)
            {
                return _accountName;
            }
        }
    }

    public bool HasSession => AccountName is not null;

    public SessionAcquireResult BindAccount(string accountName)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_accountName is not null &&
                !string.Equals(
                    _accountName,
                    accountName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A connection cannot change accounts after acquiring a session.");
            }

            var result = _registry.TryAcquire(
                accountName,
                ConnectionId);

            if (result.Succeeded)
            {
                _accountName = accountName.Trim();
            }

            return result;
        }
    }

    public bool TransferOrAcquireFrom(
        string accountName,
        long expectedOwnerConnectionId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var normalizedAccount = accountName.Trim();
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedAccount);

            if (_accountName is not null)
            {
                throw new InvalidOperationException(
                    "A connection already owns a session.");
            }

            if (_registry.TryTransfer(
                    normalizedAccount,
                    expectedOwnerConnectionId,
                    ConnectionId))
            {
                _accountName = normalizedAccount;
                return true;
            }

            var acquire = _registry.TryAcquire(
                normalizedAccount,
                ConnectionId);

            if (!acquire.Succeeded)
            {
                return false;
            }

            _accountName = normalizedAccount;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_accountName is not null)
            {
                _registry.Release(
                    _accountName,
                    ConnectionId);
                _accountName = null;
            }
        }
    }
}
