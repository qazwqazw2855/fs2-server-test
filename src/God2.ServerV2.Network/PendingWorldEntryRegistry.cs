using System.Collections.Concurrent;
using God2.ServerV2.Application;
using God2.ServerV2.Session;

namespace God2.ServerV2.Network;

public sealed record PendingWorldEntry(
    string RemoteAddress,
    string AccountName,
    long LoginConnectionId,
    long ReservationConnectionId,
    long AccountId,
    byte SelectedServerId,
    CharacterListEntry Character,
    DateTimeOffset ExpiresAtUtc);

public sealed class PendingWorldEntryRegistry
{
    private readonly ConcurrentDictionary<string, PendingWorldEntry> _entries =
        new(StringComparer.Ordinal);

    private readonly SessionRegistry _sessionRegistry;
    private readonly TimeSpan _timeToLive;
    private long _nextReservationConnectionId = long.MaxValue;

    public PendingWorldEntryRegistry(
        SessionRegistry sessionRegistry,
        TimeSpan? timeToLive = null)
    {
        _sessionRegistry = sessionRegistry ??
            throw new ArgumentNullException(nameof(sessionRegistry));
        _timeToLive = timeToLive ?? TimeSpan.FromMinutes(2);

        if (_timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }
    }

    public bool TryReserve(
        string remoteAddress,
        string accountName,
        long loginConnectionId,
        long accountId,
        byte selectedServerId,
        CharacterListEntry character,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(character);

        if (loginConnectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(loginConnectionId));
        }

        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        if (character.AccountId != accountId)
        {
            throw new ArgumentException(
                "The pending character must belong to the authenticated account.",
                nameof(character));
        }

        RemoveExpired(nowUtc);

        var normalizedAccount = accountName.Trim();
        var reservationConnectionId =
            Interlocked.Decrement(ref _nextReservationConnectionId);

        if (!_sessionRegistry.TryTransfer(
                normalizedAccount,
                loginConnectionId,
                reservationConnectionId))
        {
            return false;
        }

        var entry = new PendingWorldEntry(
            remoteAddress,
            normalizedAccount,
            loginConnectionId,
            reservationConnectionId,
            accountId,
            selectedServerId,
            character,
            nowUtc.Add(_timeToLive));

        if (_entries.TryAdd(remoteAddress, entry))
        {
            return true;
        }

        if (!_sessionRegistry.TryTransfer(
                normalizedAccount,
                reservationConnectionId,
                loginConnectionId))
        {
            throw new InvalidOperationException(
                "Pending world entry ownership rollback failed.");
        }

        return false;
    }

    public bool TryClaim(
        string remoteAddress,
        DateTimeOffset nowUtc,
        out PendingWorldEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);

        entry = null;

        if (!_entries.TryRemove(remoteAddress, out var candidate))
        {
            return false;
        }

        if (candidate.ExpiresAtUtc <= nowUtc)
        {
            _sessionRegistry.Release(
                candidate.AccountName,
                candidate.ReservationConnectionId);

            return false;
        }

        entry = candidate;
        return true;
    }

    public int RemoveExpired(DateTimeOffset nowUtc)
    {
        var removed = 0;

        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAtUtc <= nowUtc &&
                ((ICollection<KeyValuePair<string, PendingWorldEntry>>)_entries)
                    .Remove(pair))
            {
                // Remove only the entry observed as expired; another thread
                // may have claimed it and reserved a fresh entry at this key.
                _sessionRegistry.Release(
                    pair.Value.AccountName,
                    pair.Value.ReservationConnectionId);

                removed++;
            }
        }

        return removed;
    }
}
