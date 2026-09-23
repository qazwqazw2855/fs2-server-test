using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldMapTransitionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Async_transition_loads_destination_map_first()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();
        var worldNpcs =
            new WorldNpcRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));

        outboxes.TryRegister(101);

        var repository =
            new TransitionNpcRepository(
                [Npc(200, 5004)]);

        var npcStateService =
            new WorldNpcStateService(
                new NpcSnapshotService(repository),
                worldNpcs);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes,
                npcStateService);

        var result =
            await service.TryTransitionAsync(
                101,
                1,
                200,
                65,
                64,
                Now,
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(
            200,
            result!.UpdatedPresence.Character.MapId);

        Assert.True(
            worldNpcs.IsMapLoaded(200));

        Assert.Single(
            worldNpcs.SnapshotMap(200));

        Assert.Equal(
            1,
            repository.CallCount);
    }

    [Fact]
    public async Task Runtime_version_change_after_database_commit_reports_inconsistent_state()
    {
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var outboxes = new WorldReplicationOutboxRegistry();
        var worldNpcs = new WorldNpcRegistry();

        presences.TryEnter(Presence(101, 1, 100));

        interactions.TryOpen(
            101, 1, 100, 3793,
            [Npc(100, 3793)],
            Now);

        outboxes.TryRegister(101);

        var npcStateService =
            new WorldNpcStateService(
                new NpcSnapshotService(
                    new TransitionNpcRepository(
                        [Npc(200, 5004)])),
                worldNpcs);

        var writer =
            new RacingMapTransitionWriter(presences);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes,
                npcStateService,
                writer);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await service.TryTransitionAsync(
                        101, 1, 200, 65, 64,
                        Now,
                        CancellationToken.None);
                });

        Assert.Contains(
            "database commit succeeded",
            error.Message);

        Assert.True(
            presences.TryGetByCharacter(1, out var unchanged));

        Assert.Equal(100, unchanged!.Character.MapId);
        Assert.Equal(1, interactions.Count);
        Assert.Empty(outboxes.Snapshot(101));
    }

    [Fact]
    public async Task Disconnect_during_destination_load_does_not_write_database()
    {
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var outboxes = new WorldReplicationOutboxRegistry();
        var worldNpcs = new WorldNpcRegistry();

        presences.TryEnter(Presence(101, 1, 100));
        outboxes.TryRegister(101);

        var writer = new CountingMapTransitionWriter();
        var npcStateService = new WorldNpcStateService(
            new NpcSnapshotService(new LeavingNpcRepository(presences)),
            worldNpcs);
        var service = new WorldMapTransitionService(
            presences, interactions, outboxes, npcStateService, writer);

        var result = await service.TryTransitionAsync(
            101, 1, 200, 65, 64, Now, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, writer.CallCount);
        Assert.False(presences.TryGetByCharacter(1, out _));
        Assert.Empty(outboxes.Snapshot(101));
    }

    [Fact]
    public async Task Persistence_success_updates_runtime_concurrency_state()
    {
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var outboxes = new WorldReplicationOutboxRegistry();
        var worldNpcs = new WorldNpcRegistry();

        presences.TryEnter(Presence(101, 1, 100));
        outboxes.TryRegister(101);

        var npcStateService =
            new WorldNpcStateService(
                new NpcSnapshotService(
                    new TransitionNpcRepository(
                        [Npc(200, 5004)])),
                worldNpcs);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes,
                npcStateService,
                new SuccessfulMapTransitionWriter());

        var result = await service.TryTransitionAsync(
            101, 1, 200, 65, 64,
            Now,
            CancellationToken.None);

        Assert.NotNull(result);

        var character = result!.UpdatedPresence.Character;

        Assert.Equal(200, character.MapId);
        Assert.Equal(65, character.PositionX);
        Assert.Equal(64, character.PositionY);
        Assert.Equal(1, character.RuntimeVersion);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            character.ConcurrencyToken);
    }

    [Fact]
    public async Task Persistence_conflict_preserves_old_world_state()
    {
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var outboxes = new WorldReplicationOutboxRegistry();
        var worldNpcs = new WorldNpcRegistry();

        presences.TryEnter(Presence(101, 1, 100));

        interactions.TryOpen(
            101, 1, 100, 3793,
            [Npc(100, 3793)],
            Now);

        outboxes.TryRegister(101);

        var npcStateService =
            new WorldNpcStateService(
                new NpcSnapshotService(
                    new TransitionNpcRepository(
                        [Npc(200, 5004)])),
                worldNpcs);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes,
                npcStateService,
                new ConflictMapTransitionWriter());

        var result = await service.TryTransitionAsync(
            101, 1, 200, 65, 64,
            Now,
            CancellationToken.None);

        Assert.Null(result);

        Assert.True(
            presences.TryGetByCharacter(1, out var unchanged));

        Assert.Equal(100, unchanged!.Character.MapId);
        Assert.Equal(1, interactions.Count);
        Assert.Empty(outboxes.Snapshot(101));
    }

    [Fact]
    public async Task Destination_load_failure_preserves_old_world_state()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();
        var worldNpcs =
            new WorldNpcRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));

        interactions.TryOpen(
            101,
            1,
            100,
            3793,
            [Npc(100, 3793)],
            Now);

        outboxes.TryRegister(101);

        var npcStateService =
            new WorldNpcStateService(
                new NpcSnapshotService(
                    new ThrowingNpcRepository()),
                worldNpcs);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes,
                npcStateService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.TryTransitionAsync(
                    101,
                    1,
                    200,
                    65,
                    64,
                    Now,
                    CancellationToken.None));

        Assert.True(
            presences.TryGetByCharacter(
                1,
                out var unchanged));

        Assert.Equal(
            100,
            unchanged!.Character.MapId);
        Assert.Equal(1, interactions.Count);
        Assert.False(worldNpcs.IsMapLoaded(200));
    }

    [Fact]
    public void Transition_releases_old_map_npc_interaction()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));

        interactions.TryOpen(
            101,
            1,
            100,
            3793,
            [Npc(100, 3793)],
            Now);

        outboxes.TryRegister(101);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes);

        Assert.True(
            service.TryTransition(
                101,
                1,
                200,
                65,
                64,
                Now,
                out var result));

        Assert.NotNull(result!.ReleasedInteraction);
        Assert.Equal(
            3793U,
            result.ReleasedInteraction!.ClientEntityHandle);
        Assert.Equal(0, interactions.Count);
        Assert.Equal(
            200,
            result.UpdatedPresence.Character.MapId);
    }

    [Fact]
    public void Transition_queues_leave_for_old_map_peers()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));
        presences.TryEnter(
            Presence(202, 2, 100));

        outboxes.TryRegister(101);
        outboxes.TryRegister(202);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes);

        Assert.True(
            service.TryTransition(
                101,
                1,
                200,
                65,
                64,
                Now,
                out var result));

        var peerEvents =
            outboxes.Snapshot(202);

        Assert.Single(peerEvents);
        Assert.Equal(
            WorldReplicationEventKind.PlayerLeft,
            peerEvents[0].Kind);
        Assert.Equal(
            101,
            peerEvents[0].Subject.ConnectionId);
        Assert.Equal(
            100,
            peerEvents[0].Subject.Character.MapId);

        Assert.Equal(
            1,
            result!.PlayerLeftEventsQueued);
    }

    [Fact]
    public void Transition_queues_enter_for_new_map_both_directions()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));
        presences.TryEnter(
            Presence(303, 3, 200));

        outboxes.TryRegister(101);
        outboxes.TryRegister(303);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes);

        Assert.True(
            service.TryTransition(
                101,
                1,
                200,
                65,
                64,
                Now,
                out var result));

        var destinationPeerEvents =
            outboxes.Snapshot(303);

        Assert.Single(destinationPeerEvents);
        Assert.Equal(
            WorldReplicationEventKind.PlayerEntered,
            destinationPeerEvents[0].Kind);
        Assert.Equal(
            101,
            destinationPeerEvents[0].Subject.ConnectionId);
        Assert.Equal(
            200,
            destinationPeerEvents[0].Subject.Character.MapId);

        var movingPlayerEvents =
            outboxes.Snapshot(101);

        Assert.Single(movingPlayerEvents);
        Assert.Equal(
            WorldReplicationEventKind.PlayerEntered,
            movingPlayerEvents[0].Kind);
        Assert.Equal(
            303,
            movingPlayerEvents[0].Subject.ConnectionId);

        Assert.Equal(
            2,
            result!.PlayerEnteredEventsQueued);
    }

    [Fact]
    public void Two_players_change_maps_and_leave_without_stale_peer_events()
    {
        var presences = new WorldPresenceRegistry();
        var interactions = new NpcInteractionSessionRegistry();
        var outboxes = new WorldReplicationOutboxRegistry();

        presences.TryEnter(Presence(101, 1, 100));
        presences.TryEnter(Presence(202, 2, 100));
        presences.TryEnter(Presence(303, 3, 200));
        outboxes.TryRegister(101);
        outboxes.TryRegister(202);
        outboxes.TryRegister(303);

        var service = new WorldMapTransitionService(
            presences, interactions, outboxes);

        Assert.True(service.TryTransition(
            101, 1, 200, 65, 64, Now, out var first));
        Assert.Equal(1, first!.PlayerLeftEventsQueued);
        Assert.Equal(2, first.PlayerEnteredEventsQueued);
        Assert.Equal(100, Assert.Single(outboxes.Snapshot(202))
            .Subject.Character.MapId);

        Assert.True(service.TryTransition(
            202, 2, 200, 70, 75, Now, out var second));
        Assert.Empty(second!.PreviousVisiblePeers);
        Assert.Equal(2, second.NewVisiblePeers.Count);
        Assert.Equal(4, second.PlayerEnteredEventsQueued);

        Assert.True(presences.TryLeave(
            101, out var departed, out var remainingPeers));
        Assert.Equal(200, departed!.Character.MapId);
        Assert.Equal(2, remainingPeers.Count);
        Assert.DoesNotContain(
            remainingPeers, peer => peer.ConnectionId == 101);
        Assert.Empty(presences.VisiblePeers(101));
        Assert.Empty(presences.Snapshot()
            .Where(peer => peer.Character.MapId == 100));

        // Queued events retain the state at the time of each transition.
        Assert.Equal(100, Assert.Single(outboxes.Snapshot(202)
            .Where(e => e.Kind == WorldReplicationEventKind.PlayerLeft))
            .Subject.Character.MapId);
        Assert.Equal(200, Assert.Single(outboxes.Snapshot(303)
            .Where(e => e.Kind == WorldReplicationEventKind.PlayerEntered &&
                        e.Subject.ConnectionId == 101))
            .Subject.Character.MapId);
    }

    [Fact]
    public void Failed_transition_does_not_release_interaction()
    {
        var presences = new WorldPresenceRegistry();
        var interactions =
            new NpcInteractionSessionRegistry();
        var outboxes =
            new WorldReplicationOutboxRegistry();

        presences.TryEnter(
            Presence(101, 1, 100));

        interactions.TryOpen(
            101,
            1,
            100,
            3793,
            [Npc(100, 3793)],
            Now);

        outboxes.TryRegister(101);

        var service =
            new WorldMapTransitionService(
                presences,
                interactions,
                outboxes);

        Assert.False(
            service.TryTransition(
                202,
                1,
                200,
                65,
                64,
                Now,
                out var result));

        Assert.Null(result);
        Assert.Equal(1, interactions.Count);

        Assert.True(
            presences.TryGetByCharacter(
                1,
                out var unchanged));

        Assert.Equal(
            100,
            unchanged!.Character.MapId);
    }

    private sealed class TransitionNpcRepository :
        INpcSnapshotRepository
    {
        private readonly IReadOnlyList<NpcSnapshotEntry>
            _entries;

        public TransitionNpcRepository(
            IReadOnlyList<NpcSnapshotEntry> entries)
        {
            _entries = entries;
        }

        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<NpcSnapshotEntry>>
            ListByMapAsync(
                long mapId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(_entries);
        }
    }

    private sealed class RacingMapTransitionWriter :
        ICharacterMapTransitionWriter
    {
        private readonly WorldPresenceRegistry _presences;

        public RacingMapTransitionWriter(
            WorldPresenceRegistry presences)
        {
            _presences = presences;
        }

        public ValueTask<CharacterMapTransitionWriteResult>
            TryUpdateAsync(
                CharacterMapTransitionWriteRequest request,
                CancellationToken cancellationToken)
        {
            _presences.TryChangeMap(
                101,
                1,
                100,
                11,
                21,
                request.ExpectedRuntimeVersion,
                request.ExpectedRuntimeVersion + 1,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                out _,
                out _,
                out _,
                out _);

            return ValueTask.FromResult(
                CharacterMapTransitionWriteResult.Success(
                    request.ExpectedRuntimeVersion + 1,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }
    }

    private sealed class SuccessfulMapTransitionWriter :
        ICharacterMapTransitionWriter
    {
        public ValueTask<CharacterMapTransitionWriteResult>
            TryUpdateAsync(
                CharacterMapTransitionWriteRequest request,
                CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                CharacterMapTransitionWriteResult.Success(
                    request.ExpectedRuntimeVersion + 1,
                    "0123456789abcdef0123456789abcdef"));
    }

    private sealed class ConflictMapTransitionWriter :
        ICharacterMapTransitionWriter
    {
        public ValueTask<CharacterMapTransitionWriteResult>
            TryUpdateAsync(
                CharacterMapTransitionWriteRequest request,
                CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                CharacterMapTransitionWriteResult.Conflict);
    }

    private sealed class LeavingNpcRepository(
        WorldPresenceRegistry presences) : INpcSnapshotRepository
    {
        public ValueTask<IReadOnlyList<NpcSnapshotEntry>> ListByMapAsync(
            long mapId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            presences.TryLeave(101, out _);
            return ValueTask.FromResult<IReadOnlyList<NpcSnapshotEntry>>([]);
        }
    }

    private sealed class CountingMapTransitionWriter :
        ICharacterMapTransitionWriter
    {
        public int CallCount { get; private set; }

        public ValueTask<CharacterMapTransitionWriteResult> TryUpdateAsync(
            CharacterMapTransitionWriteRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(
                CharacterMapTransitionWriteResult.Conflict);
        }
    }

    private sealed class ThrowingNpcRepository :
        INpcSnapshotRepository
    {
        public ValueTask<IReadOnlyList<NpcSnapshotEntry>>
            ListByMapAsync(
                long mapId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                "Simulated destination map load failure.");
        }
    }

    private static WorldPresence Presence(
        long connectionId,
        long characterId,
        long mapId) =>
        new(
            connectionId,
            $"account-{characterId}",
            characterId,
            new CharacterListEntry(
                CharacterId: characterId,
                AccountId: characterId,
                Name: $"character-{characterId}",
                ClassCode: null,
                GenderCode: null,
                LifeSkillCode: null,
                Level: 1,
                AppearanceCode: null,
                MapId: mapId,
                PositionX: 10,
                PositionY: 20,
                CreatedAtUtc: Now,
                LastPlayedAtUtc: null),
            Now);

    private static NpcSnapshotEntry Npc(
        long mapId,
        uint handle) =>
        new(
            SpawnId: 170153793,
            NpcId: 170015087,
            Name: "NPC",
            MapId: mapId,
            PositionX: 65,
            PositionY: 61,
            ClientBuildId: null,
            ClientEntityHandle: handle,
            ResourceType: null,
            ResourceOrdinal: null,
            SelectorHighBits: null,
            DirectionCode: null,
            StateCode: null,
            SpawnMessageSha256: null,
            OpaqueTemplateSha256: null,
            WireEvidenceStatus: "EvidenceBlocked",
            WireEvidenceReference: null);
}
