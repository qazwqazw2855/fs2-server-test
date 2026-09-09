using God2.ClassicServer.Protocol;
using System.Security.Cryptography;
using System.Text.Json;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class ProtocolFoundationTests
{
    [Fact]
    public void Unknown_packet_handlers_cannot_be_registered_as_verified()
    {
        var registry = new PacketRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new UnknownHandler()));
    }

    [Fact]
    public void Official_registry_contains_migrated_verified_and_unknown_packets()
    {
        var registry = ProtocolRegistry.Official;

        Assert.True(registry.TryGetPacket("world-heartbeat-05007ACCEC", out var heartbeat));
        Assert.NotNull(heartbeat);
        Assert.True(heartbeat.Verified);
        Assert.Equal(ProtocolConfidence.Verified, heartbeat.Confidence);
        Assert.NotEmpty(heartbeat.EvidenceSources);

        Assert.True(registry.TryGetPacket(OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId, out var loginRequest));
        Assert.NotNull(loginRequest);
        Assert.True(loginRequest.Verified);
        Assert.Equal(208, loginRequest.Length);
        Assert.Single(loginRequest.KnownFields);
        Assert.Equal("frameLength", loginRequest.KnownFields[0].Name);

        Assert.True(registry.TryGetPacket(OfficialLoginEvidenceCatalog.LoginSuccessResponseKnowledgeId, out var loginResponse));
        Assert.NotNull(loginResponse);
        Assert.True(loginResponse.Verified);
        Assert.Equal(417, loginResponse.Length);

        Assert.True(registry.TryGetPacket("world-npc-interaction-client-8-byte-candidate", out var npc));
        Assert.NotNull(npc);
        Assert.False(npc.Verified);
        Assert.True(npc.Recovered);
        Assert.Equal(PacketRecoveryStatus.EvidenceOnly, npc.RecoveryStatus);

        Assert.True(registry.TryGetPacket("official-20260815-c2s-battle-assistance-join-6a-12", out var assistanceJoin));
        Assert.NotNull(assistanceJoin);
        Assert.Equal("Battle", assistanceJoin.Family);
        Assert.Equal(PacketDirection.ClientToServer, assistanceJoin.Direction);
        Assert.Equal(12, assistanceJoin.Length);
        Assert.True(assistanceJoin.Recovered);
        Assert.False(assistanceJoin.Verified);

        var joinFrame = new PacketDeserializer(registry)
            .Read(Convert.FromHexString("0C006AFE000000F2000000FA"));
        Assert.True(joinFrame.Succeeded);
        Assert.Equal(assistanceJoin.Id, joinFrame.Value!.Knowledge!.Id);
    }

    [Fact]
    public void Captured_20260806_packet_evidence_is_registered_as_evidence_only()
    {
        var registry = ProtocolRegistry.Official;
        var expected = new (string Id, string Family, PacketDirection Direction, int Length)[]
        {
            ("capture-20260806-login-handshake-c2s-208-candidate", "Login", PacketDirection.ClientToServer, 208),
            ("capture-20260806-client-keepalive-c2s-5-candidate", "Heartbeat", PacketDirection.ClientToServer, 5),
            ("capture-20260806-client-action-c2s-20-candidate", "Movement", PacketDirection.ClientToServer, 20),
            ("capture-20260806-server-world-delta-s2c-20-candidate", "WorldState", PacketDirection.ServerToClient, 20),
            ("capture-20260806-server-world-delta-s2c-14-candidate", "WorldState", PacketDirection.ServerToClient, 14)
        };

        foreach (var item in expected)
        {
            Assert.True(registry.TryGetPacket(item.Id, out var packet), $"{item.Id} was not registered.");
            Assert.NotNull(packet);
            Assert.Equal(item.Family, packet.Family);
            Assert.Equal(item.Direction, packet.Direction);
            Assert.Equal(item.Length, packet.Length);
            Assert.False(packet.Verified);
            Assert.True(packet.Recovered);
            Assert.Equal(ProtocolConfidence.Inferred, packet.Confidence);
            Assert.Equal(PacketRecoveryStatus.EvidenceOnly, packet.RecoveryStatus);
            Assert.Contains(packet.EvidenceSources, source =>
                source.Path.EndsWith("CapturedPacketEvidence.20260806.json", StringComparison.Ordinal));
            Assert.Contains(packet.EvidenceSources, source =>
                source.Path.EndsWith("CapturedPacketEvidence.20260806.Batch2.json", StringComparison.Ordinal));
        }

        var deserializer = new PacketDeserializer(registry);
        var keepAlive = deserializer.Read(Convert.FromHexString("0500452B7B")).Value!;
        var clientAction = deserializer.Read(Convert.FromHexString("14004A314E713F59075F64DE02F0874D408536D8")).Value!;
        var serverDelta = deserializer.Read(Convert.FromHexString("1400FB805A673F59073F43DF02F0878545C60CDB")).Value!;
        var batch2KeepAlive = deserializer.Read(Convert.FromHexString("050012405B")).Value!;
        var batch2ClientAction = deserializer.Read(Convert.FromHexString("140092D94A100808DE07FD78E530080A45BB0671")).Value!;
        var batch2ServerDelta20 = deserializer.Read(Convert.FromHexString("140045254A0B0808DE07FE79E53008D25090DC58")).Value!;
        var batch2ServerDelta14 = deserializer.Read(Convert.FromHexString("0E005B0939888A78DAA5873EEAEC")).Value!;

        Assert.Equal("capture-20260806-client-keepalive-c2s-5-candidate", keepAlive.Knowledge!.Id);
        Assert.Equal("capture-20260806-client-action-c2s-20-candidate", clientAction.Knowledge!.Id);
        Assert.Equal("capture-20260806-server-world-delta-s2c-20-candidate", serverDelta.Knowledge!.Id);
        Assert.Equal("capture-20260806-client-keepalive-c2s-5-candidate", batch2KeepAlive.Knowledge!.Id);
        Assert.Equal("capture-20260806-client-action-c2s-20-candidate", batch2ClientAction.Knowledge!.Id);
        Assert.Equal("capture-20260806-server-world-delta-s2c-20-candidate", batch2ServerDelta20.Knowledge!.Id);
        Assert.Equal("capture-20260806-server-world-delta-s2c-14-candidate", batch2ServerDelta14.Knowledge!.Id);
        Assert.All(
            new[] { keepAlive, clientAction, serverDelta, batch2KeepAlive, batch2ClientAction, batch2ServerDelta20, batch2ServerDelta14 },
            packet => Assert.False(packet.Knowledge!.Verified));
    }

    [Fact]
    public void Official_protocol_knowledge_packets_have_game_function_mappings()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(
            "src",
            "God2.ClassicServer.Protocol",
            "Knowledge",
            "protocol-knowledge-base.json")));

        foreach (var collectionName in new[] { "verifiedOpcodes", "unknownOpcodes", "packets" })
        {
            Assert.True(document.RootElement.TryGetProperty(collectionName, out var collection), $"{collectionName} is missing.");
            Assert.NotEmpty(collection.EnumerateArray());

            foreach (var packet in collection.EnumerateArray())
            {
                var id = packet.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(id));

                Assert.True(packet.TryGetProperty("gameFeature", out var gameFeature), $"{collectionName}:{id} is missing gameFeature.");
                Assert.False(string.IsNullOrWhiteSpace(gameFeature.GetString()), $"{collectionName}:{id} has blank gameFeature.");
                Assert.NotEqual("未註解遊戲功能", gameFeature.GetString());

                Assert.True(packet.TryGetProperty("functionDescription", out var functionDescription), $"{collectionName}:{id} is missing functionDescription.");
                Assert.False(string.IsNullOrWhiteSpace(functionDescription.GetString()), $"{collectionName}:{id} has blank functionDescription.");
            }
        }
    }

    [Fact]
    public void Official_npc_world_protocol_blocks_s2c_builders_until_spawn_evidence_exists()
    {
        var protocol = new EvidenceGatedOfficialNpcWorldProtocol();
        var interaction = protocol.GetKnownInteractionCandidate();

        Assert.Equal("0800775884CB3F09", interaction.EncodedHex);
        Assert.Equal("7758", interaction.OpcodeCandidate);
        Assert.Equal(8, interaction.Length);
        Assert.Contains(protocol.EntityCatalog, entry => entry.EntityType == OfficialWorldEntityType.Player && entry.RecoveryStatus == PacketRecoveryStatus.Recovered);
        Assert.Contains(protocol.EntityCatalog, entry => entry.EntityType == OfficialWorldEntityType.NPC && entry.RecoveryStatus == PacketRecoveryStatus.NeedsRecovery);

        var spawn = protocol.BuildNpcSpawn();
        var update = protocol.BuildNpcUpdate();
        var despawn = protocol.BuildNpcDespawn();

        Assert.False(spawn.Succeeded);
        Assert.False(update.Succeeded);
        Assert.False(despawn.Succeeded);
        Assert.Equal(EvidenceGatedOfficialNpcWorldProtocol.NpcS2CEvidenceRequiredCode, spawn.Error.Code);
        Assert.Equal(EvidenceGatedOfficialNpcWorldProtocol.NpcS2CEvidenceRequiredCode, update.Error.Code);
        Assert.Equal(EvidenceGatedOfficialNpcWorldProtocol.NpcS2CEvidenceRequiredCode, despawn.Error.Code);
    }

    [Fact]
    public void Packet_deserializer_classifies_recovered_official_frames()
    {
        var deserializer = new PacketDeserializer(ProtocolRegistry.Official);

        var heartbeat = deserializer.Read(Convert.FromHexString("05007ACCEC"));
        var movement = deserializer.Read(Convert.FromHexString("0A0080BAD7C34DA69488"));

        Assert.True(heartbeat.Succeeded);
        Assert.Equal("Heartbeat", heartbeat.Value!.Knowledge!.Family);
        Assert.Equal("7ACC", heartbeat.Value.Header.OpcodeCandidate);

        Assert.True(movement.Succeeded);
        Assert.Equal("Movement", movement.Value!.Knowledge!.Family);
        Assert.Equal(10, movement.Value.Header.Length);
    }

    [Fact]
    public void Packet_deserializer_keeps_unclassified_frames_unknown()
    {
        var deserializer = new PacketDeserializer(ProtocolRegistry.Official);

        var result = deserializer.Read(Convert.FromHexString("0300FF"));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.Knowledge);
        Assert.Equal(ProtocolConfidence.Unknown, result.Value.Confidence);
    }

    [Fact]
    public void Packet_serializer_preserves_length_prefixed_frame()
    {
        var deserializer = new PacketDeserializer(ProtocolRegistry.Official);
        var serializer = new PacketSerializer();
        var envelope = deserializer.Read(Convert.FromHexString("05007ACCEC")).Value!;

        var result = serializer.Write(envelope);

        Assert.True(result.Succeeded);
        Assert.Equal("05007ACCEC", Convert.ToHexString(result.Value!.Span));
    }

    [Fact]
    public void Official_login_packet_roles_are_verified_while_field_semantics_remain_gated()
    {
        var codec = new EvidenceGatedOfficialLoginCharacterProtocolCodec();
        var deserializer = new PacketDeserializer(ProtocolRegistry.Official);
        var requestPacket = deserializer.Read(ReadEvidence("login-request-208-3399f8d051f7.bin")).Value!;
        var responsePacket = deserializer.Read(ReadEvidence("login-success-bootstrap-417-eaab54f5893d.bin")).Value!;
        var unknownPacket = deserializer.Read(Convert.FromHexString("04001000")).Value!;

        var requestRole = codec.VerifyOpaqueLoginRequest(requestPacket);
        var responseRole = codec.VerifyOpaqueLoginSuccessResponse(responsePacket);
        var login = codec.DeserializeLoginRequest(requestPacket);
        var characterSelect = codec.DeserializeCharacterSelectRequest(unknownPacket);
        var worldEntry = codec.SerializeWorldEntryContext(new OfficialWorldEntryContextPacket(1, 0, 0, 0, new Dictionary<string, byte[]>()));

        Assert.False(codec.Coverage.IsComplete);
        Assert.Equal(11, codec.Coverage.Gaps.Count);
        Assert.True(codec.LoginCapabilities.LoginPacketRoleVerified);
        Assert.False(codec.LoginCapabilities.LoginCredentialFieldsVerified);
        Assert.True(codec.LoginCapabilities.LoginResponseRoleVerified);
        Assert.False(codec.LoginCapabilities.LoginRuntimeMutationEnabled);
        Assert.True(requestRole.Succeeded);
        Assert.Equal("LoginRequest", requestRole.Value!.CandidateRole);
        Assert.True(responseRole.Succeeded);
        Assert.Equal("LoginSuccessAndCharacterBootstrap", responseRole.Value!.CandidateRole);
        Assert.False(login.Succeeded);
        Assert.Equal("protocol.login_credential_fields_required", login.Error.Code);
        Assert.False(characterSelect.Succeeded);
        Assert.False(worldEntry.Succeeded);
        Assert.Contains(codec.Coverage.Gaps, gap => gap.PacketKind == OfficialLoginCharacterPacketKind.LoginRequest);
        Assert.Contains(codec.Coverage.Gaps, gap => gap.PacketKind == OfficialLoginCharacterPacketKind.CharacterListResponse);
        Assert.Contains(codec.Coverage.Gaps, gap => gap.PacketKind == OfficialLoginCharacterPacketKind.CharacterCreateRequest);
        Assert.Contains(codec.Coverage.Gaps, gap => gap.PacketKind == OfficialLoginCharacterPacketKind.CharacterDeleteRequest);
        Assert.Contains(codec.Coverage.Gaps, gap => gap.PacketKind == OfficialLoginCharacterPacketKind.WorldEntryContext);
    }

    [Fact]
    public void Known_login_handshake_samples_have_a_complete_19_byte_application_boundary()
    {
        var accumulator = new FrameAccumulator();
        var server = accumulator.Append(Convert.FromHexString("1300405FD0401BB55367D34D90DF1D929883DD"));
        var client = accumulator.Append(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"));

        Assert.Single(server.Frames);
        Assert.Equal(19, server.Frames[0].Bytes.Length);
        Assert.Single(client.Frames);
        Assert.Equal(19, client.Frames[0].Bytes.Length);
        Assert.Equal(0, accumulator.BufferedByteCount);
    }

    [Fact]
    public void Frame_accumulator_extracts_verified_login_request_and_success_response()
    {
        var request = ReadEvidence("login-request-208-ca4f32d2125c.bin");
        var response = ReadEvidence("login-success-bootstrap-417-04104e82b860.bin");

        var requestResult = new FrameAccumulator().Append(request);
        var responseResult = new FrameAccumulator().Append(response);

        Assert.Single(requestResult.Frames);
        Assert.Equal(208, requestResult.Frames[0].Bytes.Length);
        Assert.Single(responseResult.Frames);
        Assert.Equal(417, responseResult.Frames[0].Bytes.Length);
    }

    [Fact]
    public void Frame_accumulator_preserves_partial_and_coalesced_login_frames()
    {
        var handshake = Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC");
        var request = ReadEvidence("login-request-208-bc4eeb76f105.bin");
        var combined = handshake.Concat(request).ToArray();
        var accumulator = new FrameAccumulator();

        var partial = accumulator.Append(combined.AsSpan(0, 73));
        var completed = accumulator.Append(combined.AsSpan(73));

        Assert.Single(partial.Frames);
        Assert.Equal(19, partial.Frames[0].Bytes.Length);
        Assert.Equal(FrameReadStatus.NeedMoreData, partial.Issue!.Status);
        Assert.Single(completed.Frames);
        Assert.Equal(208, completed.Frames[0].Bytes.Length);
        Assert.Equal(0, completed.BufferedByteCount);
    }

    [Theory]
    [InlineData("208")]
    [InlineData("1,207")]
    [InlineData("2,206")]
    [InlineData("19,189")]
    [InlineData("3,5,7,11,13,17,19,23,29,31,47")]
    [InlineData("208+next")]
    public void Frame_accumulator_preserves_fragmented_208_byte_login_request(string layout)
    {
        var request = ReadEvidence("login-request-208-bc4eeb76f105.bin");
        var next = Convert.FromHexString("05007ACCEC");
        var accumulator = new FrameAccumulator(maxPacketSize: 512, maxBufferedBytes: 1024);
        var frames = new List<ProtocolFrame>();

        if (layout == "208+next")
        {
            var result = accumulator.Append(request.Concat(next).ToArray());
            frames.AddRange(result.Frames);

            Assert.True(result.Succeeded);
            Assert.Equal(2, frames.Count);
            Assert.Equal(208, frames[0].Bytes.Length);
            Assert.Equal(5, frames[1].Bytes.Length);
            Assert.Equal(0, result.BufferedByteCount);
            return;
        }

        var offset = 0;
        foreach (var chunkSize in layout.Split(',').Select(int.Parse))
        {
            var take = Math.Min(chunkSize, request.Length - offset);
            if (take <= 0)
            {
                continue;
            }

            var result = accumulator.Append(request.AsSpan(offset, take));
            frames.AddRange(result.Frames);
            offset += take;
        }

        while (offset < request.Length)
        {
            var take = Math.Min(17, request.Length - offset);
            var result = accumulator.Append(request.AsSpan(offset, take));
            frames.AddRange(result.Frames);
            offset += take;
        }

        Assert.Single(frames);
        Assert.Equal(208, frames[0].Bytes.Length);
        Assert.Equal(0, accumulator.BufferedByteCount);
    }

    [Fact]
    public void Tcp_reconstruction_deduplicates_retransmission_and_trims_overlap()
    {
        var reconstructed = TcpApplicationStreamReconstructor.Reconstruct(
        [
            new TcpPayloadSegment(1000, Convert.FromHexString("1300E10638")),
            new TcpPayloadSegment(1000, Convert.FromHexString("1300E10638")),
            new TcpPayloadSegment(1003, Convert.FromHexString("0638FA2835"))
        ]);

        Assert.Equal("1300E10638FA2835", Convert.ToHexString(reconstructed.Bytes.Span));
        Assert.Equal(1, reconstructed.DuplicateSegmentCount);
        Assert.Equal(7, reconstructed.OverlapBytesTrimmed);
        Assert.Empty(reconstructed.Gaps);
    }

    [Fact]
    public void Verified_login_evidence_hashes_and_manifest_lineage_match()
    {
        var manifestPath = RepositoryPath(
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "source-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entries = document.RootElement.GetProperty("Migrated").EnumerateArray().ToArray();
        var requestPath = RepositoryPath(
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "VerifiedRaw",
            "Login",
            "login-request-208-3399f8d051f7.bin");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(requestPath)));

        var entry = Assert.Single(
            entries,
            candidate => string.Equals(
                candidate.GetProperty("SHA256").GetString(),
                hash,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal("VERIFIED_RAW", entry.GetProperty("VerificationStatus").GetString());
        Assert.Contains("OfficialDirectLogin", entry.GetProperty("Lineage").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("unified-readonly/", entry.GetProperty("SourcePath").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Byte_exact_login_matcher_does_not_confuse_login_and_heartbeat()
    {
        var deserializer = new PacketDeserializer(ProtocolRegistry.Official);
        var login = deserializer.Read(ReadEvidence("login-request-208-d8e99afead7e.bin")).Value!;
        var heartbeat = deserializer.Read(Convert.FromHexString("05007ACCEC")).Value!;
        var matcher = new VerifiedPacketRoleMatcher();

        Assert.Equal(OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId, login.Knowledge!.Id);
        Assert.Equal("Heartbeat", heartbeat.Knowledge!.Family);
        Assert.True(matcher.Matches(login, OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId));
        Assert.False(matcher.Matches(heartbeat, OfficialLoginEvidenceCatalog.LoginRequestKnowledgeId));
        Assert.False(matcher.Matches(login, "world-heartbeat-05007ACCEC"));
    }

    [Fact]
    public void Cross_session_login_samples_only_share_the_length_offsets()
    {
        Assert.Equal([0, 1], StableOffsets(ReadEvidenceFamily("login-client-handshake-19-*.bin")));
        Assert.Equal([0, 1], StableOffsets(ReadEvidenceFamily("login-server-handshake-19-*.bin")));
        Assert.Equal([0, 1], StableOffsets(ReadEvidenceFamily("login-request-208-*.bin")));
        Assert.Equal([0, 1], StableOffsets(ReadEvidenceFamily("login-success-bootstrap-417-*.bin")));
    }

    [Fact]
    public void Unverified_or_correlated_frames_cannot_pass_the_login_role_gate_or_generate_a_response()
    {
        var codec = new EvidenceGatedOfficialLoginCharacterProtocolCodec();
        var correlatedHandshake = new PacketDeserializer(ProtocolRegistry.Official)
            .Read(Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC"))
            .Value!;

        var role = codec.VerifyOpaqueLoginRequest(correlatedHandshake);
        var response = codec.SerializeLoginResponse(new OfficialLoginResponsePacket(
            "Success",
            new Dictionary<string, byte[]>()));

        Assert.False(role.Succeeded);
        Assert.Equal("protocol.login_packet_role_unverified", role.Error.Code);
        Assert.False(response.Succeeded);
        Assert.Equal("protocol.login_runtime_mutation_disabled", response.Error.Code);
        Assert.Empty(response.Value.ToArray());
    }

    [Fact]
    public void Frame_accumulator_buffers_partial_packets()
    {
        var accumulator = new FrameAccumulator();

        var first = accumulator.Append(Convert.FromHexString("05007A"));
        var second = accumulator.Append(Convert.FromHexString("CCEC"));

        Assert.True(first.Succeeded);
        Assert.Empty(first.Frames);
        Assert.Equal(FrameReadStatus.NeedMoreData, first.Issue!.Status);

        Assert.True(second.Succeeded);
        Assert.Single(second.Frames);
        Assert.Equal("05007ACCEC", Convert.ToHexString(second.Frames[0].Bytes.Span));
    }

    [Fact]
    public void Frame_accumulator_returns_multiple_packets_from_one_receive()
    {
        var accumulator = new FrameAccumulator();

        var result = accumulator.Append(Convert.FromHexString("05007ACCEC05003D09A5"));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal("05007ACCEC", Convert.ToHexString(result.Frames[0].Bytes.Span));
        Assert.Equal("05003D09A5", Convert.ToHexString(result.Frames[1].Bytes.Span));
    }

    [Fact]
    public void Frame_accumulator_rejects_invalid_and_oversized_lengths()
    {
        var invalid = new FrameAccumulator().Append(Convert.FromHexString("0100"));
        var oversized = new FrameAccumulator(maxPacketSize: 8).Append(Convert.FromHexString("090000000000000000"));

        Assert.False(invalid.Succeeded);
        Assert.Equal(FrameReadStatus.InvalidFrame, invalid.Issue!.Status);

        Assert.False(oversized.Succeeded);
        Assert.Equal(FrameReadStatus.OversizedFrame, oversized.Issue!.Status);
    }

    [Fact]
    public void Packet_sequence_validator_rejects_replay_candidate()
    {
        var validator = new MonotonicPacketSequenceValidator();
        var first = new PacketEnvelope(new PacketHeader(new Opcode(1), 2, 10, "0001"), ReadOnlyMemory<byte>.Empty, ProtocolConfidence.Verified);
        var replay = first with { Header = first.Header with { Sequence = 10 } };

        Assert.True(validator.Validate(first).Succeeded);
        Assert.False(validator.Validate(replay).Succeeded);
    }

    private static byte[] ReadEvidence(string fileName) =>
        File.ReadAllBytes(RepositoryPath(
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "VerifiedRaw",
            "Login",
            fileName));

    private static IReadOnlyList<byte[]> ReadEvidenceFamily(string pattern)
    {
        var root = RepositoryPath(
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "VerifiedRaw",
            "Login");
        return Directory.GetFiles(root, pattern).OrderBy(path => path, StringComparer.Ordinal).Select(File.ReadAllBytes).ToArray();
    }

    private static int[] StableOffsets(IReadOnlyList<byte[]> samples) =>
        Enumerable.Range(0, samples.Min(sample => sample.Length))
            .Where(offset => samples.All(sample => sample[offset] == samples[0][offset]))
            .ToArray();

    private static string RepositoryPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine([current.FullName, .. parts]);
    }

    private sealed class UnknownHandler : IPacketHandler
    {
        public Opcode Opcode => new(1);

        public ProtocolConfidence Confidence => ProtocolConfidence.Unknown;

        public Task<God2.ClassicServer.Application.Common.OperationResult> HandleAsync(PacketEnvelope packet, CancellationToken cancellationToken)
        {
            return Task.FromResult(God2.ClassicServer.Application.Common.OperationResult.Success);
        }
    }
}
