using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Common;
using System.Buffers.Binary;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RuntimeFoundationTests
{
    [Fact]
    public void Runtime_evidence_path_resolver_normalizes_an_explicit_directory()
    {
        var configured = Path.Combine(Path.GetTempPath(), "god2-runtime-tests", "..", "evidence");

        var resolved = RuntimeEvidencePathResolver.Resolve(configured);

        Assert.Equal(Path.GetFullPath(configured), resolved);
    }

    [Fact]
    public void Runtime_evidence_path_default_is_outside_the_published_binary_directory()
    {
        var resolved = RuntimeEvidencePathResolver.Resolve();
        var binaryDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.False(resolved.StartsWith(binaryDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Official_client_coordinate_grid_reconstructs_resource_bounds_and_cells_from_static_consumer_evidence()
    {
        Assert.True(OfficialClientWorldCoordinateGrid.TryCreateBounds(12, 12, out var map3Bounds));
        Assert.Equal(new MapBounds(0, 0, 251, 251), map3Bounds);
        Assert.Equal((9, 6), OfficialClientWorldCoordinateGrid.ToResourceCell(new WorldPosition3(196, 139)));

        Assert.True(OfficialClientWorldCoordinateGrid.TryCreateBounds(10, 30, out var map19Bounds));
        Assert.Equal(new MapBounds(0, 0, 209, 629), map19Bounds);
        Assert.Equal((1, 1), OfficialClientWorldCoordinateGrid.ToResourceCell(new WorldPosition3(28, 34)));

        Assert.False(OfficialClientWorldCoordinateGrid.TryCreateBounds(null, 12, out _));
        Assert.False(OfficialClientWorldCoordinateGrid.TryCreateBounds(1561, 12, out _));
    }

    [Fact]
    public void Official_client_coordinate_grid_divisor_matches_the_recovered_magic_division_for_every_packed_value()
    {
        const int magic = 0x30C30C31;
        for (var value = 0; value <= OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate; value++)
        {
            var product = (long)magic * value;
            var high = (int)(product >> 32);
            var recovered = high >> 2;
            recovered += (int)((uint)recovered >> 31);

            Assert.Equal(value / OfficialClientWorldCoordinateGrid.UnitsPerResourceCell, recovered);
        }
    }

    [Fact]
    public void Client_map_identity_resolver_rejects_coordinates_outside_the_official_15_bit_fields()
    {
        var identity = new ClientMapIdentity(
            10,
            3,
            4,
            "cityi2/cityi2.mdt",
            OfficialPortalWireCodec.ClientBuildId,
            1m,
            1m,
            0m,
            0m,
            MapIdentityEvidenceStatus.Verified,
            MapIdentityEvidenceStatus.Verified,
            true,
            OfficialClientWorldCoordinateGrid.ConsumerEvidence);
        var map = new MapDefinition(
            10,
            null,
            "Map",
            "client:map/island03/cityi2/cityi2",
            new MapBounds(0, 0, 32767, 32767),
            [],
            [],
            [],
            [],
            [],
            [],
            identity);

        var result = new ClientMapIdentityResolver().Resolve(
            map,
            new WorldPosition3(32768, 1),
            OfficialPortalWireCodec.ClientBuildId);

        Assert.False(result.Succeeded);
        Assert.Equal("client_map.coordinate_out_of_range", result.Error.Code);
    }

    public static TheoryData<int> ProtocolAuditConcurrencyRuns
    {
        get
        {
            var data = new TheoryData<int>();
            for (var run = 1; run <= 30; run++)
            {
                data.Add(run);
            }

            return data;
        }
    }

    [Fact]
    public async Task Runtime_cache_publishes_immutable_counts()
    {
        var builder = new ImmutableRuntimeCacheBuilder();
        StaticDataLoadCount[] counts = [new("Map", 1)];

        var result = await builder.BuildAsync(counts, CancellationToken.None);
        counts[0] = new StaticDataLoadCount("Map", 99);

        Assert.True(result.Succeeded);
        Assert.Equal("Map", builder.PublishedCounts[0].DataType);
        Assert.Equal(1, builder.PublishedCounts[0].Count);
        Assert.False(builder.PublishedCounts is StaticDataLoadCount[]);
    }

    [Fact]
    public void Network_host_decodes_ingress_through_protocol_layer()
    {
        var host = new TcpNetworkHost();

        var result = host.DecodeIngressFrame(Convert.FromHexString("05007ACCEC"));

        Assert.True(result.Succeeded);
        Assert.Equal("Heartbeat", result.Value!.Knowledge!.Family);
    }

    [Fact]
    public void Official_login_version_follow_ups_match_verified_stage_specific_client_contracts()
    {
        var initialEncrypted = OfficialClientLoginProtocolFrames.BuildLoginVersionFollowUp();
        var initialDecoded = OfficialClientLoginProtocolFrames.DecodeLoginFrame(initialEncrypted);
        var creationEncrypted = OfficialClientLoginProtocolFrames.BuildCharacterCreationLoginVersionFollowUp();
        var creationDecoded = OfficialClientLoginProtocolFrames.DecodeLoginFrame(creationEncrypted);

        Assert.Equal("0600ed7bef12", OfficialClientLoginProtocolFrames.Hex(initialEncrypted));
        Assert.Equal("060001220055", OfficialClientLoginProtocolFrames.Hex(initialDecoded));
        Assert.Equal(OfficialClientLoginProtocolFrames.InitialLoginVersionWord, BitConverter.ToUInt16(initialDecoded, 3));
        Assert.Equal(OfficialClientLoginProtocolFrames.InitialLoginVersionChecksum, initialDecoded[5]);
        Assert.Equal("0600edadbd60", OfficialClientLoginProtocolFrames.Hex(creationEncrypted));
        Assert.Equal("060001f00023", OfficialClientLoginProtocolFrames.Hex(creationDecoded));
        Assert.Equal(OfficialClientLoginProtocolFrames.CharacterCreationLoginVersionWord, BitConverter.ToUInt16(creationDecoded, 3));
        Assert.Equal(OfficialClientLoginProtocolFrames.CharacterCreationLoginVersionChecksum, creationDecoded[5]);
    }

    [Fact]
    public void Official_world_bootstrap_matches_verified_world_stream_contract()
    {
        var handshake = OfficialClientWorldProtocolFrames.BuildWorldServerHandshake();
        var firstFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();

        Assert.Equal("1300fd9fcac842a869fea1bec83a23dacf5249", OfficialClientLoginProtocolFrames.Hex(handshake));
        Assert.Equal("0600a96db7e8", OfficialClientLoginProtocolFrames.Hex(firstFollowUp));
        Assert.NotEqual(
            OfficialClientLoginProtocolFrames.Hex(OfficialClientLoginProtocolFrames.BuildLoginVersionFollowUp()),
            OfficialClientLoginProtocolFrames.Hex(firstFollowUp));
        Assert.Equal(firstFollowUp, bootstrap.Take(firstFollowUp.Length).ToArray());
        Assert.Equal(1778, bootstrap.Length);
    }

    [Fact]
    public void Official_world_movement_decodes_transport_verified_matrix_packet()
    {
        var frame = Convert.FromHexString("0A0080B8D9C14BA69488");

        Assert.True(OfficialClientWorldProtocolFrames.TryDecodeWorldMovement(frame, out var movement));
        Assert.Equal("0A0080B8D9C14BA69488", movement.EncodedHex);
        Assert.Equal("0A002E12000C0001FF72", movement.DecodedHex);
        Assert.Equal(1, movement.Sequence);
        Assert.Equal(18, movement.XCandidate);
        Assert.Equal(12, movement.YCandidate);
        Assert.Equal(0xFF, movement.StateCandidate);
        Assert.Equal(OfficialClientWorldProtocolFrames.OfficialWorldMovementMode.VisualMountedBaseline, movement.MovementMode);
        Assert.Equal(OfficialClientWorldProtocolFrames.OfficialWorldDirection.Unknown, movement.Direction);
        Assert.Equal(
            frame,
            OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(Convert.FromHexString(movement.DecodedHex)));
    }

    [Fact]
    public void Official_character_create_decodes_current_build_capture()
    {
        var frame = Convert.FromHexString(
            "300097060F2A9A3ECAF7D2BBC53720D7CC4F4F96C863F8287319434EFCCC5C0EE8425406FB4AF87D5F3A3F5042DE9FF9");

        Assert.True(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(frame, out var create));
        Assert.Equal("movet04", create.Name);
        Assert.Equal("Swordsman", create.Class);
        Assert.Equal("Female", create.Gender);
        Assert.Equal("LifeSkill1", create.LifeSkill);
        Assert.Equal("Appearance1", create.Appearance);
        Assert.Equal("02000103010000A000811804000000", create.SelectionBytesHex);
        Assert.Equal(
            "3000176D6F76657430340000000000000000000000000000000000000000000002000103010000A0008118040000001E",
            create.DecodedHex);
    }

    [Theory]
    [InlineData(
        "3000177665726966793132630000000000000000000000000000000000000000020001010100002000011404000000E4",
        "020001010100002000011404000000")]
    [InlineData(
        "30001776657269667931326400000000000000000000000000000000000000000200010401000060002108040000003C",
        "020001040100006000210804000000")]
    [InlineData(
        "30001776657269667931326400000000000000000000000000000000000000000200010601000000006108040000001E",
        "020001060100000000610804000000")]
    [InlineData(
        "3000177665726966793132640000000000000000000000000000000000000000020001030100000000311004000000F3",
        "020001030100000000311004000000")]
    public void Official_character_create_decodes_fresh_external_client_selection_variants(
        string decodedHex,
        string expectedSelectionHex)
    {
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(
            Convert.FromHexString(decodedHex));

        Assert.True(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(encoded, out var create));
        Assert.Equal("Swordsman", create.Class);
        Assert.Equal("Female", create.Gender);
        Assert.Equal("LifeSkill1", create.LifeSkill);
        Assert.Equal("Appearance1", create.Appearance);
        Assert.Equal(expectedSelectionHex, create.SelectionBytesHex);
    }

    [Theory]
    [InlineData(
        "3000033D2894E1398DCC0F58F1DC1A8F9580DAA86A56B04465AD4D3F2746D0AB05A7DD1C9D3DB079F16E19496D581351",
        "020001030100004000211804000000")]
    [InlineData(
        "3000033D2894E1398DCC0F58F1DC1A8F9580DAA86A56B04465AD4D3F2746D0AB05A7DD1C9D3DB039B15E21416D581389",
        "020001030100000000311004000000")]
    public void Official_character_create_decodes_two_stage_login_cipher_transport(
        string transportHex,
        string expectedSelectionHex)
    {
        var frame = Convert.FromHexString(transportHex);

        Assert.True(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(frame, out var create));
        Assert.Equal("verify12d", create.Name);
        Assert.Equal(expectedSelectionHex, create.SelectionBytesHex);
    }

    [Fact]
    public void Official_character_create_rejects_illegal_cosmetic_slot_encoding()
    {
        var decoded = Convert.FromHexString(
            "30001776657269667931326400000000000000000000000000000000000000000200010601000000006108040000001E");
        decoded[39] = 0x01;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);

        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(
            OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(decoded),
            out _));
    }

    [Fact]
    public void Official_character_create_rejects_unknown_profile_and_invalid_checksum()
    {
        var unknownProfile = Convert.FromHexString(
            "3000176D6F76657430340000000000000000000000000000000000000000000003000103010000A0008118040000001F");
        var invalidChecksum = Convert.FromHexString(
            "3000176D6F76657430340000000000000000000000000000000000000000000002000103010000A0008118040000001F");

        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(
            OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(unknownProfile),
            out _));
        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeCharacterCreate(
            OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(invalidChecksum),
            out _));
    }

    [Fact]
    public void Official_character_slot_action_decodes_current_build_capture()
    {
        var frame = Convert.FromHexString("050092B3C2");

        Assert.True(OfficialClientWorldProtocolFrames.TryDecodeCharacterSlotAction(frame, out var slot));
        Assert.Equal(1, slot);
    }

    [Fact]
    public void Official_world_movement_rejects_invalid_checksum_state_and_bounds()
    {
        var invalidChecksum = Convert.FromHexString("0A0080B8D9C14BA69489");
        var invalidState = BuildOfficialWorldMovementFrame(18, 12, 1, 0x00);
        var invalidBounds = BuildOfficialWorldMovementFrame(
            OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate + 1,
            12,
            1);

        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeWorldMovement(invalidChecksum, out _));
        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeWorldMovement(invalidState, out _));
        Assert.False(OfficialClientWorldProtocolFrames.TryDecodeWorldMovement(invalidBounds, out _));
    }

    [Theory]
    [InlineData(0, -12, WorldDirection.North)]
    [InlineData(0, 12, WorldDirection.South)]
    [InlineData(12, 0, WorldDirection.East)]
    [InlineData(-12, 0, WorldDirection.West)]
    [InlineData(12, -12, WorldDirection.NorthEast)]
    [InlineData(-12, -12, WorldDirection.NorthWest)]
    [InlineData(12, 12, WorldDirection.SouthEast)]
    [InlineData(-12, 12, WorldDirection.SouthWest)]
    public void Official_world_movement_resolves_all_eight_directions(
        int deltaX,
        int deltaY,
        WorldDirection expected)
    {
        Assert.Equal(
            expected,
            TcpNetworkHost.ResolveWorldDirection(deltaX, deltaY, WorldDirection.Unknown));
    }

    [Fact]
    public void Official_world_movement_keeps_direction_when_position_does_not_change()
    {
        Assert.Equal(
            WorldDirection.SouthWest,
            TcpNetworkHost.ResolveWorldDirection(0, 0, WorldDirection.SouthWest));
    }

    [Fact]
    public void Official_world_movement_acknowledgement_matches_captured_server_frame()
    {
        var encoded = OfficialClientWorldProtocolFrames.BuildWorldMovementAcknowledgement(0x13);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);

        Assert.Equal("05005D1365", Convert.ToHexString(decoded));
    }

    [Fact]
    public void Official_world_bootstrap_sequence_splits_verified_1778_byte_stream()
    {
        var sequence = OfficialClientWorldProtocolFrames.GetWorldBootstrapSequence();

        Assert.Equal(12, sequence.Count);
        Assert.Equal(1778, sequence.Sum(frame => frame.Length));
        Assert.Equal("WorldFirstFollowUp", sequence[0].Purpose);
        Assert.Equal(6, sequence[0].Length);
        Assert.Equal("PlayerSpawn", sequence[1].Purpose);
        Assert.Equal(128, sequence[1].Length);
        Assert.Equal(128, OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128().Length);
    }

    [Fact]
    public void Official_world_gold_balance_frame_matches_recovered_world_0x27_consumer_contract()
    {
        var encoded = OfficialClientWorldProtocolFrames.BuildGoldBalanceFrame(1000);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);

        Assert.Equal(8, decoded.Length);
        Assert.Equal((byte)0x27, decoded[2]);
        Assert.Equal(1000u, BitConverter.ToUInt32(decoded, 3));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decoded), decoded[^1]);
    }

    [Fact]
    public void Official_world_player_spawn_parses_current_mounted_character()
    {
        var spawn = OfficialClientWorldProtocolFrames.ParseCurrentPlayerSpawn();

        Assert.Equal(0x1F, spawn.Opcode);
        Assert.Equal("kero", spawn.CharacterName);
        Assert.Equal(0x035F7FF4u, spawn.CharacterIdCandidate);
        Assert.Equal(OfficialClientWorldProtocolFrames.OfficialWorldMovementMode.VisualMountedBaseline, spawn.MovementMode);
        Assert.Equal(0x02, spawn.AppearanceModeCandidate);
        Assert.Equal(0x02, spawn.StateFlagsCandidate);
    }

    [Fact]
    public void Official_world_player_spawn_recomputes_checksum_after_authoritative_identity_projection()
    {
        var encoded = OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128(42, "DbHero");
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);

        Assert.Equal((byte)0x1F, decoded[2]);
        Assert.Equal("DbHero", System.Text.Encoding.ASCII.GetString(decoded, 7, 6));
        Assert.Equal(42u, BitConverter.ToUInt32(decoded, 19));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decoded), decoded[^1]);
    }

    [Fact]
    public void Official_world_bootstrap_restores_verified_persisted_map_three_location()
    {
        var character = OfficialNetworkCharacter("Map3Hero") with
        {
            MapId = 3,
            PositionX = 196,
            PositionY = 139
        };

        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var baselineLength = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake().Length;
        var prelude = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength, 6));
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength + 6, 12));
        var decoded = OfficialPortalWireCodec.DecodeResult(prelude, transition);

        Assert.Equal(baselineLength + 18, bootstrap.Length);
        Assert.True(decoded.Succeeded, decoded.FailureCode);
        Assert.Equal(3, decoded.Value!.ClientMapId);
        Assert.Equal(196, decoded.Value.X);
        Assert.Equal(139, decoded.Value.Y);
    }

    [Fact]
    public void Official_world_bootstrap_projects_arbitrary_map_nineteen_position_through_static_consumer_contract()
    {
        var character = OfficialNetworkCharacter("MovedHero") with
        {
            MapId = 19,
            PositionX = 29,
            PositionY = 35
        };

        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var baselineLength = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake().Length;
        var prelude = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength, 6));
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength + 6, 12));
        var packedMap = BitConverter.ToUInt16(transition, 3);
        var packedPosition = BitConverter.ToUInt32(transition, 7);

        Assert.Equal(baselineLength + 18, bootstrap.Length);
        Assert.Equal(OfficialPortalWireCodec.PreludeOpcode, prelude[2]);
        Assert.Equal(OfficialPortalWireCodec.AdditiveChecksum(prelude.AsSpan(0, 5)), prelude[^1]);
        Assert.Equal((ushort)19, (ushort)(packedMap >> 6));
        Assert.Equal((byte)4, (byte)(packedMap & 0x3F));
        Assert.Equal((ushort)29, (ushort)((packedPosition >> 2) & 0x7FFF));
        Assert.Equal((ushort)35, (ushort)(packedPosition >> 17));
        Assert.Equal(OfficialPortalWireCodec.AdditiveChecksum(transition.AsSpan(0, 11)), transition[^1]);
    }

    [Fact]
    public void Official_world_bootstrap_restores_verified_map_seven_area_fifteen_in_capture_order()
    {
        var character = OfficialNetworkCharacter("Map7Hero") with
        {
            MapId = 7,
            PositionX = 238,
            PositionY = 54
        };

        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character, clientAreaId: 15);
        var baselineLength = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake().Length;
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength, 12));
        var prelude = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            bootstrap.AsSpan(baselineLength + 12, 6));
        var packedMap = BitConverter.ToUInt16(transition, 3);
        var packedPosition = BitConverter.ToUInt32(transition, 7);

        Assert.Equal(baselineLength + 18, bootstrap.Length);
        Assert.Equal(OfficialPortalWireCodec.MapTransitionOpcode, transition[2]);
        Assert.Equal(OfficialPortalWireCodec.PreludeOpcode, prelude[2]);
        Assert.Equal((ushort)7, (ushort)(packedMap >> 6));
        Assert.Equal((byte)15, (byte)(packedMap & 0x3F));
        Assert.Equal((ushort)238, (ushort)((packedPosition >> 2) & 0x7FFF));
        Assert.Equal((ushort)54, (ushort)(packedPosition >> 17));
    }

    [Fact]
    public async Task World_runtime_factory_loads_valid_map_and_writes_inspector_snapshot()
    {
        var snapshot = BuildExperimentalWorldContent();
        var factory = new MapRuntimeFactory();

        var result = factory.Create(snapshot, mapId: 1785918668);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(1785918668, result.Value!.MapRuntime.Definition.MapId);
        Assert.Equal("DbBackedExperimental Fenghua town candidate", result.Value.MapRuntime.Definition.Name);

        var inspector = new WorldRuntimeInspector();
        var output = await inspector.WriteAsync(
            result.Value.MapRuntime,
            Path.Combine(Path.GetTempPath(), "God2WorldRuntimeInspectorTests"),
            new DateTimeOffset(2026, 7, 30, 4, 16, 12, TimeSpan.Zero),
            CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Contains("world-runtime-snapshot-v1", File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void World_runtime_factory_creates_npc_entity_from_validated_placement()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.Equal(1, runtime.EntityRegistry.Count(WorldRuntimeEntityType.NPC));
        Assert.Contains(runtime.SpawnQueue.Snapshot, entity => entity.EntityType == WorldRuntimeEntityType.NPC && entity.TemplateId == 1001);
    }

    [Fact]
    public void World_runtime_factory_excludes_unplaced_npc()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.DoesNotContain(runtime.SpawnQueue.Snapshot, entity => entity.EntityType == WorldRuntimeEntityType.NPC && entity.TemplateId == 1002);
    }

    [Fact]
    public void World_runtime_factory_does_not_spawn_monster_template_without_spawn()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(includeMonsterSpawn: false), 1785918668).Value!.MapRuntime;

        Assert.Equal(0, runtime.EntityRegistry.Count(WorldRuntimeEntityType.Monster));
        Assert.DoesNotContain(runtime.SpawnQueue.Snapshot, entity => entity.EntityType == WorldRuntimeEntityType.Monster);
    }

    [Fact]
    public void World_runtime_factory_creates_monster_entity_from_spawn_definition()
    {
        var created = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!;
        var runtime = created.MapRuntime;

        Assert.Equal(1, runtime.EntityRegistry.Count(WorldRuntimeEntityType.Monster));
        Assert.Contains(runtime.SpawnQueue.Snapshot, entity => entity.EntityType == WorldRuntimeEntityType.Monster && entity.TemplateId == 2001);
        Assert.Empty(runtime.MonsterCombat.Snapshot);
        Assert.Contains(created.ValidationErrors, error =>
            error.Contains("combat_content.maximum_hp_invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void World_runtime_factory_creates_portal_runtime_entity()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.Equal(1, runtime.EntityRegistry.Count(WorldRuntimeEntityType.Portal));
        Assert.Contains(runtime.SpawnQueue.Snapshot, entity => entity.EntityType == WorldRuntimeEntityType.Portal && entity.TemplateId == 3001);
    }

    [Fact]
    public void World_runtime_factory_requires_merchant_to_attach_to_npc_placement()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.Single(runtime.Definition.MerchantMappings);
        Assert.Equal(4001, runtime.Definition.MerchantMappings[0].MerchantId);
        Assert.Equal(1001, runtime.Definition.MerchantMappings[0].NpcTemplateId);
    }

    [Fact]
    public void World_runtime_factory_creates_first_class_objects_with_gameplay_state()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.Equal(1, runtime.Objects.Count(RuntimeObjectKind.Map));
        Assert.Equal(1, runtime.Objects.Count(RuntimeObjectKind.Npc));
        Assert.Equal(1, runtime.Objects.Count(RuntimeObjectKind.Monster));
        Assert.Equal(1, runtime.Objects.Count(RuntimeObjectKind.Portal));
        Assert.Equal(1, runtime.Objects.Count(RuntimeObjectKind.Merchant));

        var npc = Assert.Single(runtime.Objects.OfType<NpcObject>());
        var monster = Assert.Single(runtime.Objects.OfType<MonsterObject>());
        var portal = Assert.Single(runtime.Objects.OfType<PortalObject>());
        var merchant = Assert.Single(runtime.Objects.OfType<MerchantObject>());

        Assert.Equal(1001, npc.State.NpcTemplateId);
        Assert.Equal("Idle", npc.State.Lifecycle);
        Assert.Equal(2001, monster.State.MonsterTemplateId);
        Assert.Equal("SpawnPending", monster.State.Lifecycle);
        Assert.Equal(3001, portal.State.PortalId);
        Assert.True(portal.State.IsActive);
        Assert.Equal(4001, merchant.State.MerchantId);
        Assert.Equal("AttachedToNpcPlacement", merchant.State.Lifecycle);
    }

    [Fact]
    public void Npc_content_mapper_preserves_required_raw_and_unknown_fields()
    {
        var mapped = new NpcContentMapper().Map(new NpcDatabaseRecord(
            1001,
            "npc-1001",
            "Town NPC",
            1785918668,
            4848,
            34400,
            WorldDirection.South,
            "UnknownAppearance",
            "UnknownInteraction",
            4001,
            Enabled: true,
            RawMetadata: "{\"raw\":\"kept\"}",
            Source: "test:npcs",
            ContentVersion: "test-content-v1"));

        Assert.Equal(1001, mapped.TemplateId);
        Assert.Equal("Town NPC", mapped.Name);
        Assert.Equal(1785918668, mapped.MapId);
        Assert.Equal(new WorldPosition3(4848, 34400), mapped.Position);
        Assert.Equal(WorldDirection.South, mapped.Direction);
        Assert.Equal("UnknownAppearance", mapped.Appearance);
        Assert.Equal("UnknownInteraction", mapped.InteractionType);
        Assert.Equal(4001, mapped.MerchantBindingId);
        Assert.True(mapped.Enabled);
        Assert.Equal("{\"raw\":\"kept\"}", mapped.RawMetadata);
        Assert.Equal("test-content-v1", mapped.ContentVersion);
    }

    [Fact]
    public void Content_validator_quarantines_duplicate_missing_disabled_and_invalid_binding()
    {
        var valid = new NpcPlacementRecord(1, 1001, 10, new WorldPosition3(1, 1), WorldDirection.Unknown, "Always", 4001, "test", 1.0, "test");
        var duplicate = valid with { Position = new WorldPosition3(2, 2) };
        var missingMap = valid with { PlacementId = 3, NpcTemplateId = 1002, MapId = 99 };
        var disabled = valid with { PlacementId = 4, NpcTemplateId = 1003, Enabled = false };
        var invalidMerchant = valid with { PlacementId = 5, NpcTemplateId = 1004, MerchantId = 9999 };

        var result = new RuntimeContentValidator().ValidateNpcs(
            [valid, duplicate, missingMap, disabled, invalidMerchant],
            new HashSet<int> { 10 },
            new HashSet<int> { 4001 });

        Assert.Empty(result.Valid);
        Assert.Equal(5, result.Quarantined.Count);
        Assert.Contains(result.Issues, issue => issue.Code == "content.duplicate_placement_id");
        Assert.Contains(result.Issues, issue => issue.Code == "content.missing_map_reference");
        Assert.Contains(result.Issues, issue => issue.Code == "content.disabled");
        Assert.Contains(result.Issues, issue => issue.Code == "content.invalid_merchant_binding");
    }

    [Fact]
    public void Content_validator_accepts_multiple_placements_of_the_same_npc_template()
    {
        var first = new NpcPlacementRecord(101, 1001, 10, new WorldPosition3(17, 8), WorldDirection.Unknown, "Always", null, "m2", 1.0, "m2");
        var second = first with { PlacementId = 102, Position = new WorldPosition3(14, 14) };

        var result = new RuntimeContentValidator().ValidateNpcs(
            [first, second],
            new HashSet<int> { 10 },
            new HashSet<int>());

        Assert.Equal(2, result.Valid.Count);
        Assert.Empty(result.Quarantined);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "content.duplicate_placement_id");
    }

    [Fact]
    public void World_session_binder_binds_selected_character_to_map_runtime()
    {
        var session = RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
        {
            AccountId = 1,
            CharacterId = 0x035F7FF4,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var character = BuildCharacter(mapId: 1785918668);

        var binding = new WorldSessionBinder(new MapRuntimeFactory())
            .Bind(session, character, BuildExperimentalWorldContent(), WorldContentMode.MariaDbAuthoritative);

        Assert.True(binding.Succeeded);
        Assert.Equal(1785918668, binding.Value!.MapSession.MapId);
        Assert.Single(binding.Value.MapRuntime.ActiveSessions);
        Assert.Equal(1, binding.Value.MapRuntime.EntityRegistry.Count(WorldRuntimeEntityType.Player));
        Assert.Equal(1, binding.Value.MapRuntime.Objects.Count(RuntimeObjectKind.Player));
    }

    [Fact]
    public async Task World_session_coordinator_binds_repository_content_and_removes_player_on_disconnect()
    {
        var content = BuildVerifiedPortalWorldContent();
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(content));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero");

        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        Assert.True(bound.Succeeded);
        Assert.Equal(WorldContentAuthorityKind.TestOnly, coordinator.AuthorityKind);
        Assert.Equal(WorldContentMode.MariaDbAuthoritative, bound.Value!.MapSession.ContentMode);
        Assert.Equal(character, bound.Value.Character);
        Assert.Equal(1, coordinator.ActiveBindingCount);
        Assert.Single(bound.Value.MapRuntime.ActiveSessions);
        Assert.True(coordinator.Unbind(session.SessionId));
        Assert.Equal(0, coordinator.ActiveBindingCount);
        Assert.Empty(bound.Value.MapRuntime.ActiveSessions);
        Assert.Empty(bound.Value.MapRuntime.Objects.OfType<PlayerObject>());
        Assert.Equal(0, bound.Value.MapRuntime.EntityRegistry.Count(WorldRuntimeEntityType.Player));
    }

    [Fact]
    public async Task World_session_coordinator_rebinds_committed_portal_destination_without_duplicate_player()
    {
        var map19 = BuildVerifiedPortalWorldContent().Maps[19];
        var map3 = map19 with
        {
            MapId = 3,
            SceneId = 3,
            Name = "Verified Portal Map 3",
            RegionId = "official-phase2-map-3",
            SpawnPoints = [new SpawnPoint("PortalArrival", new WorldPosition3(196, 139), WorldDirection.Unknown, "test")],
            ClientIdentity = map19.ClientIdentity! with
            {
                DatabaseMapId = 3,
                ClientMapId = 3,
                ResourceIdentity = "official-phase2-map-3"
            }
        };
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(
            new WorldContentSnapshot(new Dictionary<int, MapDefinition>
            {
                [19] = map19,
                [3] = map3
            }, [])));
        var entering = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero");
        var source = await coordinator.BindAsync(entering, character, CancellationToken.None);
        var inWorld = entering with { ProtocolStage = ProtocolStage.InWorld };
        Assert.True(coordinator.UpdateSession(inWorld).Succeeded);

        var rebound = await coordinator.RebindAsync(
            inWorld,
            character with { MapId = 3, PositionX = 196, PositionY = 139 },
            CancellationToken.None);

        Assert.True(rebound.Succeeded);
        Assert.Equal(3, rebound.Value!.MapSession.MapId);
        Assert.Equal(new WorldPosition3(196, 139), Assert.Single(rebound.Value.MapRuntime.Objects.OfType<PlayerObject>()).State.Position);
        Assert.Empty(source.Value!.MapRuntime.Objects.OfType<PlayerObject>());
        Assert.Empty(source.Value.MapRuntime.ActiveSessions);
        Assert.Single(rebound.Value.MapRuntime.ActiveSessions);
        Assert.Equal(1, coordinator.ActiveBindingCount);
    }

    [Fact]
    public async Task World_session_coordinator_rejects_unverified_bounds_without_creating_player()
    {
        var map = BuildVerifiedPortalWorldContent().Maps[19] with { Bounds = new MapBounds(0, 0, 0, 0) };
        var content = new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, []);
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(content));
        var session = BuildWorldEnteringSession(characterId: 42);

        var result = await coordinator.BindAsync(
            session,
            BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("world_content.map_bounds_unverified", result.Error.Code);
        Assert.Equal(0, coordinator.ActiveBindingCount);
        Assert.Empty(map.NpcPlacements);
    }

    [Fact]
    public async Task Legacy_compatibility_projector_uses_authoritative_runtime_player_identity()
    {
        var coordinator = new WorldSessionCoordinator(
            new InMemoryWorldContentRepository(BuildVerifiedPortalWorldContent()));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero");
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector().Project(bound.Value!);

        Assert.True(projected.Succeeded);
        Assert.Equal(19, projected.Value!.RuntimeMapId);
        Assert.Equal((ushort)19, projected.Value.ClientMapId);
        Assert.Equal((byte)4, projected.Value.ClientAreaId);
        Assert.Equal((ushort)28, projected.Value.ClientX);
        Assert.Equal((ushort)34, projected.Value.ClientY);
        Assert.Equal(42 + 0x7000_0000L, projected.Value.PlayerRuntimeEntityId);
        Assert.Equal(
            OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character),
            projected.Value.Bytes);
    }

    [Fact]
    public async Task MariaDb_authoritative_projector_appends_bound_gold_balance_to_world_bootstrap()
    {
        var coordinator = new WorldSessionCoordinator(
            new InMemoryWorldContentRepository(BuildVerifiedPortalWorldContent()),
            goldProjectionSource: new FixedGoldProjectionSource(1000));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero");
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector(
            excludeLegacyWorldEntities: true).Project(bound.Value!);

        Assert.True(projected.Succeeded, projected.Error.Code);
        Assert.Equal(1000, bound.Value!.InitialGoldBalance);
        var decodedWallet = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            projected.Value!.Bytes.AsSpan(projected.Value.Bytes.Length - 8));
        Assert.Equal((byte)0x27, decodedWallet[2]);
        Assert.Equal(1000u, BitConverter.ToUInt32(decodedWallet, 3));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decodedWallet), decodedWallet[^1]);
    }

    [Fact]
    public async Task MariaDb_authoritative_projector_restores_the_exact_verified_inventory_state()
    {
        var inventory = new PlayerInventorySnapshot(
            Guid.Parse("bd5a433d-17ee-4cbb-b1f7-ff6da286cad4"),
            42,
            32,
            1,
            1,
            "Clean",
            [new InventorySlot(
                0,
                100,
                200,
                OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId,
                OfficialMerchantTransactionWireCodec.LiveStageCanonicalItemId,
                1,
                "Unbound",
                "{}",
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"),
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"),
                1)]);
        var coordinator = new WorldSessionCoordinator(
            new InMemoryWorldContentRepository(BuildVerifiedPortalWorldContent()),
            goldProjectionSource: new FixedGoldProjectionSource(960),
            inventoryProjectionSource: new FixedInventoryProjectionSource(inventory));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero");
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector(
            excludeLegacyWorldEntities: true).Project(bound.Value!);

        Assert.True(projected.Succeeded, projected.Error.Code);
        Assert.Same(inventory, bound.Value!.InitialInventory);
        var decodedRestore = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            projected.Value!.Bytes.AsSpan(
                projected.Value.Bytes.Length - OfficialMerchantTransactionWireCodec.PurchaseResultFrameLength));
        Assert.Equal(OfficialMerchantTransactionWireCodec.PurchaseResultOpcode, decodedRestore[2]);
        Assert.Equal((uint)OfficialMerchantTransactionWireCodec.LiveStageClientItemId, BitConverter.ToUInt32(decodedRestore, 7));
        Assert.Equal(960u, BitConverter.ToUInt32(decodedRestore, 40));
        Assert.Equal(OfficialInventoryBootstrapWireCodec.VerifiedMerchantCatalogIndex, BitConverter.ToUInt16(decodedRestore, 54));
    }

    [Fact]
    public async Task Legacy_compatibility_projector_rejects_internal_map_id_without_client_identity()
    {
        var content = BuildVerifiedPortalWorldContent();
        var map = content.Maps[19] with { ClientIdentity = null };
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(
            new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, [])));
        var session = BuildWorldEnteringSession(characterId: 42);
        var bound = await coordinator.BindAsync(
            session,
            BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero"),
            CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector().Project(bound.Value!);

        Assert.False(projected.Succeeded);
        Assert.Equal("client_map.identity_missing", projected.Error.Code);
    }

    [Fact]
    public async Task Legacy_compatibility_projector_translates_database_map_and_coordinates_through_verified_identity()
    {
        const int databaseMapId = 1785918668;
        var map = new MapDefinition(
            databaseMapId,
            null,
            "Fenghua candidate",
            "r_b720effd89db741cf7bb9830f6a01bee",
            new MapBounds(0, 0, 1000, 1000),
            [],
            [],
            [],
            [],
            [],
            [],
            new ClientMapIdentity(
                databaseMapId,
                19,
                4,
                "r_b720effd89db741cf7bb9830f6a01bee",
                OfficialPortalWireCodec.ClientBuildId,
                0.1m,
                0.1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                "test-only-verified-map-identity"));
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(
            new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [databaseMapId] = map }, [])));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "DbHero") with
        {
            MapId = databaseMapId,
            PositionX = 280,
            PositionY = 340
        };
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector().Project(bound.Value!);

        Assert.True(projected.Succeeded);
        Assert.Equal(databaseMapId, projected.Value!.RuntimeMapId);
        Assert.Equal((ushort)19, projected.Value.ClientMapId);
        Assert.Equal((ushort)28, projected.Value.ClientX);
        Assert.Equal((ushort)34, projected.Value.ClientY);
        Assert.NotEqual(projected.Value.RuntimeMapId, projected.Value.ClientMapId);
    }

    [Fact]
    public async Task Legacy_compatibility_projector_passes_verified_map_seven_area_to_bootstrap_serializer()
    {
        const int databaseMapId = 170015007;
        var map = new MapDefinition(
            databaseMapId,
            null,
            "Observed Map 7 Area 15",
            "live-stage-map7-area15",
            new MapBounds(0, 0, 293, 293),
            [],
            [],
            [],
            [],
            [],
            [],
            new ClientMapIdentity(
                databaseMapId,
                7,
                15,
                "live-stage-map7-area15",
                OfficialPortalWireCodec.ClientBuildId,
                1m,
                1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                "LiveRecovery/Stage3-CGC-S3-portal-map0-area15-to-map7-area15"));
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(
            new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [databaseMapId] = map }, [])));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "Map7Hero") with
        {
            MapId = databaseMapId,
            PositionX = 238,
            PositionY = 54
        };
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector().Project(bound.Value!);

        Assert.True(projected.Succeeded, projected.Error.Code);
        Assert.Equal(databaseMapId, projected.Value!.RuntimeMapId);
        Assert.Equal((ushort)7, projected.Value.ClientMapId);
        Assert.Equal((byte)15, projected.Value.ClientAreaId);
        Assert.Equal((ushort)238, projected.Value.ClientX);
        Assert.Equal((ushort)54, projected.Value.ClientY);
        var baselineLength = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake().Length;
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            projected.Value.Bytes.AsSpan(baselineLength, 12));
        Assert.Equal(OfficialPortalWireCodec.MapTransitionOpcode, transition[2]);
    }

    [Fact]
    public async Task Legacy_compatibility_projector_distinguishes_stage_three_map_zero_area_fifteen_from_area_two()
    {
        const int databaseMapId = 170015000;
        var map = new MapDefinition(
            databaseMapId,
            null,
            "Observed Map 0 Area 15",
            "live-stage-map0-area15",
            new MapBounds(0, 0, 293, 293),
            [],
            [],
            [],
            [],
            [],
            [],
            new ClientMapIdentity(
                databaseMapId,
                0,
                15,
                "live-stage-map0-area15",
                OfficialPortalWireCodec.ClientBuildId,
                1m,
                1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                "LiveRecovery/Stage3-source-world-identity-sequence-14880"));
        var coordinator = new WorldSessionCoordinator(new InMemoryWorldContentRepository(
            new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [databaseMapId] = map }, [])));
        var session = BuildWorldEnteringSession(characterId: 42);
        var character = BuildVerifiedPortalCharacter(characterId: 42, name: "Map0Hero") with
        {
            MapId = databaseMapId,
            PositionX = 248,
            PositionY = 247
        };
        var bound = await coordinator.BindAsync(session, character, CancellationToken.None);

        var projected = new LegacyCompatibilityWorldBootstrapProjector().Project(bound.Value!);

        Assert.True(projected.Succeeded, projected.Error.Code);
        Assert.Equal(databaseMapId, projected.Value!.RuntimeMapId);
        Assert.Equal((ushort)0, projected.Value.ClientMapId);
        Assert.Equal((byte)15, projected.Value.ClientAreaId);
        var baselineLength = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake().Length;
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            projected.Value.Bytes.AsSpan(baselineLength, 12));
        Assert.Equal(
            "0C00610F000F0FE103EE0101",
            Convert.ToHexString(transition));
    }

    [Fact]
    public void World_session_binder_emits_semantic_visibility_events_for_content()
    {
        var binding = new WorldSessionBinder(new MapRuntimeFactory()).Bind(
            RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
            {
                AccountId = 1,
                CharacterId = 0x035F7FF4,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.WorldEntering
            },
            BuildCharacter(mapId: 1785918668),
            BuildExperimentalWorldContent(),
            WorldContentMode.MariaDbAuthoritative);

        Assert.True(binding.Succeeded);
        Assert.True(binding.Value!.MapRuntime.BroadcastEvents.Count >= 4);
        Assert.Contains(binding.Value.MapRuntime.BroadcastEvents.Snapshot, ev => ev.Kind == SemanticBroadcastKind.EntityEnteredVisibility);
    }

    [Fact]
    public void World_session_binder_populates_replication_dirty_visibility_and_broadcast_queues()
    {
        var binding = new WorldSessionBinder(new MapRuntimeFactory()).Bind(
            RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
            {
                AccountId = 1,
                CharacterId = 0x035F7FF4,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.WorldEntering
            },
            BuildCharacter(mapId: 1785918668),
            BuildExperimentalWorldContent(),
            WorldContentMode.MariaDbAuthoritative);

        Assert.True(binding.Succeeded);
        Assert.Equal(2, binding.Value!.MapRuntime.Replication.SpawnQueue.Count);
        Assert.Equal(4, binding.Value.MapRuntime.Replication.DirtyTracking.Count);
        Assert.Equal(3, binding.Value.MapRuntime.Replication.Visibility.Count);
        Assert.DoesNotContain(
            typeof(ReplicationRuntime).GetProperties(),
            property => string.Equals(property.Name, "BroadcastQueue", StringComparison.Ordinal));
        Assert.Equal(
            2,
            binding.Value.MapRuntime.Replication.SpawnQueue.Snapshot.Count(
                entry => entry.SerializerStatus == OfficialSerializerStatus.SerializerBlockedByEvidence));
    }

    [Fact]
    public void Repeated_visibility_snapshot_does_not_duplicate_spawn_for_same_session()
    {
        var binding = BindDbBackedWorldSession("conn-1", "session-a");
        var runtime = binding.MapRuntime;
        var before = runtime.Replication.SpawnQueue.Count;
        var player = runtime.Objects.OfType<PlayerObject>().Single();

        runtime.Replication.RecalculateVisibility(
            binding.MapSession,
            player.State.Position,
            runtime.Objects.ActiveObjects,
            player.State.VisibilityRadius,
            DateTimeOffset.UtcNow);

        Assert.Equal(before, runtime.Replication.SpawnQueue.Count);
        Assert.Equal(1, runtime.Replication.SpawnQueue.Snapshot.Count(entry => entry.ObjectKind == RuntimeObjectKind.Npc));
    }

    [Fact]
    public void Disconnect_cleanup_allows_reconnect_to_receive_single_spawn_again()
    {
        var first = BindDbBackedWorldSession("conn-1", "session-a");
        var runtime = first.MapRuntime;
        var firstNpcSpawns = runtime.Replication.SpawnQueue.Snapshot.Count(entry =>
            entry.ObjectKind == RuntimeObjectKind.Npc && entry.TargetSessionId == first.MapSession.SessionId);

        var removed = runtime.Replication.ClearSession(first.MapSession.SessionId);
        var secondSession = RuntimeSession.Connected("conn-2", "127.0.0.1:1001", DateTimeOffset.UtcNow) with
        {
            AccountId = 1,
            CharacterId = 0x035F7FF4,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var second = new WorldSessionBinder(new MapRuntimeFactory()).Bind(
            secondSession,
            BuildCharacter(mapId: 1785918668),
            BuildExperimentalWorldContent(),
            WorldContentMode.MariaDbAuthoritative).Value!;

        Assert.True(removed > 0);
        Assert.Equal(1, firstNpcSpawns);
        Assert.Equal(1, second.MapRuntime.Replication.SpawnQueue.Snapshot.Count(entry =>
            entry.ObjectKind == RuntimeObjectKind.Npc && entry.TargetSessionId == second.MapSession.SessionId));
    }

    [Fact]
    public void World_session_binder_keeps_missing_official_serializer_from_creating_fake_packets()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;

        Assert.Contains(
            runtime.BroadcastEvents.Snapshot,
            ev => ev.EntityType == WorldRuntimeEntityType.NPC &&
                ev.SerializerStatus == OfficialSerializerStatus.SerializerBlockedByEvidence);
    }

    [Fact]
    public void Evidence_gated_serializers_do_not_mutate_runtime_state_or_emit_fake_bytes()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;
        var npc = Assert.Single(runtime.Objects.OfType<NpcObject>());
        var before = npc.State;

        var serialized = new NpcSerializer().Serialize(npc.State);

        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, serialized.Status);
        Assert.Empty(serialized.Frame);
        Assert.Equal(before, npc.State);
        Assert.Contains("server-to-client NPC", serialized.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serializer_blocked_spawn_flush_produces_zero_network_output()
    {
        var binding = BindDbBackedWorldSession("conn-1", "session-a");
        var sender = new RecordingRuntimeFrameSender();
        var preflight = new RuntimeReplicationEmitter().PreflightSpawnQueue(
            binding.MapRuntime,
            binding.MapSession.SessionId);

        var result = await new RuntimeReplicationEmitter().FlushSpawnQueueAsync(
            binding.MapRuntime,
            binding.MapSession.SessionId,
            sender,
            CancellationToken.None);

        Assert.Equal(2, preflight.BlockedFrames);
        Assert.Contains(preflight.Blocks, block => block.ObjectKind == RuntimeObjectKind.Npc);
        Assert.Contains(preflight.Blocks, block => block.ObjectKind == RuntimeObjectKind.Monster);
        Assert.Equal(0, result.SentFrames);
        Assert.Equal(2, result.BlockedFrames);
        Assert.Empty(sender.Frames);
        Assert.Contains(result.Blocks, block => block.ObjectKind == RuntimeObjectKind.Npc);
    }

    [Fact]
    public void Player_serializer_success_path_matches_golden_player_spawn_bytes()
    {
        var session = RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
        {
            AccountId = 1,
            CharacterId = 0x035F7FF4,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var binding = new WorldSessionBinder(new MapRuntimeFactory())
            .Bind(session, BuildCharacter(mapId: 0), WorldContentSnapshot.Empty, WorldContentMode.FrozenCompatibility);
        var player = Assert.Single(binding.Value!.MapRuntime.Objects.OfType<PlayerObject>());

        var serialized = new PlayerSerializer().Serialize(player.State);

        Assert.Equal(OfficialSerializerStatus.Ready, serialized.Status);
        Assert.Equal(OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128(), serialized.Frame);
    }

    [Fact]
    public void Player_serializer_patches_verified_dynamic_identity_fields()
    {
        var state = new PlayerState(
            42,
            1,
            "DynHero",
            1,
            "Class1",
            "Gender1",
            "Default",
            19,
            new WorldPosition3(28, 34),
            WorldDirection.Unknown,
            "Attached",
            32,
            null);

        var serialized = new PlayerSerializer().Serialize(state);

        Assert.Equal(OfficialSerializerStatus.Ready, serialized.Status);
        Assert.Equal(
            OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128(42, "DynHero"),
            serialized.Frame);
    }

    [Fact]
    public void Existing_create_profile_player_spawn_normalizes_name_padding_and_projects_identity()
    {
        var encoded = OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(42, "DynHero");
        var decoded = OfficialClientWorldProtocolFrames.DecodeCurrentCreateWorldServerPayload(encoded);

        Assert.Equal(128, encoded.Length);
        Assert.Equal(0x1F, decoded[2]);
        Assert.Equal("DynHero", Encoding.ASCII.GetString(decoded, 7, 7));
        Assert.Equal(new byte[5], decoded.AsSpan(14, 5).ToArray());
        Assert.Equal(42u, BitConverter.ToUInt32(decoded, 19));
        Assert.Equal(0x01, decoded[35]);
        Assert.Equal(0x06, decoded[36]);

        var normalizedEvidenceFrame = OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(8, "kero");
        Assert.Equal(
            normalizedEvidenceFrame,
            OfficialClientWorldProtocolFrames.BuildCurrentCreateProfilePlayerSpawnFrame128(8, "kero"));
        var normalizedEvidenceDecoded = OfficialClientWorldProtocolFrames.DecodeCurrentCreateWorldServerPayload(normalizedEvidenceFrame);
        Assert.Equal("kero", Encoding.ASCII.GetString(normalizedEvidenceDecoded, 7, 4));
        Assert.Equal(new byte[8], normalizedEvidenceDecoded.AsSpan(11, 8).ToArray());
        Assert.Equal(8u, BitConverter.ToUInt32(normalizedEvidenceDecoded, 19));
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(normalizedEvidenceDecoded), normalizedEvidenceDecoded[^1]);
        Assert.Equal(
            "1300D0E113FFA0BCDADBBBA6B1DE5FF06F0D9F",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldServerHandshake()));
        Assert.Equal(
            "1300E826BF0CF8A53D5447D2538C3D9CA6D295",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldClientHandshake()));
        Assert.Equal(
            "06001A0F00D9",
            Convert.ToHexString(OfficialClientWorldProtocolFrames.BuildCurrentCreateWorldFirstFollowUp()));
    }

    [Fact]
    public void Active_female_swordsman_uses_clean_create_profile_instead_of_legacy_mounted_profile()
    {
        var character = OfficialNetworkCharacter("fresh13a") with
        {
            Class = "Swordsman",
            Gender = "Female",
            Appearance = "Appearance1"
        };

        var encoded = OfficialClientWorldProtocolFrames.BuildActiveWorldPlayerSpawnFrame128(character);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);

        Assert.Equal(128, encoded.Length);
        Assert.Equal(0x1F, decoded[2]);
        Assert.Equal(0x01, decoded[3]);
        Assert.Equal("fresh13a", Encoding.ASCII.GetString(decoded, 7, 8));
        Assert.Equal(0x00, decoded[15]);
        Assert.Equal(10u, BitConverter.ToUInt32(decoded, 19));
        Assert.Equal(0x01, decoded[35]);
        Assert.Equal(0x06, decoded[36]);
    }

    [Fact]
    public void Active_world_bootstrap_keeps_swordsman_character_list_and_world_appearance_profile_aligned()
    {
        var character = BuildCharacter(mapId: 19) with
        {
            CharacterId = 42,
            Name = "G2A",
            PositionX = 28,
            PositionY = 34
        };

        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(bootstrap.AsSpan(6, 128));

        Assert.Equal(0x1F, decoded[2]);
        Assert.Equal("G2A", Encoding.ASCII.GetString(decoded, 7, 3));
        Assert.Equal(42u, BitConverter.ToUInt32(decoded, 19));
        Assert.Equal(0x02, decoded[35]);
        Assert.Equal(0x02, decoded[36]);
        Assert.Equal(decoded[^1], OfficialLoginWireTransform.ComputeChecksum(decoded));
    }

    [Fact]
    public void Frozen_compatibility_world_session_binding_keeps_golden_path_independent()
    {
        var session = RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
        {
            AccountId = 1,
            CharacterId = 0x035F7FF4,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };

        var binding = new WorldSessionBinder(new MapRuntimeFactory())
            .Bind(session, BuildCharacter(mapId: 0), WorldContentSnapshot.Empty, WorldContentMode.FrozenCompatibility);

        Assert.True(binding.Succeeded);
        Assert.Equal(WorldContentMode.FrozenCompatibility, binding.Value!.MapSession.ContentMode);
        Assert.Equal(1, binding.Value.MapRuntime.EntityRegistry.Count(WorldRuntimeEntityType.Player));
        Assert.Equal(0, binding.Value.MapRuntime.SpawnQueue.Count);
        Assert.Equal(0, binding.Value.MapRuntime.Replication.SpawnQueue.Count);
    }

    [Fact]
    public void Db_backed_experimental_can_reach_world_ready_boundary_without_official_npc_serializer()
    {
        var runtime = new MapRuntimeFactory().Create(BuildExperimentalWorldContent(), 1785918668).Value!.MapRuntime;
        var inspector = new WorldRuntimeInspector().Capture(runtime, DateTimeOffset.UtcNow);

        Assert.Equal(1, inspector.NpcCount);
        Assert.Equal(1, inspector.MonsterCount);
        Assert.Equal(1, inspector.PortalCount);
        Assert.True(inspector.SpawnQueueCount >= 3);
        Assert.True(inspector.SerializerBlockedCount >= 3);
    }

    [Fact]
    public void Runtime_inspector_exposes_live_objects_queues_visibility_and_serializer_status()
    {
        var binding = new WorldSessionBinder(new MapRuntimeFactory()).Bind(
            RuntimeSession.Connected("conn-1", "127.0.0.1:1000", DateTimeOffset.UtcNow) with
            {
                AccountId = 1,
                CharacterId = 0x035F7FF4,
                IsAuthenticated = true,
                ProtocolStage = ProtocolStage.WorldEntering
            },
            BuildCharacter(mapId: 1785918668),
            BuildExperimentalWorldContent(),
            WorldContentMode.MariaDbAuthoritative);

        var inspector = new WorldRuntimeInspector().Capture(binding.Value!.MapRuntime, DateTimeOffset.UtcNow);

        Assert.Single(inspector.Players);
        Assert.Single(inspector.Npcs);
        Assert.Single(inspector.Monsters);
        Assert.Single(inspector.Portals);
        Assert.Single(inspector.Merchants);
        Assert.Equal(2, inspector.ReplicationSpawnQueueCount);
        Assert.Equal(3, inspector.VisibilityRecordCount);
        Assert.Equal("PendingSpawn", inspector.Npcs[0].ReplicationState);
        Assert.Equal(1, inspector.Npcs[0].VisibleToPlayerCount);
        Assert.Equal("ObjectCreated", inspector.Npcs[0].DirtyFlags);
        Assert.Equal(OfficialSerializerStatus.SerializerBlockedByEvidence, inspector.Npcs[0].SerializerStatus);
        Assert.Contains("NPC serializer is blocked", inspector.Npcs[0].SerializerBlockReason, StringComparison.Ordinal);
        Assert.Contains(
            inspector.SerializerStatus,
            status => status.Serializer == RuntimeSerializerKind.Npc.ToString() &&
                status.Status == OfficialSerializerStatus.Ready &&
                status.CurrentStatus == SerializerEvidenceStatus.PartiallyVerified);
    }

    [Fact]
    public void Runtime_inspector_filters_by_type_map_lifecycle_and_evidence_block()
    {
        var binding = BindDbBackedWorldSession("conn-1", "session-a");

        var inspector = new WorldRuntimeInspector().Capture(
            binding.MapRuntime,
            DateTimeOffset.UtcNow,
            new WorldRuntimeInspectorQuery(
                RuntimeObjectKind.Npc,
                1785918668,
                "Idle",
                OfficialSerializerStatus.SerializerBlockedByEvidence,
                EvidenceBlockedOnly: true));

        Assert.Single(inspector.Npcs);
        Assert.Empty(inspector.Monsters);
        Assert.Equal(RuntimeObjectKind.Npc, inspector.Npcs[0].Kind);
        Assert.Equal("Idle", inspector.Npcs[0].LifecycleState);
        Assert.Equal("runtime-content-v1", inspector.Npcs[0].ContentVersion);
    }

    [Fact]
    public void Official_character_bootstrap_builders_preserve_verified_lengths()
    {
        var loginRequest = new byte[208];
        loginRequest[0] = 0xD0;
        loginRequest[1] = 0x00;
        var loginSuccess = OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
            loginRequest,
            IPAddress.Loopback.GetAddressBytes(),
            2592);
        var characterList = OfficialClientLoginProtocolFrames.BuildServerSelectionCharacterListBootstrap();

        Assert.Equal(417, loginSuccess.Length);
        Assert.Equal(78, characterList.Length);
        Assert.Equal("4e00f361", OfficialClientLoginProtocolFrames.Hex(characterList)[..8]);
    }

    [Fact]
    public void Official_world_walking_probe_is_deferred_and_cannot_mutate_golden_bootstrap()
    {
        var spawn = OfficialClientWorldProtocolFrames.ParseCurrentPlayerSpawn();
        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake();

        Assert.Equal(OfficialClientWorldProtocolFrames.OfficialWorldMovementMode.VisualMountedBaseline, spawn.MovementMode);
        Assert.Equal(
            OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp(),
            bootstrap.Take(OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp().Length).ToArray());
        Assert.DoesNotContain(
            "Walking",
            Enum.GetNames<OfficialClientWorldProtocolFrames.OfficialWorldMovementMode>());
        Assert.Contains(
            OfficialClientWorldProtocolFrames.OfficialWorldMovementMode.DeferredUntilOfficialWalkingEvidence.ToString(),
            Enum.GetNames<OfficialClientWorldProtocolFrames.OfficialWorldMovementMode>());
    }

    [Fact]
    public void Official_server_selection_request_decodes_to_character_list_opcode()
    {
        var decoded = OfficialClientLoginProtocolFrames.DecodeLoginFrame(Convert.FromHexString("06009202CE97"));

        Assert.Equal("0600a60001d9", OfficialClientLoginProtocolFrames.Hex(decoded));
        Assert.Equal(6, decoded[0]);
        Assert.Equal(0xA6, decoded[2]);
        Assert.Equal(1, decoded[4]);
    }

    [Fact]
    public async Task Network_host_routes_pending_character_list_followup_connection_to_world_handshake()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("RouteHero");
        var runtime = CreateOfficialNetworkRuntime(character);
        using var output = new SynchronizedStringWriter();
        var host = CreateOfficialNetworkHost(runtime, new TextWriterServerLogger(output, "Information"));
        Assert.True((await host.StartAsync(
            new NetworkOptions("127.0.0.1", port, AdvertisedIp: "10.20.30.40"),
            CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        Assert.Equal(
            "1300405fd0401bb55367d34d90df1d929883dd",
            OfficialClientLoginProtocolFrames.Hex(await ReadExactAsync(loginStream, 19)));
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        Assert.Equal("0600ed7bef12", OfficialClientLoginProtocolFrames.Hex(await ReadExactAsync(loginStream, 6)));

        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        var loginBootstrap = await ReadExactAsync(loginStream, 417);
        var decodedLoginBootstrap = OfficialClientLoginProtocolFrames.DecodeLoginFrame(loginBootstrap);
        foreach (var offset in new[] { 215, 274, 333 })
        {
            Assert.Equal([10, 20, 30, 40], decodedLoginBootstrap.Skip(offset).Take(4));
            Assert.Equal(port, BitConverter.ToUInt16(decodedLoginBootstrap, offset + 4));
        }

        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        Assert.Equal(78, (await ReadExactAsync(loginStream, 78)).Length);

        using (var invalidWorldClient = new TcpClient())
        {
            await invalidWorldClient.ConnectAsync(IPAddress.Loopback, port);
            var invalidWorldStream = invalidWorldClient.GetStream();
            Assert.Equal(
                "1300fd9fcac842a869fea1bec83a23dacf5249",
                OfficialClientLoginProtocolFrames.Hex(await ReadExactAsync(invalidWorldStream, 19)));
            var invalidHandshake = Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962");
            invalidHandshake[^1] ^= 1;
            await invalidWorldStream.WriteAsync(invalidHandshake);
            await AssertWorldSocketClosedAsync(invalidWorldStream);
            await WaitUntilAsync(() => host.ActiveConnectionCount == 1);
        }

        using (var invalidWorldEntryClient = new TcpClient())
        {
            await invalidWorldEntryClient.ConnectAsync(IPAddress.Loopback, port);
            var invalidWorldEntryStream = invalidWorldEntryClient.GetStream();
            await ReadExactAsync(invalidWorldEntryStream, 19);
            await invalidWorldEntryStream.WriteAsync(
                Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
            var firstFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
            Assert.Equal(firstFollowUp, await ReadExactAsync(invalidWorldEntryStream, firstFollowUp.Length));
            await invalidWorldEntryStream.WriteAsync(Convert.FromHexString("060000000000"));
            await AssertWorldSocketClosedAsync(invalidWorldEntryStream);
            await WaitUntilAsync(() => host.ActiveConnectionCount == 1);
        }

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        Assert.Equal(
            "1300fd9fcac842a869fea1bec83a23dacf5249",
            OfficialClientLoginProtocolFrames.Hex(await ReadExactAsync(worldStream, 19)));
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var expectedBootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var worldFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(worldFollowUp, await ReadExactAsync(worldStream, worldFollowUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        Assert.Equal(
            expectedBootstrap[worldFollowUp.Length..],
            await ReadExactAsync(worldStream, expectedBootstrap.Length - worldFollowUp.Length));
        await WaitUntilAsync(() => host.ActiveConnectionCount == 1);

        Assert.Contains("Connection closed (server_initiated_close).", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Connection transport ended.", output.ToString(), StringComparison.Ordinal);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task World_movement_commits_official_click_destination_and_diagonal_direction()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("ProjHero");
        var runtime = CreateOfficialNetworkRuntime(character);
        var store = new InMemoryPortalTransitionStore();
        using var output = new SynchronizedStringWriter();
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            store,
            new WorldSessionCoordinator(new InMemoryWorldContentRepository(BuildVerifiedPortalWorldContent())),
            new ClientCoordinateOverrideProjector(29, 35),
            logger: new TextWriterServerLogger(output, "Information"));
        Assert.True((await host.StartAsync(
            new NetworkOptions("127.0.0.1", port),
            CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var expectedBootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var worldFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(worldFollowUp, await ReadExactAsync(worldStream, worldFollowUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        Assert.Equal(
            expectedBootstrap[worldFollowUp.Length..],
            await ReadExactAsync(worldStream, expectedBootstrap.Length - worldFollowUp.Length));

        await worldStream.WriteAsync(BuildOfficialWorldMovementFrame(16, 14, 1));
        AssertMovementAcknowledgement(await ReadExactAsync(
            worldStream,
            OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementFrameLength), 1);
        await WaitUntilAsync(() => output.ToString().Contains("world_movement_committed", StringComparison.Ordinal));

        await worldStream.WriteAsync(BuildOfficialWorldMovementFrame(18, 16, 3));
        AssertMovementAcknowledgement(await ReadExactAsync(
            worldStream,
            OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementFrameLength), 3);
        await WaitUntilAsync(() => output.ToString().Contains("movementCount=2", StringComparison.Ordinal));

        var persisted = await store.LoadAsync(character.CharacterId, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal(new WorldPosition3(18, 16), persisted.RawPosition);
        Assert.Equal(WorldDirection.SouthEast, persisted.Direction);
        Assert.Contains("direction=NorthWest", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("world_movement_sequence_resynchronized", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("world_movement_step_rejected", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task World_movement_into_observed_map19_exit_sends_transition_and_rebinds_map3()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("PortalHero") with { PositionX = 15, PositionY = 10 };
        var runtime = CreateOfficialNetworkRuntime(character);
        var store = new InMemoryPortalTransitionStore();
        var worldSessions = new WorldSessionCoordinator(
            new InMemoryWorldContentRepository(BuildVerifiedPortalRoundTripWorldContent()));
        using var output = new SynchronizedStringWriter();
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            store,
            worldSessions,
            new ClientCoordinateOverrideProjector(15, 10),
            logger: new TextWriterServerLogger(output, "Information"));
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var followUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(followUp, await ReadExactAsync(worldStream, followUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        var bootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        Assert.Equal(
            bootstrap[followUp.Length..],
            await ReadExactAsync(worldStream, bootstrap.Length - followUp.Length));

        await worldStream.WriteAsync(BuildOfficialWorldMovementFrame(16, 20, 1));
        AssertMovementAcknowledgement(await ReadExactAsync(
            worldStream,
            OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementFrameLength), 1);
        var prelude = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(await ReadExactAsync(worldStream, 6));
        var transition = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(await ReadExactAsync(worldStream, 12));
        var decoded = OfficialPortalWireCodec.DecodeResult(prelude, transition);

        await WaitUntilAsync(() => output.ToString().Contains("official_portal_committed", StringComparison.Ordinal));
        var persisted = await store.LoadAsync(character.CharacterId, CancellationToken.None);
        var worldSession = Assert.Single(
            runtime.SessionAuthority.ActiveSessions,
            candidate => candidate.ProtocolStage == ProtocolStage.InWorld);
        var rebound = worldSessions.GetBinding(worldSession.SessionId);
        await host.StopAsync(CancellationToken.None);

        Assert.True(decoded.Succeeded);
        Assert.Equal((ushort)3, decoded.Value!.ClientMapId);
        Assert.Equal((ushort)196, decoded.Value.X);
        Assert.Equal((ushort)139, decoded.Value.Y);
        Assert.Equal(3, persisted.CurrentMapId);
        Assert.Equal(new WorldPosition3(196, 139), persisted.RawPosition);
        Assert.True(rebound.Succeeded);
        Assert.Equal(3, rebound.Value!.MapSession.MapId);
    }

    [Fact]
    public async Task World_movement_commits_to_persistence_runtime_and_m4_distance_authority_once()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("MoveHero") with { PositionX = 17, PositionY = 12 };
        var runtime = CreateOfficialNetworkRuntime(character);
        var store = new InMemoryPortalTransitionStore();
        var worldSessions = new WorldSessionCoordinator(
            new MariaDbAuthorityTestWorldContentRepository(BuildM3NpcWorldContent()));
        using var output = new SynchronizedStringWriter();
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            store,
            worldSessions,
            new LegacyCompatibilityWorldBootstrapProjector(excludeLegacyWorldEntities: true),
            logger: new TextWriterServerLogger(output, "Information"),
            npcInteractionClosedLoop: OfficialNpcInteractionClosedLoop.CreateForTesting(worldSessions));
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var followUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(followUp, await ReadExactAsync(worldStream, followUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        var bootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character);
        await ReadExactAsync(
            worldStream,
            bootstrap.Length - followUp.Length + 42 + OfficialClientWorldProtocolFrames.BuildGoldBalanceFrame(0).Length);

        var movementFrame = BuildOfficialWorldMovementFrame(17, 11, 1);
        await worldStream.WriteAsync(movementFrame);
        AssertMovementAcknowledgement(await ReadExactAsync(
            worldStream,
            OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementFrameLength), 1);
        await WaitUntilAsync(() => output.ToString().Contains("world_movement_committed", StringComparison.Ordinal));

        var persisted = await store.LoadAsync(character.CharacterId, CancellationToken.None);
        Assert.Equal(new WorldPosition3(17, 11), persisted.RawPosition);
        Assert.Equal(WorldDirection.North, persisted.Direction);
        Assert.Equal(1, persisted.RuntimeVersion);
        Assert.Contains("portalRuntimeSynchronized=True", output.ToString(), StringComparison.Ordinal);

        var worldSession = Assert.Single(
            runtime.SessionAuthority.ActiveSessions,
            candidate => candidate.ProtocolStage == ProtocolStage.InWorld);
        var binding = worldSessions.GetBinding(worldSession.SessionId);
        Assert.True(binding.Succeeded);
        var player = Assert.IsType<PlayerObject>(binding.Value!.MapRuntime.Objects.Get(binding.Value.MapSession.PlayerRuntimeEntityId).Value);
        Assert.Equal(new WorldPosition3(17, 11), player.State.Position);
        Assert.Equal(WorldDirection.North, player.State.Direction);

        var decodedOpen = new byte[OfficialNpcInteractionWireCodec.OpenFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decodedOpen, checked((ushort)decodedOpen.Length));
        decodedOpen[2] = OfficialNpcInteractionWireCodec.OpenOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decodedOpen.AsSpan(3), 1504);
        decodedOpen[^1] = OfficialLoginWireTransform.ComputeChecksum(decodedOpen);
        await worldStream.WriteAsync(OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decodedOpen));
        var dialog = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(
            await ReadExactAsync(worldStream, OfficialNpcInteractionWireCodec.DialogFrameLength));
        Assert.Equal(OfficialNpcInteractionWireCodec.DialogResultOpcode, dialog[2]);

        await worldStream.WriteAsync(movementFrame);
        await WaitUntilAsync(() => output.ToString().Contains("world_movement_sequence_rejected", StringComparison.Ordinal));
        var afterReplay = await store.LoadAsync(character.CharacterId, CancellationToken.None);
        Assert.Equal(1, afterReplay.RuntimeVersion);
        Assert.Equal(new WorldPosition3(17, 11), afterReplay.RawPosition);

        Array.Clear(movementFrame);
        Array.Clear(decodedOpen);
        Array.Clear(dialog);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Production_authority_cutover_sends_runtime_npcs_after_stripped_legacy_bootstrap()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("M3Hero");
        var runtime = CreateOfficialNetworkRuntime(character);
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            new InMemoryPortalTransitionStore(),
            new WorldSessionCoordinator(new InMemoryWorldContentRepository(BuildM3NpcWorldContent())),
            new LegacyCompatibilityWorldBootstrapProjector(excludeLegacyWorldEntities: true));
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var followUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(followUp, await ReadExactAsync(worldStream, followUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());

        var bootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character);
        var response = await ReadExactAsync(
            worldStream,
            bootstrap.Length - followUp.Length + 42 + OfficialClientWorldProtocolFrames.BuildGoldBalanceFrame(0).Length);
        var decodedStatic = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(response.AsSpan(454 - followUp.Length, 681));
        Assert.Equal(681, BinaryPrimitives.ReadUInt16LittleEndian(decodedStatic));
        Assert.Equal("83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B", Convert.ToHexString(SHA256.HashData(decodedStatic.AsSpan(638, 21))));
        Assert.Equal("7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644", Convert.ToHexString(SHA256.HashData(decodedStatic.AsSpan(659, 21))));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Production_network_host_decodes_m4_session_cipher_before_npc_dispatch()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("M4Hero") with { PositionX = 17, PositionY = 11 };
        var runtime = CreateOfficialNetworkRuntime(character);
        var worldSessions = new WorldSessionCoordinator(
            new MariaDbAuthorityTestWorldContentRepository(BuildM3NpcWorldContent()));
        var host = new TcpNetworkHost(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            new InMemoryPortalTransitionStore(),
            worldSessions,
            new LegacyCompatibilityWorldBootstrapProjector(excludeLegacyWorldEntities: true),
            npcInteractionClosedLoop: OfficialNpcInteractionClosedLoop.CreateForTesting(worldSessions));
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        await ReadExactAsync(worldStream, 19);
        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var followUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(followUp, await ReadExactAsync(worldStream, followUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());

        var bootstrap = OfficialClientWorldProtocolFrames.BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character);
        await ReadExactAsync(
            worldStream,
            bootstrap.Length - followUp.Length + 42 + OfficialClientWorldProtocolFrames.BuildGoldBalanceFrame(0).Length);
        var decodedOpen = new byte[OfficialNpcInteractionWireCodec.OpenFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decodedOpen, checked((ushort)decodedOpen.Length));
        decodedOpen[2] = OfficialNpcInteractionWireCodec.OpenOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decodedOpen.AsSpan(3), 1504);
        decodedOpen[^1] = OfficialLoginWireTransform.ComputeChecksum(decodedOpen);
        var encodedOpen = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decodedOpen);
        Assert.NotEqual(OfficialNpcInteractionWireCodec.OpenOpcode, encodedOpen[2]);

        await worldStream.WriteAsync(encodedOpen);
        var encodedDialog = await ReadExactAsync(worldStream, OfficialNpcInteractionWireCodec.DialogFrameLength);
        var decodedDialog = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedDialog);
        Assert.Equal(OfficialNpcInteractionWireCodec.DialogResultOpcode, decodedDialog[2]);
        Assert.Equal(
            OfficialNpcInteractionWireCodec.DialogApplicationSha256,
            Convert.ToHexString(SHA256.HashData(decodedDialog.AsSpan(2, decodedDialog.Length - 3))));

        var decodedClose = new byte[OfficialNpcInteractionWireCodec.CloseFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decodedClose, checked((ushort)decodedClose.Length));
        decodedClose[2] = OfficialNpcInteractionWireCodec.MerchantCloseOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decodedClose.AsSpan(3), 1504);
        decodedClose[^1] = OfficialLoginWireTransform.ComputeChecksum(decodedClose);
        var encodedClose = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(decodedClose);
        await worldStream.WriteAsync(encodedClose);
        await worldStream.WriteAsync(encodedOpen);
        var reopenedDialog = await ReadExactAsync(worldStream, OfficialNpcInteractionWireCodec.DialogFrameLength);
        Assert.Equal(
            OfficialNpcInteractionWireCodec.DialogResultOpcode,
            OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(reopenedDialog)[2]);

        Array.Clear(decodedOpen);
        Array.Clear(encodedOpen);
        Array.Clear(decodedDialog);
        Array.Clear(decodedClose);
        Array.Clear(encodedClose);
        Array.Clear(reopenedDialog);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task World_claim_is_published_before_character_bootstrap_delivery_is_consumed()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("EarlyHero");
        var runtime = CreateOfficialNetworkRuntime(character);
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(
            new NetworkOptions("127.0.0.1", port),
            CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));

        Assert.Single(await ReadExactAsync(loginStream, 1));

        using var worldClient = new TcpClient();
        await worldClient.ConnectAsync(IPAddress.Loopback, port);
        var worldStream = worldClient.GetStream();
        Assert.Equal(
            "1300fd9fcac842a869fea1bec83a23dacf5249",
            OfficialClientLoginProtocolFrames.Hex(await ReadExactAsync(worldStream, 19)));
        Assert.Equal(77, (await ReadExactAsync(loginStream, 77)).Length);

        await worldStream.WriteAsync(Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962"));
        var expectedBootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        var worldFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        Assert.Equal(worldFollowUp, await ReadExactAsync(worldStream, worldFollowUp.Length));
        await worldStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        Assert.Equal(
            expectedBootstrap[worldFollowUp.Length..],
            await ReadExactAsync(worldStream, expectedBootstrap.Length - worldFollowUp.Length));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Concurrent_world_handshake_sockets_from_one_address_fail_closed_before_claim()
    {
        var port = GetFreeTcpPort();
        var character = OfficialNetworkCharacter("RaceHero");
        var runtime = CreateOfficialNetworkRuntime(character);
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(
            new NetworkOptions("127.0.0.1", port),
            CancellationToken.None)).Succeeded);

        using var loginClient = new TcpClient();
        await loginClient.ConnectAsync(IPAddress.Loopback, port);
        var loginStream = loginClient.GetStream();
        await ReadExactAsync(loginStream, 19);
        await loginStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(loginStream, 6);
        await loginStream.WriteAsync(BuildSyntheticLoginRequest208());
        await ReadExactAsync(loginStream, 417);
        await loginStream.WriteAsync(Convert.FromHexString("06009202CE97"));
        await ReadExactAsync(loginStream, 78);

        using var first = new TcpClient();
        using var second = new TcpClient();
        await Task.WhenAll(
            first.ConnectAsync(IPAddress.Loopback, port),
            second.ConnectAsync(IPAddress.Loopback, port));
        var firstStream = first.GetStream();
        var secondStream = second.GetStream();
        await Task.WhenAll(
            ReadExactAsync(firstStream, 19),
            ReadExactAsync(secondStream, 19));
        var handshake = Convert.FromHexString("1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962");
        await Task.WhenAll(
            firstStream.WriteAsync(handshake).AsTask(),
            secondStream.WriteAsync(handshake).AsTask());

        var worldFollowUp = OfficialClientWorldProtocolFrames.BuildWorldFirstFollowUp();
        await Task.WhenAll(
            ReadExactAsync(firstStream, worldFollowUp.Length),
            ReadExactAsync(secondStream, worldFollowUp.Length));
        await Task.WhenAll(
            firstStream.WriteAsync(BuildWorldEntryStateGateFrame208()).AsTask(),
            secondStream.WriteAsync(BuildWorldEntryStateGateFrame208()).AsTask());

        await Task.WhenAll(
            AssertWorldSocketClosedAsync(firstStream),
            AssertWorldSocketClosedAsync(secondStream));
        await WaitUntilAsync(() => host.ActiveConnectionCount == 1);

        using var retry = new TcpClient();
        await retry.ConnectAsync(IPAddress.Loopback, port);
        var retryStream = retry.GetStream();
        await ReadExactAsync(retryStream, 19);
        await retryStream.WriteAsync(handshake);
        var expectedBootstrap = OfficialClientWorldProtocolFrames.BuildWorldBootstrapAfterHandshake(character);
        Assert.Equal(worldFollowUp, await ReadExactAsync(retryStream, worldFollowUp.Length));
        await retryStream.WriteAsync(BuildWorldEntryStateGateFrame208());
        Assert.Equal(
            expectedBootstrap[worldFollowUp.Length..],
            await ReadExactAsync(retryStream, expectedBootstrap.Length - worldFollowUp.Length));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Network_host_returns_official_invalid_credentials_prompt_and_allows_same_connection_retry()
    {
        var port = GetFreeTcpPort();
        var runtime = CreateOfficialNetworkRuntime(OfficialNetworkCharacter());
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await ReadExactAsync(stream, 19);
        await stream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        await ReadExactAsync(stream, 6);
        await stream.WriteAsync(BuildSyntheticLoginRequest208("wrong-password"));

        var expectedFailure = OfficialClientLoginProtocolFrames.BuildLoginFailureResponse(
            OfficialLoginFailureCode.CredentialsRejected);
        var failure = await ReadExactAsync(stream, expectedFailure.Length);
        Assert.Equal(expectedFailure, failure);
        Assert.Equal("05000A77C6", Convert.ToHexString(failure));
        Assert.Equal(
            [0x05, 0x00, 0x1E, 0x03, 0x16],
            OfficialClientLoginProtocolFrames.DecodeLoginFrame(failure));
        Assert.Equal(1, host.ActiveConnectionCount);
        await WaitUntilAsync(() => !runtime.DuplicateLoginGuard.IsOnline(1));
        var account = await runtime.AccountRepository.FindByIdAsync(1, CancellationToken.None);
        Assert.NotNull(account);
        Assert.Equal(1, account.FailedLoginCount);

        var validRequest = BuildSyntheticLoginRequest208();
        await stream.WriteAsync(validRequest);
        var expectedSuccess = OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
            validRequest,
            IPAddress.Loopback.GetAddressBytes(),
            checked((ushort)port));
        Assert.Equal(expectedSuccess, await ReadExactAsync(stream, expectedSuccess.Length));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Network_host_returns_evidence_pinned_character_creation_bootstrap_without_publishing_world_claim()
    {
        var port = GetFreeTcpPort();
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            100,
            new InMemoryAccountRepository([Account(1, "fixture_account", "xxxxxxxxxx")]),
            new InMemoryCharacterRepository());
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var loginClient = await OpenOfficialCharacterCreationReconnectAsync(port, runtime);
        var loginStream = loginClient.GetStream();
        var creationFrame = await ReadExactAsync(
            loginStream,
            OfficialClientLoginProtocolFrames.CharacterCreationBootstrapLength);
        var decodedCreationFrame = OfficialClientLoginProtocolFrames.DecodeLoginFrame(creationFrame);
        Assert.Equal(OfficialClientLoginProtocolFrames.CharacterCreationBootstrapLength, decodedCreationFrame.Length);
        Assert.Equal(OfficialClientLoginProtocolFrames.CharacterCreationBootstrapOpcode, decodedCreationFrame[2]);
        Assert.Equal(
            "80001F02F0CFAA4732410000000000000000000100000000000000000000000000000002020000040000000138E302323134313030323037320000E8CEB00900000000000000000000000001020000030001007CF191772A5B7676000000003F5B767650C7C1D3E8CEB009F0ECA51EE8CEB009240000001FF5E87190FBAF2BA2",
            Convert.ToHexString(decodedCreationFrame));

        using var independentConnection = new TcpClient();
        await independentConnection.ConnectAsync(IPAddress.Loopback, port);
        Assert.Equal(
            "1300405FD0401BB55367D34D90DF1D929883DD",
            Convert.ToHexString(await ReadExactAsync(independentConnection.GetStream(), 19)));

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Network_host_commits_captured_character_create_and_closes_for_list_refresh()
    {
        var port = GetFreeTcpPort();
        var characters = new InMemoryCharacterRepository();
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            100,
            new InMemoryAccountRepository([Account(1, "fixture_account", "xxxxxxxxxx")]),
            characters,
            TestCharacterCreationAuthority());
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = await OpenOfficialCharacterCreationReconnectAsync(port, runtime);
        var stream = client.GetStream();
        await ReadExactAsync(stream, OfficialClientLoginProtocolFrames.CharacterCreationBootstrapLength);

        await stream.WriteAsync(Convert.FromHexString(
            "3000033D2894E1398DCC0F58F1DC1A8F9580DAA86A56B04465AD4D3F2746D0AB05A7DD1C9D3DB039B15E21416D581389"));
        await AssertWorldSocketClosedAsync(stream);

        var created = await characters.ListByAccountAsync(1, CancellationToken.None);
        var character = Assert.Single(created);
        Assert.Equal("verify12d", character.Name);
        Assert.Equal("Swordsman", character.Class);
        Assert.Equal("Female", character.Gender);
        Assert.Equal("LifeSkill1", character.LifeSkill);
        Assert.Equal("Appearance1", character.Appearance);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Network_host_rejects_corrupted_character_create_without_mutation()
    {
        var port = GetFreeTcpPort();
        var characters = new InMemoryCharacterRepository();
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            100,
            new InMemoryAccountRepository([Account(1, "fixture_account", "xxxxxxxxxx")]),
            characters,
            TestCharacterCreationAuthority());
        var host = CreateOfficialNetworkHost(runtime);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = await OpenOfficialCharacterCreationReconnectAsync(port, runtime);
        var stream = client.GetStream();
        await ReadExactAsync(stream, OfficialClientLoginProtocolFrames.CharacterCreationBootstrapLength);
        var corrupted = Convert.FromHexString(
            "300097060F2A9A3ECAF7D2BBC53720D7CC4F4F96C863F8287319434EFCCC5C0EE8425406FB4AF87D5F3A3F5042DE9FF9");
        corrupted[^1] ^= 1;

        await stream.WriteAsync(corrupted);
        await AssertWorldSocketClosedAsync(stream);
        Assert.Empty(await characters.ListByAccountAsync(1, CancellationToken.None));

        await host.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(OfficialLoginFailureCode.AccountNotRegistered, "05001E0013", "05000A7AC0")]
    [InlineData(OfficialLoginFailureCode.DuplicateLogin, "05001E0114", "05000A79C2")]
    [InlineData(OfficialLoginFailureCode.AccountStopped, "05001E0215", "05000A78C4")]
    [InlineData(OfficialLoginFailureCode.CredentialsRejected, "05001E0316", "05000A77C6")]
    [InlineData(OfficialLoginFailureCode.ServerFull, "05001E091C", "05000A71D2")]
    public void Official_login_failure_response_matches_static_client_result_branch(
        OfficialLoginFailureCode failureCode,
        string decodedHex,
        string encodedHex)
    {
        var encoded = OfficialClientLoginProtocolFrames.BuildLoginFailureResponse(failureCode);

        Assert.Equal(encodedHex, Convert.ToHexString(encoded));
        Assert.Equal(decodedHex, Convert.ToHexString(OfficialClientLoginProtocolFrames.DecodeLoginFrame(encoded)));
    }

    [Fact]
    public void Official_login_failure_response_rejects_undefined_client_result()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OfficialClientLoginProtocolFrames.BuildLoginFailureResponse((OfficialLoginFailureCode)0xFF));
    }

    [Fact]
    public void Official_login_success_bootstrap_patches_server_list_endpoints_to_supplied_listener_endpoint()
    {
        var request = BuildSyntheticLoginRequest208();
        var encoded = OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
            request,
            [10, 20, 30, 40],
            3456);
        var decoded = OfficialClientLoginProtocolFrames.DecodeLoginFrame(encoded);

        foreach (var offset in new[] { 215, 274, 333 })
        {
            Assert.Equal([10, 20, 30, 40], decoded.Skip(offset).Take(4).Select(value => (int)value).ToArray());
            Assert.Equal(3456, BitConverter.ToUInt16(decoded, offset + 4));
        }

        Assert.Equal(Encoding.ASCII.GetBytes("fixture_account"), decoded.Skip(3).Take("fixture_account".Length));
        Assert.Equal(Enumerable.Repeat((byte)'x', 10), decoded.Skip(27).Take(10));
    }

    [Fact]
    public async Task Network_host_binds_only_login_port_and_ignores_world_port()
    {
        var loginPort = GetFreeTcpPort();
        var worldPort = GetFreeTcpPort();
        var host = new TcpNetworkHost();

        var started = await host.StartAsync(new NetworkOptions("127.0.0.1", loginPort, worldPort), CancellationToken.None);

        Assert.True(started.Succeeded);
        Assert.Equal(1, host.ListenerCount);
        Assert.Equal(1, host.AcceptLoopCount);
        Assert.Equal([loginPort], host.BoundPorts);
        Assert.Throws<SocketException>(() => BindProbe(loginPort).Dispose());

        using (BindProbe(worldPort))
        {
        }

        await host.StopAsync(CancellationToken.None);

        using (BindProbe(loginPort))
        {
        }
    }

    [Fact]
    public async Task Network_host_supports_clean_start_stop_restart_lifecycle()
    {
        var port = GetFreeTcpPort();
        var host = new TcpNetworkHost();
        var options = new NetworkOptions("127.0.0.1", port, 0);

        Assert.True((await host.StartAsync(options, CancellationToken.None)).Succeeded);
        await host.StopAsync(CancellationToken.None);
        Assert.False(host.IsStarted);
        Assert.Equal(0, host.AcceptLoopCount);

        Assert.True((await host.StartAsync(options, CancellationToken.None)).Succeeded);
        Assert.Equal([port], host.BoundPorts);
        await host.StopAsync(CancellationToken.None);

        using (BindProbe(port))
        {
        }
    }

    [Fact]
    public async Task Network_shutdown_during_partial_receive_waits_and_cleans_connection_state()
    {
        var port = GetFreeTcpPort();
        var sessions = new SessionStore();
        var authority = new InMemorySessionAuthority(sessions);
        await authority.InitializeAsync(CancellationToken.None);
        var host = new TcpNetworkHost(authority);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await WaitUntilAsync(() => host.ActiveConnectionCount == 1);
        await client.GetStream().WriteAsync(Convert.FromHexString("05007A"));
        await WaitUntilAsync(() => host.ProtocolConnectionStateCount == 0);

        await host.StopAsync(CancellationToken.None);

        Assert.Equal(0, host.ActiveConnectionCount);
        Assert.Equal(0, host.ConnectionTaskCount);
        Assert.Equal(0, host.ProtocolConnectionStateCount);
        Assert.Empty(authority.ActiveSessions);
    }

    [Fact]
    public async Task Network_host_accepts_many_clients_without_thread_per_packet_or_task_leaks()
    {
        const int clientCount = 100;
        var port = GetFreeTcpPort();
        var host = new TcpNetworkHost(
            new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official)),
            maximumConnections: clientCount);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        var clients = Enumerable.Range(0, clientCount).Select(_ => new TcpClient()).ToArray();
        await Task.WhenAll(clients.Select(client => client.ConnectAsync(IPAddress.Loopback, port)));
        await WaitUntilAsync(() => host.ActiveConnectionCount == clientCount, timeoutMilliseconds: 10_000);

        foreach (var client in clients)
        {
            client.Dispose();
        }

        await WaitUntilAsync(() => host.ActiveConnectionCount == 0, timeoutMilliseconds: 10_000);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal(0, host.ConnectionTaskCount);
    }

    [Fact]
    public async Task Slow_client_does_not_block_another_client_receive_or_disconnect()
    {
        var port = GetFreeTcpPort();
        var host = new TcpNetworkHost();
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var slowClient = new TcpClient();
        using var activeClient = new TcpClient();
        await slowClient.ConnectAsync(IPAddress.Loopback, port);
        await activeClient.ConnectAsync(IPAddress.Loopback, port);
        await WaitUntilAsync(() => host.ActiveConnectionCount == 2);

        await activeClient.GetStream().WriteAsync(Convert.FromHexString("05007ACCEC"));
        activeClient.Dispose();
        await WaitUntilAsync(() => host.ActiveConnectionCount == 1);

        Assert.Equal(1, host.ActiveConnectionCount);
        await host.StopAsync(CancellationToken.None);
        Assert.Equal(0, host.ActiveConnectionCount);
    }

    [Fact]
    public async Task Sensitive_unknown_packet_bytes_are_not_written_to_general_log()
    {
        var port = GetFreeTcpPort();
        using var output = new SynchronizedStringWriter();
        var factory = new PacketFactory(new PacketDeserializer(ProtocolRegistry.Official));
        var host = new TcpNetworkHost(
            factory,
            new ProtocolConnectionRuntime(factory),
            logger: new TextWriterServerLogger(output, "Information"));
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await client.GetStream().WriteAsync(Convert.FromHexString("0400FFAA"));
        await WaitUntilAsync(() => output.ToString().Contains("Unknown protocol packet recorded as metadata", StringComparison.Ordinal));
        await host.StopAsync(CancellationToken.None);

        Assert.DoesNotContain("0400FFAA", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FFAA", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Network_host_preserves_order_for_coalesced_frames_on_one_connection()
    {
        var port = GetFreeTcpPort();
        var order = new System.Collections.Concurrent.ConcurrentQueue<ushort>();
        var registry = SyntheticRegistry(
            ("first", "Login", "0400A001"),
            ("second", "Login", "0400A002"));
        var packetRegistry = new PacketRegistry(registry);
        packetRegistry.Register(new RecordingPacketHandler(0xA001, order));
        packetRegistry.Register(new RecordingPacketHandler(0xA002, order));
        var factory = new PacketFactory(new PacketDeserializer(registry));
        var runtime = new ProtocolConnectionRuntime(factory, new InMemoryPacketRouter(packetRegistry));
        var host = new TcpNetworkHost(factory, runtime);
        Assert.True((await host.StartAsync(new NetworkOptions("127.0.0.1", port), CancellationToken.None)).Succeeded);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await client.GetStream().WriteAsync(Convert.FromHexString("0400A0010400A002"));
        await WaitUntilAsync(() => order.Count == 2);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal([0xA001, 0xA002], order.ToArray());
    }

    [Fact]
    public void Frame_accumulator_has_bounded_sliding_storage()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 64, maxBufferedBytes: 128);

        for (var index = 0; index < 1_000; index++)
        {
            var result = accumulator.Append(Convert.FromHexString("05007ACCEC"));
            Assert.Single(result.Frames);
        }

        var oversizedBuffer = accumulator.Append(new byte[129]);

        Assert.Equal(0, accumulator.BufferedByteCount);
        Assert.InRange(accumulator.BufferCapacity, 2, 128);
        Assert.Equal(FrameReadStatus.OversizedFrame, oversizedBuffer.Issue!.Status);
        Assert.Equal("frame.buffer_oversized", oversizedBuffer.Issue.Code);
    }

    [Fact]
    public void Frame_accumulator_and_protocol_frame_clear_consumed_sensitive_bytes()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 64, maxBufferedBytes: 128);
        var result = accumulator.Append(Convert.FromHexString("0600A001BEEF"));

        var frame = Assert.Single(result.Frames);
        Assert.All(RuntimePrivate.AccumulatorBuffer(accumulator), value => Assert.Equal(0, value));
        Assert.Contains(frame.Bytes.Span.ToArray(), value => value != 0);

        frame.Clear();

        Assert.All(frame.Bytes.Span.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Frame_accumulator_rejects_mixed_valid_and_invalid_input_without_retaining_frames()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 64, maxBufferedBytes: 128);

        var result = accumulator.Append(Convert.FromHexString("0400A0010100"));

        Assert.Equal(FrameReadStatus.InvalidFrame, result.Issue!.Status);
        Assert.Empty(result.Frames);
        Assert.Equal(0, accumulator.BufferedByteCount);
        Assert.All(RuntimePrivate.AccumulatorBuffer(accumulator), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Frame_accumulator_clear_erases_partial_transport_bytes()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 64, maxBufferedBytes: 128);
        var partial = accumulator.Append(Convert.FromHexString("0600A001"));
        Assert.Equal(FrameReadStatus.NeedMoreData, partial.Issue!.Status);
        Assert.Equal(4, accumulator.BufferedByteCount);

        accumulator.Clear();

        Assert.Equal(0, accumulator.BufferedByteCount);
        Assert.All(RuntimePrivate.AccumulatorBuffer(accumulator), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Frame_accumulator_compacts_partial_tail_and_erases_consumed_prefix()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 64, maxBufferedBytes: 128);

        var result = accumulator.Append(Convert.FromHexString("0400A0010600B0"));

        var frame = Assert.Single(result.Frames);
        Assert.Equal(FrameReadStatus.NeedMoreData, result.Issue!.Status);
        Assert.Equal(3, accumulator.BufferedByteCount);
        var storage = RuntimePrivate.AccumulatorBuffer(accumulator);
        Assert.Equal(Convert.FromHexString("0600B0"), storage[..3]);
        Assert.All(storage[3..], value => Assert.Equal(0, value));
        frame.Clear();
    }

    [Fact]
    public void Frame_accumulator_erases_abandoned_storage_when_growing()
    {
        var accumulator = new FrameAccumulator(maxPacketSize: 512, maxBufferedBytes: 1_024);
        var first = new byte[240];
        first[0] = 0xF4;
        first[1] = 0x01;
        Array.Fill(first, (byte)0xA5, 2, first.Length - 2);
        Assert.Equal(FrameReadStatus.NeedMoreData, accumulator.Append(first).Issue!.Status);
        var abandoned = RuntimePrivate.AccumulatorBuffer(accumulator);

        var second = Enumerable.Repeat((byte)0x5A, 40).ToArray();
        Assert.Equal(FrameReadStatus.NeedMoreData, accumulator.Append(second).Issue!.Status);

        Assert.NotSame(abandoned, RuntimePrivate.AccumulatorBuffer(accumulator));
        Assert.All(abandoned, value => Assert.Equal(0, value));
        accumulator.Clear();
    }

    [Fact]
    public async Task Protocol_runtime_captures_unknown_packets_without_crashing()
    {
        var captures = new InMemoryUnknownPacketCaptureSink();
        var audit = new InMemoryProtocolAuditLog();
        var runtime = new ProtocolConnectionRuntime(unknownPacketCapture: captures, audit: audit);

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.Login, "runtime/protocol-evidence"),
            Convert.FromHexString("0400FFAA"),
            CancellationToken.None);

        Assert.Equal(PacketRouteStatus.CapturedUnknown, result.Status);
        Assert.Single(captures.Captures);
        Assert.Equal("FFAA", captures.Captures[0].Opcode);
        Assert.Equal(string.Empty, captures.Captures[0].RawEvidencePath);
        Assert.DoesNotContain(captures.Captures[0].RawPayloadSha256, captures.Captures[0].RawEvidencePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(audit.Entries, entry => entry.EventName == "Unknown Packet Captured");
    }

    [Fact]
    public async Task Protocol_runtime_does_not_misclassify_server_batch_shape_as_client_movement_evidence()
    {
        var evidence = new InMemoryOfficialLiveMovementEvidenceSink();
        var runtime = new ProtocolConnectionRuntime(officialLiveMovementEvidenceSink: evidence);
        var child = Convert.FromHexString("08007387000000A6");
        var frame = new byte[child.Length + 3];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        child.CopyTo(frame, 2);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);

        await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            frame,
            CancellationToken.None);

        Assert.Empty(evidence.Records);
    }

    [Fact]
    public async Task Protocol_runtime_dispatches_current_build_candidate_to_evidence_only_path()
    {
        var captures = new InMemoryUnknownPacketCaptureSink();
        var audit = new InMemoryProtocolAuditLog();
        var runtime = new ProtocolConnectionRuntime(unknownPacketCapture: captures, audit: audit);
        var frame = new byte[20];
        frame[0] = 20;

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            frame,
            CancellationToken.None);

        Assert.Equal(PacketRouteStatus.CapturedEvidence, result.Status);
        Assert.Equal("battle-command-20-byte-envelope-candidate", result.EvidenceSignatureId);
        Assert.Single(captures.Captures);
        Assert.Contains(audit.Entries, entry =>
            entry.EventName == "Current Build Evidence" &&
            entry.Message.Contains(result.EvidenceSignatureId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Official_protocol_runtime_routes_mission_interaction_28_as_registered()
    {
        var runtime = CreateOfficialNetworkRuntime(OfficialNetworkCharacter()).ProtocolConnectionRuntime;
        var frame = BuildOfficialInventoryActivationTransportFrame(
            clientItemId: 0x1234,
            slotIndex: 0x21,
            quantity: 0x01);
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame);

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            decodedFrame,
            CancellationToken.None);

        Array.Clear(decodedFrame);
        Assert.Equal(PacketRouteStatus.Handled, result.Status);
    }

    [Theory]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.AccountLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.GameLoginDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.RouteOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.RouteDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode, 4)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.AttributeIncrementDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.GameplayDisconnectDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationStepDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CombinationCommitDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.MailRecordActionDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest8FDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest90DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest91DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest94DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PetEggRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.FPetRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartActionB2DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartAddRecordDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.TeamRequestDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterDeleteDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterSelectDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bRequestOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.CharacterCreate1bDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetDecodedFrameLength)]
    public async Task Official_protocol_runtime_routes_compatibility_in_world_requests_as_registered(byte opcode, int decodedLength)
    {
        var runtime = CreateOfficialNetworkRuntime(OfficialNetworkCharacter()).ProtocolConnectionRuntime;
        var frame = BuildOfficialCompatibilityRequestTransportFrame(opcode, decodedLength);
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame);

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            decodedFrame,
            CancellationToken.None);

        Array.Clear(decodedFrame);
        Assert.Equal(PacketRouteStatus.Handled, result.Status);
    }

    [Theory]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PetPkTargetDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest92DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PkCursorTargetDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction24Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction26DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction27DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartySelectionDecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95Opcode, PublicBetaCompatibilityGameplayRequestWireAdapter.PartyRequest95DecodedFrameLength)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.SocialTextEnvelopeOpcode, 4)]
    [InlineData(PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishOpcode, PublicBetaCompatibilityGameplayRequestWireAdapter.VendorCartPublishDecodedFrameLength)]
    public void Runtime_fallback_frames_keep_router_minimum_for_declared_client_frames(byte opcode, int decodedLength)
    {
        var frame = BuildOfficialCompatibilityRequestTransportFrame(opcode, decodedLength);
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldTransportFrame(frame);

        Assert.Equal(Math.Max(decodedLength, 4), BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame));
        Assert.Equal(opcode, decodedFrame[2]);
    }

    [Fact]
    public async Task Protocol_runtime_rejects_packets_outside_current_stage()
    {
        var runtime = new ProtocolConnectionRuntime();

        var result = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.Login, "runtime/protocol-evidence"),
            Convert.FromHexString("0A0080BAD7C34DA69488"),
            CancellationToken.None);

        Assert.Equal(PacketRouteStatus.StageRejected, result.Status);
        Assert.Equal("packet.stage_rejected", result.Error.Code);
    }

    [Fact]
    public async Task Protocol_runtime_stage_gate_matches_unified_login_world_lifecycle()
    {
        var runtime = RuntimeWithSyntheticFamilies(
            ("login", "Login", "0400A001"),
            ("movement", "Movement", "0400A002"),
            ("world-transfer", "WorldTransfer", "0400A003"));

        var loginInWorld = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            Convert.FromHexString("0400A001"),
            CancellationToken.None);
        var movementInLogin = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.Login, "runtime/protocol-evidence"),
            Convert.FromHexString("0400A002"),
            CancellationToken.None);
        var movementInWorld = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.InWorld, "runtime/protocol-evidence"),
            Convert.FromHexString("0400A002"),
            CancellationToken.None);
        var transferAfterSelect = await runtime.DecodeAndRouteAsync(
            new PacketRuntimeContext("conn-1", "session-1", ProtocolStage.CharacterSelected, "runtime/protocol-evidence"),
            Convert.FromHexString("0400A003"),
            CancellationToken.None);

        Assert.Equal(PacketRouteStatus.StageRejected, loginInWorld.Status);
        Assert.Equal(PacketRouteStatus.StageRejected, movementInLogin.Status);
        Assert.NotEqual(PacketRouteStatus.StageRejected, movementInWorld.Status);
        Assert.NotEqual(PacketRouteStatus.StageRejected, transferAfterSelect.Status);
    }

    [Fact]
    public async Task Login_authenticates_account_creates_session_and_returns_character_list()
    {
        var (auth, sessions, characters, _) = CreateAuthenticationRuntime([Account(1, "ray", "secret")]);
        var session = sessions.Create("conn-1", "127.0.0.1:1000");
        await characters.CreateAsync(1, new CharacterCreateRequest("HeroOne", "Unknown", "Unknown", "LifeSkill1", "Unknown", "seed"), 0, 0, 0, 4, CancellationToken.None);

        var result = await auth.LoginAsync(new LoginRequest("ray", "secret", "login-1"), session, new CharacterListQuery(characters), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Session.IsAuthenticated);
        Assert.Equal(ProtocolStage.CharacterList, result.Session.ProtocolStage);
        Assert.Single(result.Characters);
    }

    [Fact]
    public async Task Official_login_character_protocol_runtime_requires_raw_packet_evidence_before_runtime_mutation()
    {
        var (auth, sessions, characters, audit) = CreateAuthenticationRuntime([Account(1, "ray", "secret")]);
        var session = sessions.Create("conn-1", "127.0.0.1:1000");
        var runtime = new OfficialLoginCharacterProtocolRuntime(
            new EvidenceGatedOfficialLoginCharacterProtocolCodec(),
            auth,
            new CharacterListQuery(characters),
            new CharacterRuntimeService(characters, sessions, new ReplayGuard()));
        var packet = new PacketDeserializer(ProtocolRegistry.Official)
            .Read(ReadProtocolEvidence("login-request-208-3399f8d051f7.bin"))
            .Value!;

        var login = await runtime.HandleLoginAsync(session, packet, CancellationToken.None);
        var select = await runtime.HandleCharacterSelectAsync(session, packet, CancellationToken.None);
        var latest = sessions.Get(session.SessionId).Value!;

        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, login.Code);
        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, select.Code);
        Assert.Empty(login.ResponseFrame.ToArray());
        Assert.NotEmpty(login.RecoveryGaps);
        Assert.Empty(audit.Entries);
        Assert.False(latest.IsAuthenticated);
        Assert.Null(latest.AccountId);
        Assert.Null(latest.CharacterId);
        Assert.Equal(ProtocolStage.Login, latest.ProtocolStage);
        Assert.True(runtime.LoginCapabilities.LoginPacketRoleVerified);
        Assert.False(runtime.LoginCapabilities.LoginCredentialFieldsVerified);
        Assert.True(runtime.LoginCapabilities.LoginResponseRoleVerified);
        Assert.False(runtime.LoginCapabilities.LoginRuntimeMutationEnabled);
    }

    [Fact]
    public void Session_store_returns_latest_stage_after_runtime_updates()
    {
        var sessions = new SessionStore();
        var session = sessions.Create("conn-1", "127.0.0.1:1000");
        var authenticated = sessions.Authenticate(session, 1).Value!;
        var selected = sessions.BindCharacter(authenticated, 10).Value!;
        sessions.Transition(selected, ProtocolStage.WorldEntering);
        sessions.Transition(sessions.Get(session.SessionId).Value!, ProtocolStage.InWorld);

        var latest = sessions.Get(session.SessionId);

        Assert.True(latest.Succeeded);
        Assert.Equal(session.SessionId, latest.Value!.SessionId);
        Assert.Equal(session.ConnectionId, latest.Value.ConnectionId);
        Assert.Equal(ProtocolStage.InWorld, latest.Value.ProtocolStage);
        Assert.Equal(10, latest.Value.CharacterId);
    }

    [Fact]
    public void Session_store_rejects_stage_regression()
    {
        var sessions = new SessionStore();
        var session = sessions.Create("conn-1", "127.0.0.1:1000");
        var authenticated = sessions.Authenticate(session, 1).Value!;
        var listed = sessions.Transition(authenticated, ProtocolStage.CharacterList).Value!;
        var selected = sessions.BindCharacter(listed, 10).Value!;

        var regression = sessions.Transition(selected, ProtocolStage.Login);

        Assert.False(regression.Succeeded);
        Assert.Equal("session.stage_transition_invalid", regression.Error.Code);
    }

    [Fact]
    public void Session_store_uses_latest_authoritative_value_when_caller_supplies_stale_snapshot()
    {
        var sessions = new SessionStore();
        var stale = sessions.Create("conn-1", "127.0.0.1:1000");
        var authenticated = sessions.Authenticate(stale, 42).Value!;
        var listed = sessions.Transition(stale, ProtocolStage.CharacterList);
        var relogin = sessions.Authenticate(stale, 99);

        Assert.True(listed.Succeeded);
        Assert.False(relogin.Succeeded);
        var latest = sessions.Get(stale.SessionId).Value!;
        Assert.Equal(42, latest.AccountId);
        Assert.Equal(ProtocolStage.CharacterList, latest.ProtocolStage);
        Assert.True(latest.IsAuthenticated);
        Assert.True(authenticated.LastActivityAtUtc <= latest.LastActivityAtUtc);
    }

    [Fact]
    public async Task Concurrent_close_is_idempotent_and_closed_session_cannot_be_revived()
    {
        var observer = new CountingCloseObserver();
        var sessions = new SessionStore(closeObservers: [observer]);
        var session = sessions.Create("conn-1", "127.0.0.1:1000");

        await Task.WhenAll(
            sessions.CloseAsync(session, "first", CancellationToken.None),
            sessions.CloseAsync(session, "second", CancellationToken.None));
        var authentication = sessions.Authenticate(session, 1);

        Assert.False(authentication.Succeeded);
        Assert.Equal(1, observer.Count);
        Assert.Equal(ProtocolStage.Closed, sessions.Get(session.SessionId).Value!.ProtocolStage);
        Assert.Empty(sessions.ActiveSessions);
    }

    [Fact]
    public async Task Concurrent_bind_character_and_close_cannot_revive_or_regress_session()
    {
        var sessions = new SessionStore();
        var connected = sessions.Create("conn-1", "127.0.0.1:1000");
        var authenticated = sessions.Authenticate(connected, 1).Value!;
        var listed = sessions.Transition(authenticated, ProtocolStage.CharacterList).Value!;

        await Task.WhenAll(
            Task.Run(() => sessions.BindCharacter(listed, 10)),
            sessions.CloseAsync(listed, "disconnect", CancellationToken.None));

        var latest = sessions.Get(connected.SessionId).Value!;
        Assert.Equal(ProtocolStage.Closed, latest.ProtocolStage);
        Assert.True(latest.IsClosing);
        Assert.False(sessions.Transition(listed, ProtocolStage.WorldEntering).Succeeded);
    }

    [Fact]
    public async Task Close_observer_failure_does_not_skip_remaining_cleanup()
    {
        var completed = new CountingCloseObserver();
        var sessions = new SessionStore(closeObservers: [new ThrowingCloseObserver(), completed]);
        var session = sessions.Create("conn-1", "127.0.0.1:1000");

        await sessions.CloseAsync(session, "test", CancellationToken.None);

        Assert.Equal(1, completed.Count);
        Assert.Single(sessions.CleanupFailures);
        Assert.Equal(nameof(ThrowingCloseObserver), sessions.CleanupFailures[0].Observer);
    }

    [Fact]
    public void Replay_guard_is_scoped_bounded_and_expires_entries()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var guard = new ReplayGuard(TimeSpan.FromSeconds(10), maximumEntries: 2, clock);

        Assert.True(guard.TryAccept("session-a", "request-1"));
        Assert.False(guard.TryAccept("session-a", "request-1"));
        Assert.True(guard.TryAccept("session-b", "request-1"));
        Assert.False(guard.TryAccept("session-c", "request-1"));
        Assert.Equal(2, guard.Count);

        clock.UtcNow = clock.UtcNow.AddSeconds(11);

        Assert.True(guard.TryAccept("session-c", "request-1"));
        Assert.InRange(guard.Count, 1, 2);
        guard.RemoveScope("session-c");
        Assert.Equal(0, guard.Count);
    }

    [Fact]
    public async Task Replay_guard_expiry_cannot_remove_a_concurrently_renewed_entry()
    {
        for (var run = 0; run < 50; run++)
        {
            var clock = new FixedClock(DateTimeOffset.UtcNow);
            var guard = new ReplayGuard(TimeSpan.FromSeconds(10), maximumEntries: 1, clock);
            Assert.True(guard.TryAccept("session", "request"));
            clock.UtcNow = clock.UtcNow.AddSeconds(11);
            using var start = new ManualResetEventSlim(false);
            var attempts = Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    return guard.TryAccept("session", "request");
                }))
                .ToArray();

            start.Set();
            var results = await Task.WhenAll(attempts);

            Assert.Single(results, accepted => accepted);
            Assert.Equal(1, guard.Count);
        }
    }

    [Fact]
    public async Task Replay_guard_never_exceeds_capacity_under_concurrent_unique_requests()
    {
        const int maximumEntries = 8;
        var guard = new ReplayGuard(maximumEntries: maximumEntries);
        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 128)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                return guard.TryAccept("session", $"request-{index}");
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(maximumEntries, results.Count(accepted => accepted));
        Assert.Equal(maximumEntries, guard.Count);
    }

    [Fact]
    public void Rate_limiter_normalizes_endpoint_and_removes_stale_keys()
    {
        var now = DateTimeOffset.UtcNow;
        var limiter = new LoginAttemptRateLimiter(limit: 1, window: TimeSpan.FromSeconds(10), maximumKeys: 2);

        Assert.True(limiter.TryConsume("127.0.0.1:1000", "Ray", now));
        Assert.False(limiter.TryConsume("127.0.0.1:2000", "ray", now));
        Assert.True(limiter.TryConsume("127.0.0.2:1000", "Ray", now));
        Assert.False(limiter.TryConsume("127.0.0.3:1000", "Ray", now));

        Assert.True(limiter.TryConsume("127.0.0.3:1000", "Ray", now.AddSeconds(11)));
        Assert.InRange(limiter.KeyCount, 1, 2);
    }

    [Fact]
    public void Password_verifier_rejects_unbounded_or_malformed_work_factors()
    {
        var verifier = new PasswordVerifier();
        var salt = Convert.ToBase64String(new byte[16]);
        var hash = Convert.ToBase64String(new byte[32]);

        Assert.False(verifier.Verify("secret", $"pbkdf2-sha256$2147483647${salt}${hash}"));
        Assert.False(verifier.Verify("secret", $"pbkdf2-sha256$100000$AA==${hash}"));
        Assert.False(verifier.Verify("secret", $"pbkdf2-sha256$100000${salt}$AA=="));
        Assert.False(verifier.Verify("secret", new string('x', 257)));
        Assert.True(verifier.Verify("secret", PasswordVerifier.Hash("secret")));
    }

    [Fact]
    public void Production_login_decoder_supports_erasable_password_storage()
    {
        var frame = BuildSyntheticLoginRequest208("erase-me");

        Assert.True(OfficialClientLoginProtocolFrames.TryDecodeSensitiveLoginRequest(
            frame.Span,
            out var username,
            out var password));
        Assert.Equal("fixture_account", username);
        Assert.Equal("erase-me", password);
        Assert.True(new PasswordVerifier().Verify(password, PasswordVerifier.Hash("erase-me")));

        Array.Clear(password);
        Assert.All(password, value => Assert.Equal('\0', value));
    }

    [Theory]
    [MemberData(nameof(ProtocolAuditConcurrencyRuns))]
    public void In_memory_protocol_audits_are_concurrent_bounded_snapshots(int run)
    {
        var captures = new InMemoryUnknownPacketCaptureSink(maximumEntries: 32);
        var audit = new InMemoryProtocolAuditLog(maximumEntries: 32);

        Parallel.For(0, 1_000, index =>
        {
            captures.Capture(new UnknownPacketCapture(
                DateTimeOffset.UtcNow,
                $"connection-{run}-{index}",
                $"session-{run}-{index}",
                "Unknown",
                1,
                "hash",
                "sensitive",
                ProtocolStage.Login));
            audit.Write(new ProtocolAuditEntry(
                DateTimeOffset.UtcNow,
                $"connection-{run}-{index}",
                $"session-{run}-{index}",
                "Unknown",
                string.Empty,
                1,
                ProtocolStage.Login,
                "captured"));
        });

        Assert.Equal(32, captures.Captures.Count);
        Assert.Equal(32, audit.Entries.Count);
    }

    [Fact]
    public void Sequence_validation_is_isolated_per_connection()
    {
        var runtime = new ProtocolConnectionRuntime();
        var packet = new PacketEnvelope(
            new PacketHeader(new Opcode(1), 2, 10, "0001"),
            ReadOnlyMemory<byte>.Empty,
            ProtocolConfidence.Verified);

        var connectionA = new PacketRuntimeContext("conn-a", "session-a", ProtocolStage.Login, "runtime/protocol-evidence");
        var connectionB = new PacketRuntimeContext("conn-b", "session-b", ProtocolStage.Login, "runtime/protocol-evidence");

        Assert.True(runtime.ValidateSequence(connectionA, packet).Succeeded);
        Assert.True(runtime.ValidateSequence(connectionB, packet).Succeeded);
        Assert.False(runtime.ValidateSequence(connectionA, packet).Succeeded);
    }

    [Fact]
    public async Task Login_rejects_invalid_missing_disabled_locked_duplicate_and_replay_cases()
    {
        var now = DateTimeOffset.UtcNow;
        var (auth, sessions, characters, audit) = CreateAuthenticationRuntime(
            [
                Account(1, "ray", "secret"),
                Account(2, "disabled", "secret", AccountStatus.Disabled),
                Account(3, "locked", "secret", AccountStatus.Active, now.AddMinutes(5))
            ]);

        var invalid = await auth.LoginAsync(new LoginRequest("ray", "bad", "bad-password"), sessions.Create("conn-1", "127.0.0.1:1001"), new CharacterListQuery(characters), CancellationToken.None);
        var missing = await auth.LoginAsync(new LoginRequest("missing", "secret", "missing"), sessions.Create("conn-2", "127.0.0.1:1002"), new CharacterListQuery(characters), CancellationToken.None);
        var disabled = await auth.LoginAsync(new LoginRequest("disabled", "secret", "disabled"), sessions.Create("conn-3", "127.0.0.1:1003"), new CharacterListQuery(characters), CancellationToken.None);
        var locked = await auth.LoginAsync(new LoginRequest("locked", "secret", "locked"), sessions.Create("conn-4", "127.0.0.1:1004"), new CharacterListQuery(characters), CancellationToken.None);
        var firstSession = sessions.Create("conn-5", "127.0.0.1:1005");
        var first = await auth.LoginAsync(new LoginRequest("ray", "secret", "first"), firstSession, new CharacterListQuery(characters), CancellationToken.None);
        var duplicate = await auth.LoginAsync(new LoginRequest("ray", "secret", "duplicate"), sessions.Create("conn-6", "127.0.0.1:1006"), new CharacterListQuery(characters), CancellationToken.None);
        var replay = await auth.LoginAsync(new LoginRequest("ray", "secret", "first"), firstSession, new CharacterListQuery(characters), CancellationToken.None);

        Assert.Equal(LoginResultCode.InvalidCredentials, invalid.Code);
        Assert.Equal(LoginResultCode.AccountNotFound, missing.Code);
        Assert.Equal(LoginResultCode.AccountDisabled, disabled.Code);
        Assert.Equal(LoginResultCode.AccountLocked, locked.Code);
        Assert.Equal(LoginResultCode.Success, first.Code);
        Assert.Equal(LoginResultCode.AlreadyOnline, duplicate.Code);
        Assert.Equal(LoginResultCode.ProtocolError, replay.Code);
        Assert.Equal(7, audit.Entries.Count);
    }

    [Fact]
    public async Task Character_runtime_enforces_validation_limit_ownership_and_selection_stage()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(
            characters,
            sessions,
            new ReplayGuard(),
            creationAuthority: TestCharacterCreationAuthority());
        var session = sessions.Authenticate(sessions.Create("conn-1", "127.0.0.1:1000"), 1).Value!;
        var foreign = await characters.CreateAsync(2, new CharacterCreateRequest("ForeignHero", "Unknown", "Unknown", "LifeSkill1", "Unknown", "seed-foreign"), 0, 0, 0, 4, CancellationToken.None);

        var invalid = await service.CreateAsync(session, new CharacterCreateRequest("no", "Unknown", "Unknown", "LifeSkill1", "Unknown", "invalid"), CancellationToken.None);
        var created = await service.CreateAsync(session, new CharacterCreateRequest("HeroOne", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-1"), CancellationToken.None);
        var duplicate = await service.CreateAsync(session, new CharacterCreateRequest("HeroOne", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-duplicate"), CancellationToken.None);
        var foreignDelete = await service.DeleteAsync(session, new CharacterDeleteRequest(foreign.Value!.CharacterId, "delete-foreign"), CancellationToken.None);
        var renamed = await service.RenameAsync(session, new CharacterRenameRequest(created.Character!.CharacterId, "HeroTwo", "rename-1"), CancellationToken.None);
        var selected = await service.SelectAsync(session, new CharacterSelectRequest(created.Character.CharacterId, "select-1"), CancellationToken.None);
        var createAfterSelect = await service.CreateAsync(selected.Session, new CharacterCreateRequest("TooLate", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-after-select"), CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.InvalidName, invalid.Code);
        Assert.Equal(CharacterOperationResultCode.Success, created.Code);
        Assert.Equal(1, created.Character!.MapId);
        Assert.Equal(10, created.Character.PositionX);
        Assert.Equal(20, created.Character.PositionY);
        Assert.Equal(CharacterOperationResultCode.CharacterLimitReached, duplicate.Code);
        Assert.Equal(CharacterOperationResultCode.OwnershipRejected, foreignDelete.Code);
        Assert.Equal(CharacterOperationResultCode.Success, renamed.Code);
        Assert.Equal("HeroTwo", renamed.Character!.Name);
        Assert.Equal(CharacterOperationResultCode.Success, selected.Code);
        Assert.Equal(ProtocolStage.CharacterSelected, selected.Session.ProtocolStage);
        Assert.Equal(CharacterOperationResultCode.InvalidStage, createAfterSelect.Code);
    }

    [Fact]
    public async Task Character_create_fails_closed_without_an_evidence_backed_spawn_profile()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(characters, sessions, new ReplayGuard());
        var session = sessions.Authenticate(
            sessions.Create("conn-create-blocked", "127.0.0.1:1000"),
            1).Value!;

        var result = await service.CreateAsync(
            session,
            new CharacterCreateRequest(
                "HeroOne",
                "Class1",
                "Gender1",
                "LifeSkill1",
                "Appearance1",
                "create-without-authority"),
            CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.CreationProfileUnavailable, result.Code);
        Assert.Empty(await characters.ListByAccountAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Character_runtime_enforces_current_wire_single_character_limit_and_request_replay()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(
            characters,
            sessions,
            new ReplayGuard(),
            creationAuthority: TestCharacterCreationAuthority());
        var session = sessions.Authenticate(sessions.Create("conn-1", "127.0.0.1:1000"), 1).Value!;

        var created = await service.CreateAsync(session, new CharacterCreateRequest("HeroZero", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-0"), CancellationToken.None);
        var validRetry = await service.CreateAsync(session, new CharacterCreateRequest("HeroZero", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-0"), CancellationToken.None);
        var overLimit = await service.CreateAsync(session, new CharacterCreateRequest("HeroOne", "Unknown", "Unknown", "LifeSkill1", "Unknown", "create-1"), CancellationToken.None);
        var replay = await service.DeleteAsync(session, new CharacterDeleteRequest(1, "create-0"), CancellationToken.None);
        var deleted = await service.DeleteAsync(session, new CharacterDeleteRequest(created.Character!.CharacterId, "delete-0"), CancellationToken.None);
        var deleteRetry = await service.DeleteAsync(session, new CharacterDeleteRequest(created.Character.CharacterId, "delete-0"), CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.Success, created.Code);
        Assert.Equal(CharacterOperationResultCode.Success, validRetry.Code);
        Assert.Equal(created.Character!.CharacterId, validRetry.Character!.CharacterId);
        Assert.Equal(CharacterOperationResultCode.CharacterLimitReached, overLimit.Code);
        Assert.Equal(CharacterOperationResultCode.ReplayConflict, replay.Code);
        Assert.Equal(CharacterOperationResultCode.Success, deleted.Code);
        Assert.Equal("Deleted", deleted.Character!.Status);
        Assert.Equal(CharacterOperationResultCode.Success, deleteRetry.Code);
        Assert.Equal("Deleted", deleteRetry.Character!.Status);
    }

    [Fact]
    public async Task Character_lifecycle_headless_matrix_persists_life_skill_and_deletes_each_case()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(
            characters,
            sessions,
            new ReplayGuard(),
            creationAuthority: TestCharacterCreationAuthority());
        var classes = new[] { "Class1", "Class2", "Class3", "Class4" };
        var genders = new[] { "Gender1", "Gender2" };
        var lifeSkills = new[] { "LifeSkill1", "LifeSkill2", "LifeSkill3", "LifeSkill4" };
        var caseNumber = 0;

        foreach (var characterClass in classes)
        {
            foreach (var gender in genders)
            {
                foreach (var lifeSkill in lifeSkills)
                {
                    caseNumber++;
                    var name = $"G2C{caseNumber:00}{characterClass[^1]}{gender[^1]}{lifeSkill[^1]}";
                    var session = sessions.Authenticate(sessions.Create($"conn-{caseNumber}", "127.0.0.1:1000"), 1).Value!;
                    var create = await service.CreateAsync(
                        session,
                        new CharacterCreateRequest(name, characterClass, gender, lifeSkill, "UnknownAppearance", $"create-{caseNumber}"),
                        CancellationToken.None);

                    var reloginList = await characters.ListByAccountAsync(1, CancellationToken.None);
                    var created = Assert.Single(reloginList);
                    var delete = await service.DeleteAsync(
                        session,
                        new CharacterDeleteRequest(created.CharacterId, $"delete-{caseNumber}"),
                        CancellationToken.None);
                    var afterDelete = await characters.ListByAccountAsync(1, CancellationToken.None);

                    Assert.Equal(CharacterOperationResultCode.Success, create.Code);
                    Assert.Equal(name, created.Name);
                    Assert.Equal(characterClass, created.Class);
                    Assert.Equal(gender, created.Gender);
                    Assert.Equal(lifeSkill, created.LifeSkill);
                    Assert.Equal(CharacterOperationResultCode.Success, delete.Code);
                    Assert.Empty(afterDelete);
                }
            }
        }

        Assert.Equal(32, caseNumber);
    }

    [Fact]
    public async Task Character_lifecycle_only_admits_names_representable_by_the_production_world_projection()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(
            characters,
            sessions,
            new ReplayGuard(),
            creationAuthority: TestCharacterCreationAuthority());
        var validSession = sessions.Authenticate(sessions.Create("conn-valid", "127.0.0.1:1000"), 1).Value!;
        var longSession = sessions.Authenticate(sessions.Create("conn-long", "127.0.0.1:1001"), 2).Value!;
        var unicodeSession = sessions.Authenticate(sessions.Create("conn-unicode", "127.0.0.1:1002"), 3).Value!;

        var maximum = await service.CreateAsync(
            validSession,
            new CharacterCreateRequest("ABCDEFGHIJK", "Class1", "Gender1", "LifeSkill1", "Appearance1", "valid"),
            CancellationToken.None);
        var tooLong = await service.CreateAsync(
            longSession,
            new CharacterCreateRequest("ABCDEFGHIJKL", "Class1", "Gender1", "LifeSkill1", "Appearance1", "long"),
            CancellationToken.None);
        var unicode = await service.CreateAsync(
            unicodeSession,
            new CharacterCreateRequest("角色One", "Class1", "Gender1", "LifeSkill1", "Appearance1", "unicode"),
            CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.Success, maximum.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidName, tooLong.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidName, unicode.Code);
        Assert.Throws<ArgumentException>(() => OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128(1, "角色One"));
    }

    [Fact]
    public async Task Character_lifecycle_rejects_missing_or_oversized_request_ids_before_replay_storage()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(
            characters,
            sessions,
            new ReplayGuard(),
            creationAuthority: TestCharacterCreationAuthority());
        var session = sessions.Authenticate(sessions.Create("conn-request-id", "127.0.0.1:1000"), 1).Value!;
        var oversized = new string('x', OfficialCharacterIdentityPolicy.MaximumLifecycleRequestIdLength + 1);

        var create = await service.CreateAsync(
            session,
            new CharacterCreateRequest("HeroOne", "Class1", "Gender1", "LifeSkill1", "Appearance1", string.Empty),
            CancellationToken.None);
        var delete = await service.DeleteAsync(
            session,
            new CharacterDeleteRequest(1, oversized),
            CancellationToken.None);
        var rename = await service.RenameAsync(
            session,
            new CharacterRenameRequest(1, "HeroTwo", "   "),
            CancellationToken.None);
        var select = await service.SelectAsync(
            session,
            new CharacterSelectRequest(1, oversized),
            CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.InvalidRequestId, create.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidRequestId, delete.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidRequestId, rename.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidRequestId, select.Code);
        Assert.Empty(await characters.ListByAccountAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Character_lifecycle_rejects_invalid_class_gender_and_life_skill()
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(characters, sessions, new ReplayGuard());
        var session = sessions.Authenticate(sessions.Create("conn-1", "127.0.0.1:1000"), 1).Value!;

        var invalidClass = await service.CreateAsync(
            session,
            new CharacterCreateRequest("HeroOne", "BadClass", "Gender1", "LifeSkill1", "Unknown", "invalid-class"),
            CancellationToken.None);
        var invalidGender = await service.CreateAsync(
            session,
            new CharacterCreateRequest("HeroTwo", "Class1", "BadGender", "LifeSkill1", "Unknown", "invalid-gender"),
            CancellationToken.None);
        var invalidLifeSkill = await service.CreateAsync(
            session,
            new CharacterCreateRequest("HeroTre", "Class1", "Gender1", "BadLifeSkill", "Unknown", "invalid-life-skill"),
            CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.InvalidClass, invalidClass.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidGender, invalidGender.Code);
        Assert.Equal(CharacterOperationResultCode.InvalidLifeSkill, invalidLifeSkill.Code);
        Assert.Empty(await characters.ListByAccountAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Official_character_create_and_delete_runtime_remain_evidence_gated_with_zero_network_bytes()
    {
        var composition = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 100,
            new InMemoryAccountRepository(),
            new InMemoryCharacterRepository());
        var runtime = new OfficialLoginCharacterProtocolRuntime(
            new EvidenceGatedOfficialLoginCharacterProtocolCodec(),
            composition.AuthenticationService,
            composition.CharacterListQuery,
            composition.CharacterRuntimeService);
        var session = composition.SessionStore.Authenticate(composition.SessionStore.Create("conn-1", "127.0.0.1:1000"), 1).Value!;
        var packet = new PacketEnvelope(
            new PacketHeader(new Opcode(0xFFFF), 4, 0, "FFFF"),
            new byte[] { 0xFF, 0xFF },
            ProtocolConfidence.Unknown);

        var create = await runtime.HandleCharacterCreateAsync(session, packet, CancellationToken.None);
        var delete = await runtime.HandleCharacterDeleteAsync(session, packet, CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, create.Code);
        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, delete.Code);
        Assert.Equal(0, create.ResponseFrame.Length);
        Assert.Equal(0, delete.ResponseFrame.Length);
        Assert.Contains(create.RecoveryGaps, gap => gap.Contains(nameof(OfficialLoginCharacterPacketKind.CharacterCreateRequest), StringComparison.Ordinal));
        Assert.Contains(delete.RecoveryGaps, gap => gap.Contains(nameof(OfficialLoginCharacterPacketKind.CharacterDeleteRequest), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Official_login_and_character_lifecycle_do_not_mutate_before_outbound_preflight()
    {
        var packet = new PacketEnvelope(
            new PacketHeader(new Opcode(0x0017), 4, 0, "0017"),
            new byte[] { 0x17, 0x00 },
            ProtocolConfidence.Verified);

        var loginAccounts = new InMemoryAccountRepository([Account(1, "ray", "secret")]);
        var loginComposition = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 1,
            loginAccounts,
            new InMemoryCharacterRepository(),
            TestCharacterCreationAuthority());
        var loginRuntime = new OfficialLoginCharacterProtocolRuntime(
            new RequestOnlyCharacterLifecycleCodec(),
            loginComposition.AuthenticationService,
            loginComposition.CharacterListQuery,
            loginComposition.CharacterRuntimeService);
        var loginSession = loginComposition.SessionStore.Create("login-preflight", "127.0.0.1:9999");

        var login = await loginRuntime.HandleLoginAsync(loginSession, packet, CancellationToken.None);

        var createCharacters = new InMemoryCharacterRepository();
        var createComposition = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 1,
            new InMemoryAccountRepository(),
            createCharacters,
            TestCharacterCreationAuthority());
        var createRuntime = new OfficialLoginCharacterProtocolRuntime(
            new RequestOnlyCharacterLifecycleCodec(),
            createComposition.AuthenticationService,
            createComposition.CharacterListQuery,
            createComposition.CharacterRuntimeService);
        var createSession = createComposition.SessionStore.Authenticate(
            createComposition.SessionStore.Create("create-preflight", "127.0.0.1:1000"),
            1).Value!;

        var create = await createRuntime.HandleCharacterCreateAsync(createSession, packet, CancellationToken.None);

        var deleteCharacters = new InMemoryCharacterRepository([OfficialNetworkCharacter()]);
        var deleteComposition = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 1,
            new InMemoryAccountRepository(),
            deleteCharacters,
            TestCharacterCreationAuthority());
        var deleteRuntime = new OfficialLoginCharacterProtocolRuntime(
            new RequestOnlyCharacterLifecycleCodec(),
            deleteComposition.AuthenticationService,
            deleteComposition.CharacterListQuery,
            deleteComposition.CharacterRuntimeService);
        var deleteSession = deleteComposition.SessionStore.Authenticate(
            deleteComposition.SessionStore.Create("delete-preflight", "127.0.0.1:1001"),
            1).Value!;

        var delete = await deleteRuntime.HandleCharacterDeleteAsync(deleteSession, packet, CancellationToken.None);
        var select = await deleteRuntime.HandleCharacterSelectAsync(deleteSession, packet, CancellationToken.None);
        var afterSelect = await deleteCharacters.FindByIdAsync(OfficialNetworkCharacter().CharacterId, CancellationToken.None);

        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, login.Code);
        Assert.False(loginComposition.SessionStore.Get(loginSession.SessionId).Value!.IsAuthenticated);
        Assert.Null((await loginAccounts.FindByIdAsync(1, CancellationToken.None))!.CurrentSessionId);
        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, create.Code);
        Assert.Empty(await createCharacters.ListByAccountAsync(1, CancellationToken.None));
        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, delete.Code);
        Assert.Single(await deleteCharacters.ListByAccountAsync(1, CancellationToken.None));
        Assert.Equal(CharacterOperationResultCode.NeedsProtocolRecovery, select.Code);
        Assert.Null(afterSelect!.LastPlayedAtUtc);
        Assert.Null(deleteComposition.SessionStore.Get(deleteSession.SessionId).Value!.CharacterId);
    }

    [Fact]
    public async Task Disconnect_cleanup_releases_duplicate_login_and_account_session()
    {
        var accountRepository = new InMemoryAccountRepository([Account(1, "ray", "secret")]);
        var duplicateGuard = new DuplicateLoginGuard();
        var sessions = new SessionStore(closeObservers: [new LoginSessionCleanupObserver(duplicateGuard, accountRepository)]);
        var characters = new InMemoryCharacterRepository();
        var auth = new AuthenticationService(
            accountRepository,
            new PasswordVerifier(),
            sessions,
            duplicateGuard,
            new ReplayGuard(),
            new LoginAttemptRateLimiter(limit: 100),
            new LoginAttemptAudit(),
            maximumSessions: 100);
        var session = sessions.Create("conn-1", "127.0.0.1:1000");
        var login = await auth.LoginAsync(new LoginRequest("ray", "secret", "login-cleanup"), session, new CharacterListQuery(characters), CancellationToken.None);

        await sessions.CloseAsync(login.Session, "client_close", CancellationToken.None);
        var account = await accountRepository.FindByIdAsync(1, CancellationToken.None);

        Assert.False(duplicateGuard.IsOnline(1));
        Assert.Null(account!.CurrentSessionId);
        Assert.Equal(ProtocolStage.Closed, sessions.Get(session.SessionId).Value!.ProtocolStage);
    }

    [Fact]
    public async Task Login_recovers_stale_persisted_lease_and_atomically_transfers_it_to_world_session()
    {
        var accountRepository = new InMemoryAccountRepository(
        [
            Account(1, "ray", "secret") with { CurrentSessionId = "stale-process-session" }
        ]);
        var duplicateGuard = new DuplicateLoginGuard();
        var sessions = new SessionStore(
            closeObservers: [new LoginSessionCleanupObserver(duplicateGuard, accountRepository)]);
        var auth = new AuthenticationService(
            accountRepository,
            new PasswordVerifier(),
            sessions,
            duplicateGuard,
            new ReplayGuard(),
            new LoginAttemptRateLimiter(limit: 100),
            new LoginAttemptAudit(),
            maximumSessions: 1);
        var loginSession = sessions.Create("login-connection", "127.0.0.1:1000");

        var login = await auth.LoginAsync(
            new LoginRequest("ray", "secret", "stale-lease-login"),
            loginSession,
            new CharacterListQuery(new InMemoryCharacterRepository()),
            CancellationToken.None);
        var afterLogin = await accountRepository.FindByIdAsync(1, CancellationToken.None);

        Assert.True(login.Succeeded);
        Assert.Equal(login.Session.SessionId, afterLogin!.CurrentSessionId);
        Assert.Equal(1, duplicateGuard.Count);

        var worldSession = sessions.Create("world-connection", "127.0.0.1:1001");
        var transferred = await auth.TransferToWorldSessionAsync(
            login.Session,
            worldSession,
            CancellationToken.None);
        await sessions.CloseAsync(login.Session, "login_to_world_transferred", CancellationToken.None);
        var afterTransfer = await accountRepository.FindByIdAsync(1, CancellationToken.None);

        Assert.True(transferred.Succeeded);
        Assert.Equal(worldSession.SessionId, afterTransfer!.CurrentSessionId);
        Assert.True(duplicateGuard.IsOnline(1));
        Assert.Equal(1, duplicateGuard.Count);

        await sessions.CloseAsync(transferred.Value!, "world_disconnect", CancellationToken.None);
        var afterWorldClose = await accountRepository.FindByIdAsync(1, CancellationToken.None);

        Assert.Null(afterWorldClose!.CurrentSessionId);
        Assert.False(duplicateGuard.IsOnline(1));
        Assert.Equal(0, duplicateGuard.Count);
    }

    [Fact]
    public async Task Duplicate_login_guard_enforces_capacity_and_never_loses_a_successfully_transferred_owner()
    {
        var capacityGuard = new DuplicateLoginGuard();
        var admissions = await Task.WhenAll(Enumerable.Range(1, 64).Select(accountId =>
            Task.Run(() => capacityGuard.TryEnter(accountId, $"session-{accountId}", maximumAccounts: 1))));

        Assert.Single(admissions, result => result == DuplicateLoginAdmissionResult.Entered);
        Assert.Equal(63, admissions.Count(result => result == DuplicateLoginAdmissionResult.CapacityReached));
        Assert.Equal(1, capacityGuard.Count);

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            var guard = new DuplicateLoginGuard();
            var accountId = iteration + 1L;
            var loginSessionId = $"login-{iteration}";
            var worldSessionId = $"world-{iteration}";
            Assert.True(guard.TryEnter(accountId, loginSessionId));
            using var start = new ManualResetEventSlim(false);
            var cleanup = Task.Run(() =>
            {
                start.Wait();
                guard.Leave(accountId, loginSessionId);
            });
            var transfer = Task.Run(() =>
            {
                start.Wait();
                return guard.TryTransfer(accountId, loginSessionId, worldSessionId);
            });

            start.Set();
            await cleanup;
            var transferred = await transfer;
            if (transferred)
            {
                Assert.True(guard.IsOnline(accountId));
                guard.Leave(accountId, worldSessionId);
            }
        }
    }

    [Fact]
    public async Task In_memory_account_session_replacement_is_compare_and_swap()
    {
        var repository = new InMemoryAccountRepository(
        [
            Account(1, "ray", "secret") with { CurrentSessionId = "owner-a" }
        ]);

        var conflict = await repository.ReplaceCurrentSessionAsync(
            1,
            "wrong-owner",
            "owner-b",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var replaced = await repository.ReplaceCurrentSessionAsync(
            1,
            "owner-a",
            "owner-b",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var account = await repository.FindByIdAsync(1, CancellationToken.None);

        Assert.False(conflict.Succeeded);
        Assert.Equal("account.session_conflict", conflict.Error.Code);
        Assert.True(replaced.Succeeded);
        Assert.Equal("owner-b", account!.CurrentSessionId);
    }

    [Fact]
    public async Task Disconnect_cleanup_releases_character_runtime_registration()
    {
        var registry = new CharacterRuntimeRegistry();
        var sessions = new SessionStore(closeObservers: [registry]);
        var characters = new InMemoryCharacterRepository();
        var service = new CharacterRuntimeService(characters, sessions, new ReplayGuard(), registry);
        var session = sessions.Authenticate(sessions.Create("conn-1", "127.0.0.1:1000"), 1).Value!;
        var listed = sessions.Transition(session, ProtocolStage.CharacterList).Value!;
        var created = await characters.CreateAsync(1, new CharacterCreateRequest("HeroOne", "Unknown", "Unknown", "LifeSkill1", "Unknown", "seed"), 0, 0, 0, 4, CancellationToken.None);
        var selected = await service.SelectAsync(listed, new CharacterSelectRequest(created.Value!.CharacterId, "select"), CancellationToken.None);

        Assert.True(registry.IsRegistered(selected.Session.SessionId));

        await sessions.CloseAsync(selected.Session, "client_close", CancellationToken.None);

        Assert.False(registry.IsRegistered(selected.Session.SessionId));
    }

    [Fact]
    public void Unified_runtime_composition_uses_single_shared_runtime_instances()
    {
        var runtime = UnifiedRuntimeComposition.CreateForTesting(
            maximumSessions: 100,
            new InMemoryAccountRepository(),
            new InMemoryCharacterRepository());

        Assert.Same(runtime.SessionStore, RuntimePrivate.SessionStore(runtime.SessionAuthority));
        Assert.Same(runtime.DuplicateLoginGuard, RuntimePrivate.DuplicateLoginGuard(runtime.AuthenticationService));
        Assert.Same(runtime.SessionStore, RuntimePrivate.SessionStore(runtime.AuthenticationService));
        Assert.Same(runtime.SessionStore, RuntimePrivate.SessionStore(runtime.CharacterRuntimeService));
        Assert.Same(runtime.ReplayGuard, RuntimePrivate.ReplayGuard(runtime.AuthenticationService));
        Assert.Same(runtime.ReplayGuard, RuntimePrivate.ReplayGuard(runtime.CharacterRuntimeService));
    }

    [Fact]
    public async Task Runtime_command_dispatcher_serializes_concurrent_world_mutations()
    {
        var dispatcher = new RuntimeCommandDispatcher(capacity: 128);
        var active = 0;
        var maximumActive = 0;
        var completed = 0;

        var commands = Enumerable.Range(0, 100)
            .Select(_ => dispatcher.EnqueueAsync(async cancellationToken =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref maximumActive, current);
                await Task.Delay(1, cancellationToken);
                Interlocked.Increment(ref completed);
                Interlocked.Decrement(ref active);
            }, CancellationToken.None).AsTask())
            .ToArray();

        await Task.WhenAll(commands);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(100, completed);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task Runtime_command_failure_is_observed_and_does_not_stop_following_command()
    {
        var dispatcher = new RuntimeCommandDispatcher();
        var failed = dispatcher.EnqueueAsync(
            _ => throw new InvalidOperationException("command failed"),
            CancellationToken.None).AsTask();
        var completed = false;
        var next = dispatcher.EnqueueAsync(
            _ =>
            {
                completed = true;
                return ValueTask.CompletedTask;
            },
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        await next;
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.True(completed);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.EnqueueAsync(_ => ValueTask.CompletedTask, CancellationToken.None).AsTask());
    }

    private static ICharacterCreationAuthority TestCharacterCreationAuthority() =>
        new FixedCharacterCreationAuthority(
            new CharacterCreationSpawn(
                MapId: 1,
                PositionX: 10,
                PositionY: 20,
                EvidenceStatus: "Verified",
                EvidenceReference: "test-only-character-creation-profile"));

    private sealed class FixedCharacterCreationAuthority(CharacterCreationSpawn spawn) : ICharacterCreationAuthority
    {
        public Task<OperationResult<CharacterCreationSpawn>> ResolveAsync(
            CharacterCreateRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(spawn.IsProductionEligible
                ? OperationResult<CharacterCreationSpawn>.Success(spawn)
                : OperationResult<CharacterCreationSpawn>.Failure(
                    "character.creation_profile_evidence_blocked",
                    "The supplied test character creation spawn profile is not production eligible."));
    }

    private sealed class RequestOnlyCharacterLifecycleCodec : IOfficialLoginCharacterProtocolCodec
    {
        public OfficialProtocolRecoveryCoverage Coverage { get; } = new(
        [
            new(
                OfficialLoginCharacterPacketKind.CharacterCreateResponse,
                "Verified create result response",
                "NotRecovered",
                "Test codec intentionally lacks the result serializer."),
            new(
                OfficialLoginCharacterPacketKind.CharacterDeleteResponse,
                "Verified delete result response",
                "NotRecovered",
                "Test codec intentionally lacks the result serializer.")
        ]);

        public OfficialLoginProtocolCapabilities LoginCapabilities { get; } = new(false, false, false, false);

        public OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginRequest(PacketEnvelope packet) =>
            Blocked<VerifiedOpaqueLoginPacket>();

        public OperationResult<VerifiedOpaqueLoginPacket> VerifyOpaqueLoginSuccessResponse(PacketEnvelope packet) =>
            Blocked<VerifiedOpaqueLoginPacket>();

        public OperationResult<OfficialLoginPacket> DeserializeLoginRequest(PacketEnvelope packet) =>
            OperationResult<OfficialLoginPacket>.Success(new(
                "ray",
                "secret",
                new Dictionary<string, byte[]>()));

        public OperationResult<ReadOnlyMemory<byte>> SerializeLoginResponse(OfficialLoginResponsePacket response) =>
            Blocked<ReadOnlyMemory<byte>>();

        public OperationResult<OfficialCharacterListRequestPacket> DeserializeCharacterListRequest(PacketEnvelope packet) =>
            Blocked<OfficialCharacterListRequestPacket>();

        public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterListResponse(OfficialCharacterListResponsePacket response) =>
            Blocked<ReadOnlyMemory<byte>>();

        public OperationResult<OfficialCharacterCreateRequestPacket> DeserializeCharacterCreateRequest(PacketEnvelope packet) =>
            OperationResult<OfficialCharacterCreateRequestPacket>.Success(new(
                "HeroOne",
                "Class1",
                "Gender1",
                "LifeSkill1",
                "Appearance1",
                new Dictionary<string, byte[]>()));

        public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterCreateResponse(OfficialCharacterCreateResponsePacket response) =>
            Blocked<ReadOnlyMemory<byte>>();

        public OperationResult<OfficialCharacterDeleteRequestPacket> DeserializeCharacterDeleteRequest(PacketEnvelope packet) =>
            OperationResult<OfficialCharacterDeleteRequestPacket>.Success(new(
                OfficialNetworkCharacter().CharacterId,
                null,
                new Dictionary<string, byte[]>()));

        public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterDeleteResponse(OfficialCharacterDeleteResponsePacket response) =>
            Blocked<ReadOnlyMemory<byte>>();

        public OperationResult<OfficialCharacterSelectRequestPacket> DeserializeCharacterSelectRequest(PacketEnvelope packet) =>
            OperationResult<OfficialCharacterSelectRequestPacket>.Success(new(
                OfficialNetworkCharacter().CharacterId,
                null,
                new Dictionary<string, byte[]>()));

        public OperationResult<ReadOnlyMemory<byte>> SerializeCharacterSelectResponse(OfficialCharacterSelectResponsePacket response) =>
            Blocked<ReadOnlyMemory<byte>>();

        public OperationResult<ReadOnlyMemory<byte>> SerializeWorldEntryContext(OfficialWorldEntryContextPacket context) =>
            Blocked<ReadOnlyMemory<byte>>();

        private static OperationResult<T> Blocked<T>() =>
            OperationResult<T>.Failure(
                "protocol.recovery_required",
                "The test codec intentionally leaves this operation evidence-blocked.");
    }

    private static (AuthenticationService Auth, SessionStore Sessions, InMemoryCharacterRepository Characters, LoginAttemptAudit Audit) CreateAuthenticationRuntime(IEnumerable<AccountRecord> accounts)
    {
        var sessions = new SessionStore();
        var characters = new InMemoryCharacterRepository();
        var audit = new LoginAttemptAudit();
        var auth = new AuthenticationService(
            new InMemoryAccountRepository(accounts),
            new PasswordVerifier(),
            sessions,
            new DuplicateLoginGuard(),
            new ReplayGuard(),
            new LoginAttemptRateLimiter(limit: 100),
            audit,
            maximumSessions: 100);
        return (auth, sessions, characters, audit);
    }

    private static byte[] ReadProtocolEvidence(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return File.ReadAllBytes(Path.Combine(
            current.FullName,
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "VerifiedRaw",
            "Login",
            fileName));
    }

    private static AccountRecord Account(long id, string username, string password, AccountStatus status = AccountStatus.Active, DateTimeOffset? lockedUntil = null) =>
        new(
            id,
            username,
            PasswordVerifier.Hash(password),
            status,
            DateTimeOffset.UtcNow,
            null,
            0,
            lockedUntil,
            null,
            Guid.NewGuid().ToString("N"));

    private static CharacterSummary OfficialNetworkCharacter(string name = "NetHero") =>
        new(
            10,
            1,
            name,
            "Class1",
            "Gender1",
            "LifeSkill1",
            1,
            "Default",
            19,
            28,
            34,
            "Active",
            DateTimeOffset.UtcNow,
            null);

    private static byte[] BuildOfficialWorldMovementFrame(
        int x,
        int y,
        byte sequence,
        byte state = OfficialClientWorldProtocolFrames.WorldMovementMountedState)
    {
        var decoded = new byte[OfficialClientWorldProtocolFrames.WorldMovementFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
        decoded[2] = OfficialClientWorldProtocolFrames.WorldMovementOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(3), checked((ushort)x));
        BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(5), checked((ushort)y));
        decoded[7] = sequence;
        decoded[8] = state;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    private static byte[] BuildOfficialInventoryActivationTransportFrame(
        ushort clientItemId,
        byte slotIndex,
        byte quantity)
    {
        var decoded = new byte[PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
        decoded[2] = PublicBetaCompatibilityGameplayRequestWireAdapter.Interaction28Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(3), clientItemId);
        decoded[5] = slotIndex;
        decoded[6] = quantity;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    private static byte[] BuildOfficialCompatibilityRequestTransportFrame(byte opcode, int decodedLength)
    {
        // Keep packets with short declared compatibility lengths (e.g. public-beta 2-byte records)
        // at a router-safe minimum size so the frame still decodes to a real opcode candidate.
        var frameLength = Math.Max(decodedLength, 4);
        var decoded = new byte[frameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)frameLength));
        decoded[2] = opcode;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(decoded);
        Array.Clear(decoded);
        return encoded;
    }

    private static void AssertMovementAcknowledgement(byte[] encoded, byte expectedSequence)
    {
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);
        Assert.Equal(OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementFrameLength, decoded.Length);
        Assert.Equal(OfficialClientWorldProtocolFrames.WorldMovementAcknowledgementOpcode, decoded[2]);
        Assert.Equal(expectedSequence, decoded[3]);
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decoded), decoded[^1]);
    }

    private static UnifiedRuntimeComposition CreateOfficialNetworkRuntime(CharacterSummary character) =>
        UnifiedRuntimeComposition.CreateForTesting(
            100,
            new InMemoryAccountRepository([Account(1, "fixture_account", "xxxxxxxxxx")]),
            new InMemoryCharacterRepository([character]));

    private static TcpNetworkHost CreateOfficialNetworkHost(
        UnifiedRuntimeComposition runtime,
        IServerLogger? logger = null) =>
        new(
            runtime.PacketFactory,
            runtime.ProtocolConnectionRuntime,
            runtime.SessionAuthority,
            runtime.AuthenticationService,
            runtime.CharacterListQuery,
            new InMemoryPortalTransitionStore(),
            new WorldSessionCoordinator(new InMemoryWorldContentRepository(BuildVerifiedPortalWorldContent())),
            new LegacyCompatibilityWorldBootstrapProjector(),
            characterRuntimeService: runtime.CharacterRuntimeService,
            logger: logger);

    private static async Task<TcpClient> OpenOfficialCharacterCreationReconnectAsync(
        int port,
        UnifiedRuntimeComposition runtime)
    {
        using (var initialClient = new TcpClient())
        {
            await initialClient.ConnectAsync(IPAddress.Loopback, port);
            var initialStream = initialClient.GetStream();
            await ReadExactAsync(initialStream, 19);
            await initialStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
            Assert.Equal("0600ED7BEF12", Convert.ToHexString(await ReadExactAsync(initialStream, 6)));
            var loginRequest = BuildSyntheticLoginRequest208();
            await initialStream.WriteAsync(loginRequest);
            var expectedSuccess = OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap(
                loginRequest,
                IPAddress.Loopback.GetAddressBytes(),
                checked((ushort)port));
            Assert.Equal(expectedSuccess, await ReadExactAsync(initialStream, expectedSuccess.Length));
            await initialStream.WriteAsync(Convert.FromHexString("06009202CE97"));
            var emptyList = await ReadExactAsync(initialStream, OfficialServerSelectionWireCodec.ResponseFrameLength);
            var decodedEmptyList = OfficialServerSelectionWireCodec.DecodeResponse(
                emptyList,
                OfficialServerSelectionWireCodec.ClientBuildId);
            Assert.True(decodedEmptyList.Succeeded);
            Assert.Equal(string.Empty, decodedEmptyList.Value!.CharacterName);
            await AssertWorldSocketClosedAsync(initialStream);
            await WaitUntilAsync(() => !runtime.DuplicateLoginGuard.IsOnline(1));
        }

        var reconnect = new TcpClient();
        await reconnect.ConnectAsync(IPAddress.Loopback, port);
        var reconnectStream = reconnect.GetStream();
        await ReadExactAsync(reconnectStream, 19);
        await reconnectStream.WriteAsync(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));
        Assert.Equal("0600EDADBD60", Convert.ToHexString(await ReadExactAsync(reconnectStream, 6)));
        await reconnectStream.WriteAsync(BuildSyntheticLoginRequest208());
        return reconnect;
    }

    private sealed class ClientCoordinateOverrideProjector(ushort clientX, ushort clientY)
        : IWorldBootstrapProjector
    {
        private readonly LegacyCompatibilityWorldBootstrapProjector _inner = new();

        public OperationResult<WorldBootstrapProjection> Project(WorldSessionBinding binding)
        {
            var projection = _inner.Project(binding);
            if (!projection.Succeeded || projection.Value is null)
            {
                return projection;
            }

            return OperationResult<WorldBootstrapProjection>.Success(projection.Value with
            {
                ClientX = clientX,
                ClientY = clientY
            });
        }
    }

    private sealed class MariaDbAuthorityTestWorldContentRepository(WorldContentSnapshot snapshot)
        : IWorldContentRepository
    {
        public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.MariaDb;

        public Task<WorldContentSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static TcpListener BindProbe(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }

    private static ReadOnlyMemory<byte> BuildSyntheticLoginRequest208(string password = "xxxxxxxxxx")
    {
        var decoded = new byte[208];
        decoded[0] = 0xD0;
        decoded[1] = 0x00;
        Encoding.ASCII.GetBytes("fixture_account").CopyTo(decoded, 3);
        Encoding.ASCII.GetBytes(password).CopyTo(decoded, 27);
        return OfficialClientLoginProtocolFrames.EncodeLoginFrame(decoded);
    }

    private static ReadOnlyMemory<byte> BuildWorldEntryStateGateFrame208()
    {
        var frame = new byte[208];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)frame.Length);
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), timeout.Token);
            if (read == 0)
            {
                throw new EndOfStreamException($"Expected {length} bytes but received {offset}.");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task AssertWorldSocketClosedAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await stream.ReadExactlyAsync(new byte[1], timeout.Token));
    }

    private static ProtocolConnectionRuntime RuntimeWithSyntheticFamilies(params (string Id, string Family, string Sample)[] packets)
    {
        var registry = SyntheticRegistry(packets);
        return new ProtocolConnectionRuntime(new PacketFactory(new PacketDeserializer(registry)));
    }

    private static ProtocolRegistry SyntheticRegistry(params (string Id, string Family, string Sample)[] packets)
    {
        var registry = new ProtocolRegistry(new ProtocolKnowledgeBase(
            "test",
            packets.Select(packet => new PacketKnowledge(
                packet.Id,
                packet.Family,
                PacketDirection.ClientToServer,
                packet.Sample.Length / 2,
                packet.Sample.Substring(4, 4),
                ProtocolConfidence.Verified,
                PacketRecoveryStatus.Verified,
                Recovered: true,
                Verified: true,
                [new ProtocolEvidenceSource("tests", "Synthetic", ProtocolConfidence.Verified)],
                [],
                [],
                [packet.Sample],
                [packet.Sample])).ToArray(),
            [],
            "tests"));
        return registry;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5_000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before the test timeout.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class SynchronizedStringWriter : StringWriter
    {
        private readonly object _gate = new();

        public override void Write(char value)
        {
            lock (_gate)
            {
                base.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                base.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                base.WriteLine(value);
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return base.ToString();
            }
        }
    }

    private static CharacterSummary BuildCharacter(int mapId) =>
        new(
            0x035F7FF4,
            1,
            "kero",
            "Unknown",
            "Unknown",
            "LifeSkill1",
            1,
            "official-frozen-player-spawn",
            mapId,
            4860,
            34399,
            "Active",
            new DateTimeOffset(2026, 7, 30, 4, 16, 12, TimeSpan.Zero),
            null);

    private static WorldSessionBinding BindDbBackedWorldSession(string connectionId, string sessionLabel)
    {
        var session = RuntimeSession.Connected(connectionId, "127.0.0.1:1000", DateTimeOffset.UtcNow) with
        {
            AccountId = 1,
            CharacterId = 0x035F7FF4,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };

        var binding = new WorldSessionBinder(new MapRuntimeFactory()).Bind(
            session with { SessionId = sessionLabel },
            BuildCharacter(mapId: 1785918668),
            BuildExperimentalWorldContent(),
            WorldContentMode.MariaDbAuthoritative);
        Assert.True(binding.Succeeded);
        return binding.Value!;
    }

    private static RuntimeSession BuildWorldEnteringSession(long characterId) =>
        RuntimeSession.Connected("world-connection", "127.0.0.1:2592", DateTimeOffset.UtcNow) with
        {
            SessionId = "world-session-" + characterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AccountId = 1,
            CharacterId = characterId,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };

    private static CharacterSummary BuildVerifiedPortalCharacter(long characterId, string name) =>
        new(
            characterId,
            1,
            name,
            "Class1",
            "Gender1",
            "LifeSkill1",
            1,
            "Default",
            19,
            28,
            34,
            "Active",
            DateTimeOffset.UtcNow,
            null);

    private static WorldContentSnapshot BuildVerifiedPortalWorldContent()
    {
        var map = new MapDefinition(
            19,
            19,
            "Verified Portal Map 19",
            "official-phase2-map-19",
            new MapBounds(0, 0, 1000, 1000),
            [new SpawnPoint("VerifiedPortalLocation", new WorldPosition3(28, 34), WorldDirection.Unknown, OfficialPortalWireCodec.EvidenceId)],
            [],
            [],
            [],
            [],
            [],
            new ClientMapIdentity(
                19,
                19,
                4,
                "official-phase2-map-19",
                OfficialPortalWireCodec.ClientBuildId,
                1m,
                1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                OfficialPortalWireCodec.EvidenceId));
        return new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, []);
    }

    private static WorldContentSnapshot BuildVerifiedPortalRoundTripWorldContent()
    {
        var source = BuildVerifiedPortalWorldContent().Maps[19];
        var target = new MapDefinition(
            3,
            3,
            "Verified Portal Map 3",
            "legacy-phase2-map-3",
            new MapBounds(0, 0, 1000, 1000),
            [new SpawnPoint("VerifiedPortalArrival", new WorldPosition3(196, 139), WorldDirection.Unknown, OfficialPortalWireCodec.EvidenceId)],
            [],
            [],
            [],
            [],
            [],
            new ClientMapIdentity(
                3,
                3,
                4,
                "legacy-phase2-map-3",
                OfficialPortalWireCodec.ClientBuildId,
                1m,
                1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                OfficialPortalWireCodec.EvidenceId));
        return new WorldContentSnapshot(new Dictionary<int, MapDefinition>
        {
            [19] = source,
            [3] = target
        }, []);
    }

    private static WorldContentSnapshot BuildM3NpcWorldContent()
    {
        var firstIdentity = new OfficialNpcWireIdentity(
            OfficialNpcReplicationWireCodec.ClientBuildId,
            1504,
            2,
            142,
            3,
            4,
            1,
            "83D11BE70AB2E0D6660BC38915E8C3C101FFFC2C67D7A950453E24D79669A84B",
            "Verified",
            "M3 test evidence",
            OfficialNpcReplicationWireCodec.OpaqueTemplateSha256);
        var secondIdentity = firstIdentity with
        {
            EntityHandle = 4638,
            SelectorHighBits = 2,
            DirectionCode = 5,
            SpawnMessageSha256 = "7E3AC167DEF745DE3BE7D38C22B4C0ADC1419E6DC20851F8FA3CD22D61CCA644"
        };
        var map = new MapDefinition(
            19,
            19,
            "M3 NPC Map 19",
            "official-m3-map-19",
            new MapBounds(0, 0, 209, 629),
            [new SpawnPoint("Player", new WorldPosition3(28, 34), WorldDirection.Unknown, "M3")],
            [
                new NpcPlacementRecord(
                    316049902,
                    1075128734,
                    19,
                    new WorldPosition3(17, 8),
                    WorldDirection.Unknown,
                    "Always",
                    4001,
                    "M3 test",
                    1,
                    "Verified",
                    "雜貨老闆",
                    "data2/rom/npc/npc2643.ROM",
                    "Merchant",
                    true,
                    ContentVersion: "mariadb-roadmap-m3-content-v1",
                    WireIdentity: firstIdentity),
                new NpcPlacementRecord(
                    2124220824,
                    1075128734,
                    19,
                    new WorldPosition3(14, 14),
                    WorldDirection.Unknown,
                    "Always",
                    null,
                    "M3 test",
                    1,
                    "Verified",
                    "雜貨老闆",
                    "data2/rom/npc/npc2643.ROM",
                    "Merchant",
                    true,
                    ContentVersion: "mariadb-roadmap-m3-content-v1",
                    WireIdentity: secondIdentity)
            ],
            [],
            [],
            [new MerchantMapping(
                4001,
                1075128734,
                316049902,
                "M4 verified Merchant binding",
                "雜貨老闆",
                "M4",
                "Currency",
                [],
                true)],
            [],
            new ClientMapIdentity(
                19,
                19,
                4,
                "official-m3-map-19",
                OfficialNpcReplicationWireCodec.ClientBuildId,
                1m,
                1m,
                0m,
                0m,
                MapIdentityEvidenceStatus.Verified,
                MapIdentityEvidenceStatus.Verified,
                true,
                "M3 test evidence"));
        return new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, []);
    }

    private static WorldContentSnapshot BuildExperimentalWorldContent(bool includeMonsterSpawn = true)
    {
        var mapId = 1785918668;
        var npcPlacement = new NpcPlacementRecord(
            5001,
            1001,
            mapId,
            new WorldPosition3(4848, 34400),
            WorldDirection.South,
            "Always",
            4001,
            "NormalizedContent:Phase4DbBackedRuntime",
            0.91,
            "Minimal test placement derived from DB map catalog plus explicit normalized placement evidence.");

        NpcPlacementRecord[] npcPlacements = [npcPlacement];
        MonsterSpawnDefinition[] monsterSpawns = includeMonsterSpawn
            ?
            [
                new MonsterSpawnDefinition(
                    6001,
                    2001,
                    mapId,
                    new WorldPosition3(4872, 34408),
                    WorldDirection.West,
                    TimeSpan.FromMinutes(5),
                    8,
                    1,
                    "TestOnlyNoAi",
                    "NormalizedContent:Phase4DbBackedRuntime")
            ]
            : [];

        PortalDefinition[] portals =
        [
            new PortalDefinition(
                3001,
                new PortalEndpoint(mapId, new WorldPosition3(4900, 34432)),
                new PortalEndpoint(582977555, new WorldPosition3(128, 128)),
                "ContactArea",
                "NoneRecovered",
                "DB portals contains source map candidates; official teleport packet recovery is deferred.")
        ];

        MerchantMapping[] merchants =
        [
            new MerchantMapping(
                4001,
                1001,
                npcPlacement.PlacementId,
                "NormalizedContent:merchant requires validated NPC placement.")
        ];

        var definition = new MapDefinition(
            mapId,
            1,
            "DbBackedExperimental Fenghua town candidate",
            "r_b720effd89db741cf7bb9830f6a01bee",
            new MapBounds(0, 0, 65535, 65535),
            [new SpawnPoint("PlayerSpawnBaseline", new WorldPosition3(4860, 34399), WorldDirection.South, "Mounted movement baseline raw coordinate")],
            npcPlacements,
            monsterSpawns,
            portals,
            merchants,
            ["NPC 1002 remains Unplaced and is excluded from MapRuntime."]);

        return new WorldContentSnapshot(
            new Dictionary<int, MapDefinition> { [definition.MapId] = definition },
            ["NPC 1002 is intentionally unplaced in normalized test content."]);
    }

    private static class RuntimePrivate
    {
        public static SessionStore SessionStore(InMemorySessionAuthority authority) =>
            (SessionStore)GetField(authority, "_sessionStore");

        public static SessionStore SessionStore(AuthenticationService service) =>
            (SessionStore)GetField(service, "_sessions");

        public static SessionStore SessionStore(CharacterRuntimeService service) =>
            (SessionStore)GetField(service, "_sessions");

        public static DuplicateLoginGuard DuplicateLoginGuard(AuthenticationService service) =>
            (DuplicateLoginGuard)GetField(service, "_duplicateLoginGuard");

        public static ReplayGuard ReplayGuard(AuthenticationService service) =>
            (ReplayGuard)GetField(service, "_replayGuard");

        public static ReplayGuard ReplayGuard(CharacterRuntimeService service) =>
            (ReplayGuard)GetField(service, "_replayGuard");

        public static byte[] AccumulatorBuffer(FrameAccumulator accumulator) =>
            (byte[])GetField(accumulator, "_buffer");

        private static object GetField(object instance, string name) =>
            instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(instance)!;
    }

    private sealed class CountingCloseObserver : ISessionCloseObserver
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCloseObserver : ISessionCloseObserver
    {
        public Task SessionClosedAsync(RuntimeSession session, string reason, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("observer failure");
    }

    private sealed class FixedGoldProjectionSource(long balance) : ICharacterGoldProjectionSource
    {
        public Task<OperationResult<long>> LoadGoldAsync(
            long characterId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<long>.Success(balance));
    }

    private sealed class FixedInventoryProjectionSource(PlayerInventorySnapshot snapshot) : ICharacterInventoryProjectionSource
    {
        public Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
            long characterId,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<PlayerInventorySnapshot>.Success(snapshot));
    }

    private sealed class RecordingPacketHandler : IPacketHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<ushort> _order;

        public RecordingPacketHandler(ushort opcode, System.Collections.Concurrent.ConcurrentQueue<ushort> order)
        {
            Opcode = new Opcode(opcode);
            _order = order;
        }

        public Opcode Opcode { get; }

        public ProtocolConfidence Confidence => ProtocolConfidence.Verified;

        public Task<God2.ClassicServer.Application.Common.OperationResult> HandleAsync(
            PacketEnvelope packet,
            CancellationToken cancellationToken)
        {
            _order.Enqueue(packet.Header.Opcode.Value);
            return Task.FromResult(God2.ClassicServer.Application.Common.OperationResult.Success);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int candidate)
        {
            var current = Volatile.Read(ref location);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref location, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
