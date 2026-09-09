using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RoadMapM3NpcReplicationTests
{
    [Fact]
    public void Live_dialog_spawn_is_reconstructed_from_the_observed_nested_entity_record()
    {
        var state = Npc(3793, 65, 61, 3, 5,
            "FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF") with
        {
            WireIdentity = new OfficialNpcWireIdentity(
                OfficialNpcReplicationWireCodec.ClientBuildId,
                3793,
                0,
                87,
                3,
                5,
                1,
                "FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF",
                "Verified",
                "LiveRecovery/attempt-759-decode-2579",
                OfficialNpcReplicationWireCodec.LiveStageMerchantOpaqueTemplateSha256)
        };

        var result = new NpcSerializer().Serialize(state);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Frame);

        Assert.Equal(OfficialSerializerStatus.Ready, result.Status);
        Assert.Equal("180072D10E000058600000A1000000CF01000004017A0075", Convert.ToHexString(decoded));
    }

    [Theory]
    [InlineData(1504U, 17, 8, 3, 4, "72E00500008F62000081EA680FC2034A0146001000")]
    [InlineData(4638U, 14, 14, 2, 5, "721E1200008F420000A1EA680FC2034A013A001C00")]
    public void Spawn_serializer_reproduces_build_pinned_application_record(
        uint handle,
        int x,
        int y,
        byte selector,
        byte direction,
        string expectedApplicationHex)
    {
        var state = Npc(handle, x, y, selector, direction, Sha256(expectedApplicationHex));

        var result = new NpcSerializer().Serialize(state);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Frame);

        Assert.Equal(OfficialSerializerStatus.Ready, result.Status);
        Assert.Equal(24, decoded.Length);
        Assert.Equal(expectedApplicationHex, Convert.ToHexString(decoded.AsSpan(2, 21)));
        Assert.Equal(decoded[^1], OfficialLoginWireTransform.ComputeChecksum(decoded));
    }

    [Fact]
    public void Spawn_serializer_changes_only_proven_packed_position_fields()
    {
        var original = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            new NpcSerializer().Serialize(Npc(
                1504,
                17,
                8,
                3,
                4,
                "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B")).Frame);
        var moved = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            new NpcSerializer().Serialize(Npc(
                1504,
                31,
                42,
                3,
                4,
                "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B")).Frame);

        Assert.Equal(original.AsSpan(0, 19).ToArray(), moved.AsSpan(0, 19).ToArray());
        Assert.Equal<uint>(31U, (BinaryPrimitives.ReadUInt32LittleEndian(moved.AsSpan(19, 4)) >> 2) & 0x7FFF);
        Assert.Equal<uint>(42U, BinaryPrimitives.ReadUInt32LittleEndian(moved.AsSpan(19, 4)) >> 17);
        Assert.NotEqual(original.AsSpan(19, 4).ToArray(), moved.AsSpan(19, 4).ToArray());
        Assert.Equal(moved[^1], OfficialLoginWireTransform.ComputeChecksum(moved));
    }

    [Fact]
    public void Update_and_despawn_use_statically_verified_client_consumers()
    {
        var state = Npc(
            1504,
            31,
            42,
            3,
            4,
            "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B");

        var update = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            new NpcPositionUpdateSerializer().Serialize(state).Frame);
        var despawn = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            new NpcDespawnSerializer().Serialize(state).Frame);

        Assert.Equal(OfficialNpcReplicationWireCodec.PositionUpdateOpcode, update[2]);
        Assert.Equal(1504U, BinaryPrimitives.ReadUInt32LittleEndian(update.AsSpan(3, 4)));
        Assert.Equal(1000, BinaryPrimitives.ReadUInt16LittleEndian(update.AsSpan(9, 2)));
        Assert.Equal(0x02, update[11] & 0x1F);
        Assert.Equal(update[^1], OfficialLoginWireTransform.ComputeChecksum(update));
        Assert.Equal(8, despawn.Length);
        Assert.Equal(OfficialNpcReplicationWireCodec.DespawnOpcode, despawn[2]);
        Assert.Equal(1504U, BinaryPrimitives.ReadUInt32LittleEndian(despawn.AsSpan(3, 4)));
        Assert.Equal(despawn[^1], OfficialLoginWireTransform.ComputeChecksum(despawn));
    }

    [Fact]
    public void Unverified_or_unknown_npc_identity_fails_closed_without_network_bytes()
    {
        var missing = Npc(1504, 17, 8, 3, 4, "00") with { WireIdentity = null };
        var unknown = Npc(9999, 17, 8, 3, 4, new string('0', 64));

        var missingResult = new NpcSerializer().Serialize(missing);
        var unknownResult = new NpcSerializer().Serialize(unknown);
        var wrongOpaqueHashResult = new NpcSerializer().Serialize(Npc(
            1504,
            17,
            8,
            3,
            4,
            "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B") with
        {
            WireIdentity = Npc(1504, 17, 8, 3, 4, "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B")
                .WireIdentity! with
            { OpaqueTemplateSha256 = new string('A', 64) }
        });

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, missingResult.Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, unknownResult.Status);
        Assert.Empty(missingResult.Frame);
        Assert.Empty(unknownResult.Frame);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, wrongOpaqueHashResult.Status);
        Assert.Empty(wrongOpaqueHashResult.Frame);
    }

    [Theory]
    [InlineData(1457U, 0, 20, 116, 49, 0, "000000CF010000")]
    [InlineData(2468U, 2, 142, 32, 18, 2, "EA680FC2034A01")]
    public void Derived_family_spawn_profiles_serialize_without_per_handle_code(
        uint handle,
        byte resourceType,
        byte resourceOrdinal,
        int x,
        int y,
        byte positionMode,
        string opaqueHex)
    {
        var state = DerivedNpc(handle, resourceType, resourceOrdinal, x, y, positionMode, opaqueHex);

        var result = new NpcSerializer().Serialize(state);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(result.Frame);

        Assert.Equal(OfficialSerializerStatus.Ready, result.Status);
        Assert.Equal(OfficialNpcReplicationWireCodec.SpawnOpcode, decoded[2]);
        Assert.Equal(handle, BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(3, 4)));
        Assert.Equal(resourceOrdinal + 1, decoded[7]);
        Assert.Equal((3 << 5) | resourceType, decoded[8]);
        Assert.Equal((4 << 5) | 1, decoded[11]);
        Assert.Equal(opaqueHex, Convert.ToHexString(decoded.AsSpan(12, 7)));
        Assert.Equal(((uint)y << 17) | ((uint)x << 2) | positionMode,
            BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(19, 4)));
    }

    [Fact]
    public void Derived_family_profiles_fail_closed_for_tampered_hash_unknown_family_and_lifecycle_frames()
    {
        var valid = DerivedNpc(1457, 0, 20, 116, 49, 0, "000000CF010000");
        var tampered = valid with
        {
            WireIdentity = valid.WireIdentity! with { SpawnMessageSha256 = new string('A', 64) }
        };
        var unsupported = DerivedNpc(2468, 3, 20, 32, 18, 0, "000000CF010000");

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, new NpcSerializer().Serialize(tampered).Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, new NpcSerializer().Serialize(unsupported).Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, new NpcPositionUpdateSerializer().Serialize(valid).Status);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, new NpcDespawnSerializer().Serialize(valid).Status);
    }

    [Fact]
    public void MariaDb_authoritative_bootstrap_removes_legacy_entities_and_unowned_wuji_placeholder()
    {
        var character = new CharacterSummary(
            0x035F7FF4,
            1,
            "RoadHero",
            "Class1",
            "Gender1",
            "LifeSkill1",
            1,
            "Default",
            19,
            28,
            34,
            "Active",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var legacy = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var authoritative = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character);

        Assert.Equal(legacy.Length - 84 - OfficialImmortalReplicationWireCodec.FrozenLoginApplicationRecordLength, authoritative.Length);
        Assert.Equal(legacy.AsSpan(0, 454).ToArray(), authoritative.AsSpan(0, 454).ToArray());
        var decodedStatic = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(authoritative.AsSpan(454, 639));
        Assert.Equal(639, BinaryPrimitives.ReadUInt16LittleEndian(decodedStatic));
        Assert.Equal(decodedStatic[^1], OfficialLoginWireTransform.ComputeChecksum(decodedStatic));
        Assert.Equal(legacy.AsSpan(1206).ToArray(), authoritative.AsSpan(1093).ToArray());
    }

    [Fact]
    public void MariaDb_authoritative_bootstrap_projects_owned_wuji_inside_the_frozen_application_record()
    {
        var character = new CharacterSummary(
            9, 11, "fresh13a", "Swordsman", "Female", "LifeSkill1", 1, "Appearance1",
            7, 65, 64, "Active", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            CurrentHitPoints: 333, CurrentMagicPoints: 77,
            MaximumHitPoints: 444, MaximumMagicPoints: 88,
            RemainingStatPoints: 4, Constitution: 33, Strength: 44,
            Intelligence: 22, Speed: 25);
        var wuji = new OfficialOwnedImmortalWireState(
            1180000009, OfficialImmortalReplicationWireCodec.VerifiedLoginWujiResourceId, 1,
            400, 400, 156, 156,
            30, 20, 10, 5, 0, 10, 0, 0, 0, true);

        var bootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
            character, clientAreaId: 15, [], [wuji]);
        var decodedStatic = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(454, 668));
        var expected = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord([wuji]);

        Assert.True(expected.Succeeded, expected.Error?.Message);
        Assert.Equal(668, BinaryPrimitives.ReadUInt16LittleEndian(decodedStatic));
        Assert.Equal(
            "2B4D010000900100004D0000009C0000008C1F8309",
            Convert.ToHexString(decodedStatic.AsSpan(2, 21)));
        Assert.Equal(
            "244D0100004D000000BC01000058000000",
            Convert.ToHexString(decodedStatic.AsSpan(84, 17)));
        Assert.Equal(
            "220100040021002C00160019008C1F0000BC01000058000000",
            Convert.ToHexString(decodedStatic.AsSpan(118, 25)));
        Assert.Equal(expected.Value, decodedStatic.AsSpan(143, 29).ToArray());
        Assert.Equal("39900100009C000000900100009C000000", Convert.ToHexString(decodedStatic.AsSpan(101, 17)));
        Assert.Equal("3200", Convert.ToHexString(decodedStatic.AsSpan(172, 2)));
        Assert.Equal("3300", Convert.ToHexString(decodedStatic.AsSpan(174, 2)));
        Assert.Equal("39900100009C000000900100009C000000", Convert.ToHexString(decodedStatic.AsSpan(176, 17)));
        Assert.Equal(decodedStatic[^1], OfficialLoginWireTransform.ComputeChecksum(decodedStatic));
    }

    [Fact]
    public async Task Ordered_runtime_spawn_queue_emits_once_and_removes_sent_event()
    {
        var runtime = new MapRuntime(new MapDefinition(
            19,
            19,
            "Map 19",
            "M2",
            new MapBounds(0, 0, 100, 100),
            [],
            [],
            [],
            [],
            [],
            []));
        var npc = new NpcObject(
            RuntimeObjectIds.Entity(500, RuntimeObjectKind.Npc, 19, 1075128734, "M3 test"),
            Npc(
                1504,
                17,
                8,
                3,
                4,
                "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"));
        runtime.Objects.Add(npc);
        runtime.Replication.SpawnQueue.Enqueue(new RuntimeReplicationEvent(
            1,
            ReplicationQueueKind.Spawn,
            npc.Identity.RuntimeObjectId,
            RuntimeObjectKind.Npc,
            19,
            OfficialSerializerStatus.Ready,
            "Inside AOI",
            DateTimeOffset.UnixEpoch,
            "session-m3"));
        var sender = new RecordingRuntimeFrameSender();
        var emitter = new RuntimeReplicationEmitter();

        var first = await emitter.FlushSpawnQueueAsync(runtime, "session-m3", sender, CancellationToken.None);
        var second = await emitter.FlushSpawnQueueAsync(runtime, "session-m3", sender, CancellationToken.None);

        Assert.Equal(1, first.SentFrames);
        Assert.Equal(0, first.BlockedFrames);
        Assert.Equal(0, second.SentFrames);
        Assert.Single(sender.Frames);
        Assert.Empty(runtime.Replication.SpawnQueue.Snapshot);
    }

    [Fact]
    public void Authoritative_bootstrap_embeds_verified_initial_npcs_inside_static_world_frame()
    {
        var character = new CharacterSummary(
            1, 8, "G2A", "Swordsman", "Male", "None", 1, "Default",
            19, 28, 34, "Active", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var npcs = new[]
        {
            Npc(1504, 17, 8, 3, 4, "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"),
            Npc(4638, 14, 14, 2, 5, "7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644")
        };

        var bootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character, npcs);
        var decodedStatic = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(bootstrap.AsSpan(454, 681));

        Assert.Equal(681, BinaryPrimitives.ReadUInt16LittleEndian(decodedStatic));
        Assert.Equal(1504u, BinaryPrimitives.ReadUInt32LittleEndian(decodedStatic.AsSpan(639, 4)));
        Assert.Equal(4638u, BinaryPrimitives.ReadUInt32LittleEndian(decodedStatic.AsSpan(660, 4)));
        Assert.Equal(decodedStatic[^1], OfficialLoginWireTransform.ComputeChecksum(decodedStatic));
    }

    [Fact]
    public void Authoritative_bootstrap_projects_map_seven_npcs_after_map_restoration_frames()
    {
        var character = new CharacterSummary(
            9, 11, "fresh13a", "Swordsman", "Female", "LifeSkill1", 2, "Appearance1",
            7, 238, 54, "Active", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var merchant = DerivedNpc(3954, 0, 81, 240, 54, 0, "000000CF010000") with
        {
            MapId = 170015007
        };

        var withoutNpc = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
            character, clientAreaId: 15, []);
        var withNpc = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
            character, clientAreaId: 15, [merchant]);
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            withNpc.AsSpan(withoutNpc.Length - 18, 12));
        var prelude = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            withNpc.AsSpan(withoutNpc.Length - 6, 6));
        var spawn = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(withNpc.AsSpan(withoutNpc.Length, 24));

        Assert.Equal(withoutNpc.Length + 24, withNpc.Length);
        Assert.Equal(OfficialPortalWireCodec.MapTransitionOpcode, transition[2]);
        Assert.Equal(OfficialPortalWireCodec.PreludeOpcode, prelude[2]);
        Assert.Equal(OfficialNpcReplicationWireCodec.SpawnOpcode, spawn[2]);
        Assert.Equal(3954u, BinaryPrimitives.ReadUInt32LittleEndian(spawn.AsSpan(3, 4)));
    }

    [Fact]
    public async Task Mixed_ready_and_blocked_replication_batch_emits_ready_family_and_quarantines_blocked_entry()
    {
        var runtime = new MapRuntime(new MapDefinition(
            19,
            19,
            "Map 19",
            "M3",
            new MapBounds(0, 0, 100, 100),
            [],
            [],
            [],
            [],
            [],
            []));
        var ready = new NpcObject(
            RuntimeObjectIds.Entity(501, RuntimeObjectKind.Npc, 19, 1075128734, "M3 ready"),
            Npc(1504, 17, 8, 3, 4, "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"));
        var blocked = new NpcObject(
            RuntimeObjectIds.Entity(502, RuntimeObjectKind.Npc, 19, 99, "M3 blocked"),
            Npc(1504, 17, 8, 3, 4, "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B") with
            {
                NpcTemplateId = 99,
                WireIdentity = null
            });
        runtime.Objects.Add(ready);
        runtime.Objects.Add(blocked);
        runtime.Replication.SpawnQueue.Enqueue(new RuntimeReplicationEvent(
            1, ReplicationQueueKind.Spawn, ready.Identity.RuntimeObjectId, RuntimeObjectKind.Npc, 19,
            OfficialSerializerStatus.Ready, "ready", DateTimeOffset.UnixEpoch, "session-m3"));
        runtime.Replication.SpawnQueue.Enqueue(new RuntimeReplicationEvent(
            2, ReplicationQueueKind.Spawn, blocked.Identity.RuntimeObjectId, RuntimeObjectKind.Npc, 19,
            OfficialSerializerStatus.Ready, "blocked", DateTimeOffset.UnixEpoch, "session-m3"));
        var sender = new RecordingRuntimeFrameSender();

        var result = await new RuntimeReplicationEmitter().FlushSpawnQueueAsync(
            runtime,
            "session-m3",
            sender,
            CancellationToken.None);

        Assert.Equal(1, result.SentFrames);
        Assert.Equal(1, result.BlockedFrames);
        Assert.Single(sender.Frames);
        Assert.Equal(OfficialNpcReplicationWireCodec.SpawnOpcode, OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(sender.Frames[0].Frame)[2]);
        Assert.Equal(0, runtime.Replication.SpawnQueue.Count);
    }

    [Fact]
    public async Task Visible_npc_update_and_despawn_are_session_targeted_and_emitted_in_order()
    {
        var runtime = new MapRuntime(new MapDefinition(
            19,
            19,
            "Map 19",
            "M3",
            new MapBounds(0, 0, 100, 100),
            [],
            [],
            [],
            [],
            [],
            []));
        var npc = new NpcObject(
            RuntimeObjectIds.Entity(500, RuntimeObjectKind.Npc, 19, 1075128734, "M3 lifecycle test"),
            Npc(1504, 17, 8, 3, 4, "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"));
        runtime.Objects.Add(npc);
        var session = new MapSession("session-m3", 19, 1, 700, WorldContentMode.MariaDbAuthoritative);
        runtime.Replication.RecalculateInitialVisibility(
            session,
            new WorldPosition3(28, 34),
            runtime.Objects.ActiveObjects,
            24,
            DateTimeOffset.UnixEpoch);
        var sender = new RecordingRuntimeFrameSender();
        var emitter = new RuntimeReplicationEmitter();
        Assert.Equal(1, (await emitter.FlushSpawnQueueAsync(runtime, session.SessionId, sender, CancellationToken.None)).SentFrames);

        var moved = npc with { State = npc.State with { Position = new WorldPosition3(31, 42) } };
        runtime.Objects.Add(moved);
        runtime.Replication.RecordUpdate(moved, OfficialSerializerStatus.Ready, "NPC moved", DateTimeOffset.UnixEpoch.AddSeconds(1));

        var updateEvent = Assert.Single(runtime.Replication.UpdateQueue.Snapshot);
        Assert.Equal(session.SessionId, updateEvent.TargetSessionId);
        Assert.Equal(0, emitter.PreflightPendingQueues(runtime, session.SessionId).BlockedFrames);
        Assert.Equal(1, (await emitter.FlushAsync(runtime, session.SessionId, sender, CancellationToken.None)).SentFrames);

        runtime.Replication.RecordDespawn(moved, OfficialSerializerStatus.Ready, "NPC left visibility", DateTimeOffset.UnixEpoch.AddSeconds(2));
        var despawnEvent = Assert.Single(runtime.Replication.DespawnQueue.Snapshot);
        Assert.Equal(session.SessionId, despawnEvent.TargetSessionId);
        Assert.Equal(1, (await emitter.FlushAsync(runtime, session.SessionId, sender, CancellationToken.None)).SentFrames);

        Assert.Equal(3, sender.Frames.Count);
        Assert.Equal(OfficialNpcReplicationWireCodec.PositionUpdateOpcode,
            OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(sender.Frames[1].Frame)[2]);
        Assert.Equal(OfficialNpcReplicationWireCodec.DespawnOpcode,
            OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(sender.Frames[2].Frame)[2]);
        Assert.Empty(runtime.Replication.UpdateQueue.Snapshot);
        Assert.Empty(runtime.Replication.DespawnQueue.Snapshot);
    }

    [Fact]
    public void Initial_world_snapshot_includes_only_build_pinned_npcs_outside_deferred_aoi_radius()
    {
        var runtime = new MapRuntime(new MapDefinition(
            19,
            19,
            "Map 19",
            "M2",
            new MapBounds(0, 0, 209, 629),
            [],
            [],
            [],
            [],
            [],
            []));
        var verifiedNpc = new NpcObject(
            RuntimeObjectIds.Entity(501, RuntimeObjectKind.Npc, 19, 1075128734, "M3 test"),
            Npc(
                1504,
                17,
                8,
                3,
                4,
                "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B"));
        var blockedNpc = new NpcObject(
            RuntimeObjectIds.Entity(502, RuntimeObjectKind.Npc, 19, 99, "M3 blocked test"),
            Npc(1504, 17, 8, 3, 4, "00") with { NpcTemplateId = 99, WireIdentity = null });
        runtime.Objects.Add(verifiedNpc);
        runtime.Objects.Add(blockedNpc);
        var session = new MapSession("session-m3", 19, 1, 700, WorldContentMode.MariaDbAuthoritative);

        runtime.Replication.RecalculateInitialVisibility(
            session,
            new WorldPosition3(28, 34),
            runtime.Objects.ActiveObjects,
            24,
            DateTimeOffset.UnixEpoch);

        var queued = Assert.Single(runtime.Replication.SpawnQueue.Snapshot);
        Assert.Equal(verifiedNpc.Identity.RuntimeObjectId, queued.RuntimeObjectId);
        Assert.Equal(OfficialSerializerStatus.Ready, queued.SerializerStatus);
        Assert.Equal("Build-pinned initial world snapshot", queued.Reason);
    }

    private static NpcState Npc(
        uint entityHandle,
        int x,
        int y,
        byte selector,
        byte direction,
        string applicationHash) =>
        new(
            checked((int)entityHandle),
            1075128734,
            "雜貨老闆",
            19,
            new WorldPosition3(x, y),
            WorldDirection.Unknown,
            "data2/rom/npc/npc2643.ROM",
            "Merchant",
            "Always",
            null,
            "Idle",
            24,
            true,
            "{}",
            "mariadb-roadmap-m3-content-v1",
            new OfficialNpcWireIdentity(
                OfficialNpcReplicationWireCodec.ClientBuildId,
                entityHandle,
                2,
                142,
                selector,
                direction,
                1,
                applicationHash,
                "Verified",
                "RoadMap M3 build-pinned evidence",
                OfficialNpcReplicationWireCodec.OpaqueTemplateSha256));

    private static NpcState DerivedNpc(
        uint entityHandle,
        byte resourceType,
        byte resourceOrdinal,
        int x,
        int y,
        byte positionMode,
        string opaqueHex)
    {
        var decoded = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, 24);
        decoded[2] = OfficialNpcReplicationWireCodec.SpawnOpcode;
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(3, 4), entityHandle);
        decoded[7] = checked((byte)(resourceOrdinal + 1));
        decoded[8] = checked((byte)((3 << 5) | resourceType));
        decoded[11] = (4 << 5) | 1;
        Convert.FromHexString(opaqueHex).CopyTo(decoded, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(19, 4),
            ((uint)y << 17) | ((uint)x << 2) | positionMode);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var applicationHash = Convert.ToHexString(SHA256.HashData(decoded.AsSpan(2, 21)));
        var opaqueHash = resourceType == 2
            ? OfficialNpcReplicationWireCodec.OpaqueTemplateSha256
            : OfficialNpcReplicationWireCodec.LiveStageMerchantOpaqueTemplateSha256;

        return Npc(entityHandle, x, y, 3, 4, applicationHash) with
        {
            WireIdentity = new OfficialNpcWireIdentity(
                OfficialNpcReplicationWireCodec.ClientBuildId,
                entityHandle,
                resourceType,
                resourceOrdinal,
                3,
                4,
                1,
                applicationHash,
                "Derived",
                "Official NPCAppearData identity plus family-level repeated packet evidence",
                opaqueHash)
        };
    }

    private static string Sha256(string hex) =>
        Convert.ToHexString(SHA256.HashData(Convert.FromHexString(hex)));
}
