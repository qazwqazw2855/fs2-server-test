using System.Collections.Concurrent;

namespace God2.ServerV2.Session;

public enum SessionAcquireStatus
{
    Acquired,
    AlreadyOwnedByConnection,
    DuplicateAccount
}

public readonly record struct SessionAcquireResult(
    SessionAcquireStatus Status,
    long OwnerConnectionId)
{
    public bool Succeeded =>
        Status is SessionAcquireStatus.Acquired
            or SessionAcquireStatus.AlreadyOwnedByConnection;
}

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, long> _owners =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _owners.Count;

    public SessionAcquireResult TryAcquire(
        string accountName,
        long connectionId)
    {
        var normalizedAccount = NormalizeAccount(accountName);

        while (true)
        {
            if (_owners.TryAdd(normalizedAccount, connectionId))
            {
                return new SessionAcquireResult(
                    SessionAcquireStatus.Acquired,
                    connectionId);
            }

            if (!_owners.TryGetValue(normalizedAccount, out var owner))
            {
                continue;
            }

            return owner == connectionId
                ? new SessionAcquireResult(
                    SessionAcquireStatus.AlreadyOwnedByConnection,
                    owner)
                : new SessionAcquireResult(
                    SessionAcquireStatus.DuplicateAccount,
                    owner);
        }
    }

    public bool TryTransfer(
        string accountName,
        long expectedOwnerConnectionId,
        long newOwnerConnectionId)
    {
        var normalizedAccount = NormalizeAccount(accountName);

        return _owners.TryUpdate(
            normalizedAccount,
            newOwnerConnectionId,
            expectedOwnerConnectionId);
    }

    public bool Release(
        string accountName,
        long connectionId)
    {
        var normalizedAccount = NormalizeAccount(accountName);

        return ((ICollection<KeyValuePair<string, long>>)_owners)
            .Remove(new KeyValuePair<string, long>(
                normalizedAccount,
                connectionId));
    }

    public bool TryGetOwner(
        string accountName,
        out long connectionId)
    {
        var normalizedAccount = NormalizeAccount(accountName);
        return _owners.TryGetValue(normalizedAccount, out connectionId);
    }

    private static string NormalizeAccount(string accountName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        return accountName.Trim();
    }
}
