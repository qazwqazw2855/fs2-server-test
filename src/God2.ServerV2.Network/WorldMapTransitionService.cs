using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public sealed record WorldMapTransitionResult(
    WorldPresence PreviousPresence,
    WorldPresence UpdatedPresence,
    IReadOnlyList<WorldPresence> PreviousVisiblePeers,
    IReadOnlyList<WorldPresence> NewVisiblePeers,
    NpcInteractionSession? ReleasedInteraction,
    int PlayerLeftEventsQueued,
    int PlayerEnteredEventsQueued);

public sealed class WorldMapTransitionService
{
    private readonly WorldPresenceRegistry _worldPresences;
    private readonly NpcInteractionSessionRegistry _npcInteractions;
    private readonly WorldReplicationOutboxRegistry
        _replicationOutboxes;
    private readonly WorldNpcStateService? _worldNpcStateService;
    private readonly ICharacterMapTransitionWriter?
        _characterMapTransitionWriter;

    public WorldMapTransitionService(
        WorldPresenceRegistry worldPresences,
        NpcInteractionSessionRegistry npcInteractions,
        WorldReplicationOutboxRegistry replicationOutboxes,
        WorldNpcStateService? worldNpcStateService = null,
        ICharacterMapTransitionWriter? characterMapTransitionWriter = null)
    {
        _worldPresences = worldPresences ??
            throw new ArgumentNullException(
                nameof(worldPresences));

        _npcInteractions = npcInteractions ??
            throw new ArgumentNullException(
                nameof(npcInteractions));

        _replicationOutboxes = replicationOutboxes ??
            throw new ArgumentNullException(
                nameof(replicationOutboxes));

        _worldNpcStateService = worldNpcStateService;
        _characterMapTransitionWriter =
            characterMapTransitionWriter;
    }

    public async ValueTask<WorldMapTransitionResult?>
        TryTransitionAsync(
            long connectionId,
            long characterId,
            long destinationMapId,
            int destinationX,
            int destinationY,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
    {
        if (_worldNpcStateService is null)
        {
            throw new InvalidOperationException(
                "World NPC state service is required for async map transitions.");
        }

        if (!_worldPresences.TryGetByConnection(
                connectionId,
                out var currentPresence) ||
            currentPresence!.Character.CharacterId != characterId)
        {
            Console.WriteLine(
                "[WorldMapTransition] PRECHECK_FAILED " +
                $"connection={connectionId}; " +
                $"character={characterId}; " +
                $"presenceFound={currentPresence is not null}; " +
                $"presenceCharacter=" +
                $"{currentPresence?.Character.CharacterId}");
            return null;
        }

        Console.WriteLine(
            "[WorldMapTransition] PRECHECK_OK " +
            $"connection={connectionId}; " +
            $"character={characterId}; " +
            $"currentMap={currentPresence.Character.MapId}; " +
            $"currentX={currentPresence.Character.PositionX}; " +
            $"currentY={currentPresence.Character.PositionY}; " +
            $"runtimeVersion={currentPresence.Character.RuntimeVersion}; " +
            $"token={currentPresence.Character.ConcurrencyToken}");

        await _worldNpcStateService.EnsureLoadedAsync(
            destinationMapId,
            cancellationToken);

        long? runtimeVersion = null;
        string? concurrencyToken = null;

        if (_characterMapTransitionWriter is not null)
        {
            var character = currentPresence.Character;

            var writeResult =
                await _characterMapTransitionWriter.TryUpdateAsync(
                    new CharacterMapTransitionWriteRequest(
                        characterId,
                        destinationMapId,
                        destinationX,
                        destinationY,
                        character.RuntimeVersion,
                        character.ConcurrencyToken),
                    cancellationToken);

            if (!writeResult.Updated)
            {
                Console.WriteLine(
                    "[WorldMapTransition] DB_TRANSITION_CONFLICT " +
                    $"character={characterId}; " +
                    $"expectedVersion={character.RuntimeVersion}; " +
                    $"expectedToken={character.ConcurrencyToken}; " +
                    $"destinationMap={destinationMapId}; " +
                    $"destinationX={destinationX}; " +
                    $"destinationY={destinationY}");
                return null;
            }

            runtimeVersion = writeResult.RuntimeVersion;
            concurrencyToken = writeResult.ConcurrencyToken;

            Console.WriteLine(
                "[WorldMapTransition] DB_TRANSITION_OK " +
                $"character={characterId}; " +
                $"expectedVersion={character.RuntimeVersion}; " +
                $"newVersion={runtimeVersion}; " +
                $"newToken={concurrencyToken}; " +
                $"destinationMap={destinationMapId}; " +
                $"destinationX={destinationX}; " +
                $"destinationY={destinationY}");
        }

        var expectedRuntimeVersion =
            currentPresence.Character.RuntimeVersion;

        Console.WriteLine(
            "[WorldMapTransition] CORE_BEGIN " +
            $"connection={connectionId}; " +
            $"character={characterId}; " +
            $"expectedVersion={expectedRuntimeVersion}; " +
            $"newVersion={runtimeVersion}; " +
            $"destinationMap={destinationMapId}; " +
            $"destinationX={destinationX}; " +
            $"destinationY={destinationY}");

        var coreSucceeded = TryTransitionCore(
            connectionId,
            characterId,
            destinationMapId,
            destinationX,
            destinationY,
            expectedRuntimeVersion,
            runtimeVersion,
            concurrencyToken,
            nowUtc,
            out var result);

        Console.WriteLine(
            "[WorldMapTransition] CORE_RESULT " +
            $"success={coreSucceeded}; " +
            $"resultPresent={result is not null}");

        return coreSucceeded
            ? result
            : null;
    }

    public bool TryTransition(
        long connectionId,
        long characterId,
        long destinationMapId,
        int destinationX,
        int destinationY,
        DateTimeOffset nowUtc,
        out WorldMapTransitionResult? result)
    {
        return TryTransitionCore(
            connectionId,
            characterId,
            destinationMapId,
            destinationX,
            destinationY,
            null,
            null,
            null,
            nowUtc,
            out result);
    }

    private bool TryTransitionCore(
        long connectionId,
        long characterId,
        long destinationMapId,
        int destinationX,
        int destinationY,
        long? expectedRuntimeVersion,
        long? runtimeVersion,
        string? concurrencyToken,
        DateTimeOffset nowUtc,
        out WorldMapTransitionResult? result)
    {
        if (!_worldPresences.TryChangeMap(
                connectionId,
                characterId,
                destinationMapId,
                destinationX,
                destinationY,
                expectedRuntimeVersion,
                runtimeVersion,
                concurrencyToken,
                out var previousPresence,
                out var updatedPresence,
                out var previousVisiblePeers,
                out var newVisiblePeers))
        {
            result = null;
            return false;
        }

        _npcInteractions.Remove(
            connectionId,
            out var releasedInteraction);

        var playerLeftEventsQueued = 0;

        foreach (var peer in previousVisiblePeers)
        {
            if (_replicationOutboxes.TryEnqueue(
                    peer.ConnectionId,
                    WorldReplicationEventKind.PlayerLeft,
                    previousPresence!,
                    nowUtc,
                    out _))
            {
                playerLeftEventsQueued++;
            }
        }

        var playerEnteredEventsQueued = 0;

        foreach (var peer in newVisiblePeers)
        {
            if (_replicationOutboxes.TryEnqueue(
                    peer.ConnectionId,
                    WorldReplicationEventKind.PlayerEntered,
                    updatedPresence!,
                    nowUtc,
                    out _))
            {
                playerEnteredEventsQueued++;
            }

            if (_replicationOutboxes.TryEnqueue(
                    connectionId,
                    WorldReplicationEventKind.PlayerEntered,
                    peer,
                    nowUtc,
                    out _))
            {
                playerEnteredEventsQueued++;
            }
        }

        result = new WorldMapTransitionResult(
            previousPresence!,
            updatedPresence!,
            previousVisiblePeers,
            newVisiblePeers,
            releasedInteraction,
            playerLeftEventsQueued,
            playerEnteredEventsQueued);

        return true;
    }
}
