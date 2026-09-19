using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public sealed record WorldPresence(
    long ConnectionId,
    string AccountName,
    long AccountId,
    CharacterListEntry Character,
    DateTimeOffset EnteredAtUtc);

public enum WorldPresenceEnterStatus
{
    Entered,
    ConnectionAlreadyPresent,
    AccountAlreadyPresent,
    CharacterAlreadyPresent
}

public readonly record struct WorldPresenceEnterResult(
    WorldPresenceEnterStatus Status,
    long ExistingConnectionId)
{
    public bool Succeeded =>
        Status == WorldPresenceEnterStatus.Entered;
}

public sealed class WorldPresenceRegistry
{
    private readonly object _gate = new();

    private readonly Dictionary<long, WorldPresence> _byConnection = [];
    private readonly Dictionary<string, WorldPresence> _byAccount =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, WorldPresence> _byCharacter = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byConnection.Count;
            }
        }
    }

    public WorldPresenceEnterResult TryEnter(
        WorldPresence presence) =>
        TryEnter(presence, out _);

    public WorldPresenceEnterResult TryEnter(
        WorldPresence presence,
        out IReadOnlyList<WorldPresence> visiblePeers)
    {
        ArgumentNullException.ThrowIfNull(presence);
        visiblePeers = [];

        if (presence.ConnectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(presence),
                "Connection ID must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            presence.AccountName);

        if (presence.AccountId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(presence),
                "Account ID must be positive.");
        }

        ArgumentNullException.ThrowIfNull(presence.Character);

        if (presence.Character.CharacterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(presence),
                "Character ID must be positive.");
        }

        if (presence.Character.AccountId != presence.AccountId)
        {
            throw new ArgumentException(
                "The world character must belong to the authenticated account.",
                nameof(presence));
        }

        var normalizedAccount = presence.AccountName.Trim();
        var normalizedPresence = presence with
        {
            AccountName = normalizedAccount
        };

        lock (_gate)
        {
            if (_byConnection.TryGetValue(
                    presence.ConnectionId,
                    out var connectionOwner))
            {
                return new WorldPresenceEnterResult(
                    WorldPresenceEnterStatus.ConnectionAlreadyPresent,
                    connectionOwner.ConnectionId);
            }

            if (_byAccount.TryGetValue(
                    normalizedAccount,
                    out var accountOwner))
            {
                return new WorldPresenceEnterResult(
                    WorldPresenceEnterStatus.AccountAlreadyPresent,
                    accountOwner.ConnectionId);
            }

            if (_byCharacter.TryGetValue(
                    presence.Character.CharacterId,
                    out var characterOwner))
            {
                return new WorldPresenceEnterResult(
                    WorldPresenceEnterStatus.CharacterAlreadyPresent,
                    characterOwner.ConnectionId);
            }

            _byConnection.Add(
                normalizedPresence.ConnectionId,
                normalizedPresence);
            _byAccount.Add(
                normalizedPresence.AccountName,
                normalizedPresence);
            _byCharacter.Add(
                normalizedPresence.Character.CharacterId,
                normalizedPresence);

            visiblePeers = VisiblePeersFor(
                normalizedPresence);

            return new WorldPresenceEnterResult(
                WorldPresenceEnterStatus.Entered,
                normalizedPresence.ConnectionId);
        }
    }

    public bool TryLeave(
        long connectionId,
        out WorldPresence? presence) =>
        TryLeave(
            connectionId,
            out presence,
            out _);

    public bool TryLeave(
        long connectionId,
        out WorldPresence? presence,
        out IReadOnlyList<WorldPresence> visiblePeers)
    {
        lock (_gate)
        {
            if (!_byConnection.Remove(
                    connectionId,
                    out presence))
            {
                visiblePeers = [];
                return false;
            }

            _byAccount.Remove(presence.AccountName);
            _byCharacter.Remove(presence.Character.CharacterId);

            visiblePeers = VisiblePeersFor(presence);
            return true;
        }
    }

    public bool TryGetByConnection(
        long connectionId,
        out WorldPresence? presence)
    {
        lock (_gate)
        {
            return _byConnection.TryGetValue(
                connectionId,
                out presence);
        }
    }

    public IReadOnlyList<WorldPresence> VisiblePeers(
        long connectionId)
    {
        lock (_gate)
        {
            return _byConnection.TryGetValue(
                    connectionId,
                    out var presence)
                ? VisiblePeersFor(presence)
                : [];
        }
    }

    public bool TryGetByCharacter(
        long characterId,
        out WorldPresence? presence)
    {
        lock (_gate)
        {
            return _byCharacter.TryGetValue(
                characterId,
                out presence);
        }
    }

    public bool TryChangeMap(
        long connectionId,
        long characterId,
        long destinationMapId,
        int destinationX,
        int destinationY,
        out WorldPresence? previousPresence,
        out WorldPresence? updatedPresence,
        out IReadOnlyList<WorldPresence> previousVisiblePeers,
        out IReadOnlyList<WorldPresence> newVisiblePeers) =>
        TryChangeMap(
            connectionId,
            characterId,
            destinationMapId,
            destinationX,
            destinationY,
            null,
            null,
            null,
            out previousPresence,
            out updatedPresence,
            out previousVisiblePeers,
            out newVisiblePeers);

    public bool TryChangeMap(
        long connectionId,
        long characterId,
        long destinationMapId,
        int destinationX,
        int destinationY,
        long? expectedRuntimeVersion,
        long? runtimeVersion,
        string? concurrencyToken,
        out WorldPresence? previousPresence,
        out WorldPresence? updatedPresence,
        out IReadOnlyList<WorldPresence> previousVisiblePeers,
        out IReadOnlyList<WorldPresence> newVisiblePeers)
    {
        if (connectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionId));
        }

        if (characterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(characterId));
        }

        if (destinationMapId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationMapId));
        }

        lock (_gate)
        {
            if (!_byConnection.TryGetValue(
                    connectionId,
                    out var existing) ||
                existing.Character.CharacterId != characterId ||
                (expectedRuntimeVersion.HasValue &&
                 existing.Character.RuntimeVersion !=
                     expectedRuntimeVersion.Value))
            {
                previousPresence = null;
                updatedPresence = null;
                previousVisiblePeers = [];
                newVisiblePeers = [];
                return false;
            }

            previousPresence = existing;
            previousVisiblePeers =
                VisiblePeersFor(existing);

            var updatedCharacter = existing.Character with
            {
                MapId = destinationMapId,
                PositionX = destinationX,
                PositionY = destinationY,
                RuntimeVersion =
                    runtimeVersion ?? existing.Character.RuntimeVersion,
                ConcurrencyToken =
                    concurrencyToken ?? existing.Character.ConcurrencyToken
            };

            updatedPresence = existing with
            {
                Character = updatedCharacter
            };

            _byConnection[connectionId] = updatedPresence;
            _byAccount[updatedPresence.AccountName] =
                updatedPresence;
            _byCharacter[characterId] =
                updatedPresence;

            newVisiblePeers =
                VisiblePeersFor(updatedPresence);

            return true;
        }
    }

    public bool TryMove(
        long connectionId,
        long characterId,
        ushort x,
        ushort y,
        long runtimeVersion,
        string concurrencyToken,
        out WorldPresence? updatedPresence)
    {
        if (connectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionId));
        }

        if (characterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(characterId));
        }

        lock (_gate)
        {
            if (!_byConnection.TryGetValue(
                    connectionId,
                    out var existing) ||
                existing.Character.CharacterId != characterId)
            {
                updatedPresence = null;
                return false;
            }

            var updatedCharacter = existing.Character with
            {
                PositionX = x,
                PositionY = y,
                RuntimeVersion = runtimeVersion,
                ConcurrencyToken = concurrencyToken
            };

            updatedPresence = existing with
            {
                Character = updatedCharacter
            };

            _byConnection[connectionId] = updatedPresence;
            _byAccount[updatedPresence.AccountName] =
                updatedPresence;
            _byCharacter[characterId] =
                updatedPresence;

            return true;
        }
    }

    public bool TryMove(
        long connectionId,
        long characterId,
        ushort x,
        ushort y,
        out WorldPresence? updatedPresence)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(
                    connectionId,
                    out var existing) ||
                existing.Character.CharacterId != characterId)
            {
                updatedPresence = null;
                return false;
            }

            var runtimeVersion =
                existing.Character.RuntimeVersion;

            var concurrencyToken =
                existing.Character.ConcurrencyToken;

            var updatedCharacter = existing.Character with
            {
                PositionX = x,
                PositionY = y
            };

            updatedPresence = existing with
            {
                Character = updatedCharacter
            };

            _byConnection[connectionId] = updatedPresence;
            _byAccount[updatedPresence.AccountName] =
                updatedPresence;
            _byCharacter[characterId] =
                updatedPresence;

            return true;
        }
    }

    private IReadOnlyList<WorldPresence> VisiblePeersFor(
        WorldPresence presence)
    {
        if (presence.Character.MapId is not long mapId)
        {
            return [];
        }

        return _byConnection.Values
            .Where(other =>
                other.ConnectionId != presence.ConnectionId &&
                other.Character.MapId == mapId)
            .OrderBy(other => other.ConnectionId)
            .ToArray();
    }

    public IReadOnlyList<WorldPresence> Snapshot()
    {
        lock (_gate)
        {
            return _byConnection.Values
                .OrderBy(presence => presence.ConnectionId)
                .ToArray();
        }
    }
}
