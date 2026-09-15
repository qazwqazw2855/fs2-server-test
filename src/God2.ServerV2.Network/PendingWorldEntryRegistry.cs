using System.Collections.Concurrent;
using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public sealed record PendingWorldEntry(
    string RemoteAddress,
    string AccountName,
    long LoginConnectionId,
    long AccountId,
    byte SelectedServerId,
    CharacterListEntry Character,
    DateTimeOffset ExpiresAtUtc);

public sealed class PendingWorldEntryRegistry
{
    private readonly ConcurrentDictionary<string, PendingWorldEntry> _entries =
        new(StringComparer.Ordinal);

    private readonly TimeSpan _timeToLive;

    public PendingWorldEntryRegistry(TimeSpan? timeToLive = null)
    {
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

        return _entries.TryAdd(
            remoteAddress,
            new PendingWorldEntry(
                remoteAddress,
                accountName.Trim(),
                loginConnectionId,
                accountId,
                selectedServerId,
                character,
                nowUtc.Add(_timeToLive)));
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
                _entries.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}
