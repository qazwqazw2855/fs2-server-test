using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class GeneralizedGameplayProtocolTests
{
    private const string Build = EvidenceGatedGameplayCommandDecoder.CurrentBuildId;

    [Fact]
    public void Current_evidence_blocked_decoder_cannot_create_runtime_mutation()
    {
        var descriptor = GeneralizedGameplayCatalog.EvidenceBlocked(
            "battle-command", GameplayCommandFamily.Skill, PacketDirection.ClientToServer, GameplayProtocolState.Battle);
        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(actorId: 1));

        Assert.Equal(GeneralizedProtocolResultCode.EvidenceBlocked, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Build_lock_precedes_decode_promotion()
    {
        var descriptor = VerifiedDecoder();
        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            "another-build", GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(actorId: 1));

        Assert.Equal(GeneralizedProtocolResultCode.BuildMismatch, result.Code);
    }

    [Fact]
    public void State_gate_blocks_world_use_of_battle_command()
    {
        var descriptor = VerifiedDecoder();
        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.World, descriptor.FamilyId, 20, Fields(actorId: 1));

        Assert.Equal(GeneralizedProtocolResultCode.InvalidState, result.Code);
    }

    [Fact]
    public void Fixed_length_gate_rejects_mismatch()
    {
        var descriptor = VerifiedDecoder(allowsVariableLength: false, allowedDecodedLengths: new HashSet<int> { 20 });
        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 19, Fields(actorId: 1));

        Assert.Equal(GeneralizedProtocolResultCode.InvalidLength, result.Code);
    }

    [Fact]
    public void Decoder_requires_actor()
    {
        var descriptor = VerifiedDecoder();
        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(actorId: 0));

        Assert.Equal(GeneralizedProtocolResultCode.Malformed, result.Code);
    }

    [Fact]
    public void Generic_skill_decoder_maps_identifiers_without_per_skill_handler()
    {
        var descriptor = VerifiedDecoder();
        var fields = new DecodedProtocolFields(
            new Dictionary<string, long>
            {
                ["actorId"] = 7,
                ["targetId"] = 9,
                ["skillId"] = 12001,
                ["round"] = 3,
                ["commandWindow"] = 88,
                ["flags"] = 4
            },
            new Dictionary<int, ReadOnlyMemory<byte>>());

        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, fields);

        Assert.True(result.Succeeded);
        Assert.Equal(GameplayCommandFamily.Skill, result.Value!.Family);
        Assert.Equal(12001, result.Value.SkillId);
        Assert.Equal(9, result.Value.TargetId);
        Assert.Equal(3, result.Value.Round);
    }

    [Fact]
    public void Unknown_fields_are_copied_and_hash_is_stable()
    {
        var descriptor = VerifiedDecoder();
        var source = new byte[] { 0x10, 0x20, 0x30 };
        var fields = new DecodedProtocolFields(
            new Dictionary<string, long>
            {
                ["actorId"] = 7,
                ["skillId"] = 1,
                ["round"] = 1,
                ["commandWindow"] = 1
            },
            new Dictionary<int, ReadOnlyMemory<byte>> { [12] = source });
        var decoder = new EvidenceGatedGameplayCommandDecoder([descriptor]);

        var first = decoder.Decode(Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, fields).Value!;
        source[0] = 0xFF;
        var replay = decoder.Decode(Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20,
            new DecodedProtocolFields(new Dictionary<string, long>
            {
                ["actorId"] = 7,
                ["skillId"] = 1,
                ["round"] = 1,
                ["commandWindow"] = 1
            },
                new Dictionary<int, ReadOnlyMemory<byte>> { [12] = new byte[] { 0x10, 0x20, 0x30 } })).Value!;

        Assert.Equal("102030", Convert.ToHexString(first.PreservedUnknownFields[12].AsSpan()));
        Assert.Equal(first.SourceHash, replay.SourceHash);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<int, System.Collections.Immutable.ImmutableArray<byte>>)first.PreservedUnknownFields)
                .Add(13, System.Collections.Immutable.ImmutableArray.Create<byte>(1)));
    }

    [Fact]
    public void Decoder_rejects_overlapping_unknown_regions()
    {
        var descriptor = VerifiedDecoder();
        var known = Fields(7).KnownNumericFields;
        var fields = new DecodedProtocolFields(
            known,
            new Dictionary<int, ReadOnlyMemory<byte>>
            {
                [10] = new byte[] { 1, 2, 3 },
                [12] = new byte[] { 4, 5 }
            });

        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, fields);

        Assert.Equal(GeneralizedProtocolResultCode.Malformed, result.Code);
        Assert.Equal("protocol.generalized.unknown_fields_overlap", result.FailureCode);
    }

    [Fact]
    public void Command_hash_changes_when_flags_change()
    {
        var descriptor = VerifiedDecoder();
        var decoder = new EvidenceGatedGameplayCommandDecoder([descriptor]);

        var first = decoder.Decode(Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(7, flags: 1)).Value!;
        var changed = decoder.Decode(Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(7, flags: 2)).Value!;

        Assert.NotEqual(first.SourceHash, changed.SourceHash);
    }

    [Fact]
    public void Decoder_rejects_server_to_client_descriptor()
    {
        var descriptor = VerifiedDecoder(direction: PacketDirection.ServerToClient);

        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, Fields(7));

        Assert.Equal(GeneralizedProtocolResultCode.InvalidDirection, result.Code);
    }

    [Fact]
    public void Skill_decoder_requires_skill_and_battle_timing_fields()
    {
        var descriptor = VerifiedDecoder();
        var fields = new DecodedProtocolFields(
            new Dictionary<string, long> { ["actorId"] = 7 },
            new Dictionary<int, ReadOnlyMemory<byte>>());

        var result = new EvidenceGatedGameplayCommandDecoder([descriptor]).Decode(
            Build, GameplayProtocolState.Battle, descriptor.FamilyId, 20, fields);

        Assert.Equal(GeneralizedProtocolResultCode.Malformed, result.Code);
    }

    [Fact]
    public void Serializer_rejects_captured_or_fixed_identifiers()
    {
        var serializer = new EvidenceGatedAuthoritativeResultSerializer([GeneralizedGameplayCatalog.EvidenceBlocked(
            "battle-result", GameplayCommandFamily.Skill, PacketDirection.ServerToClient, GameplayProtocolState.Battle)]);
        var result = serializer.Serialize(GameplayProtocolState.Battle, Result(captured: "capture-1"));

        Assert.Equal(GeneralizedProtocolResultCode.FixedIdentifierRejected, result.Code);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public void Serializer_fails_closed_until_automated_client_acceptance()
    {
        var serializer = new EvidenceGatedAuthoritativeResultSerializer([GeneralizedGameplayCatalog.EvidenceBlocked(
            "battle-result", GameplayCommandFamily.Skill, PacketDirection.ServerToClient, GameplayProtocolState.Battle)]);
        var result = serializer.Serialize(GameplayProtocolState.Battle, Result());

        Assert.Equal(GeneralizedProtocolResultCode.SerializerNotVerified, result.Code);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public void All_skill_effect_families_share_one_generic_catalog()
    {
        Assert.Equal(20, GeneralizedGameplayCatalog.SkillFamilies.Count);
        Assert.Equal(20, GeneralizedGameplayCatalog.SkillFamilies.Distinct().Count());
    }

    private static ProtocolFamilyDescriptor VerifiedDecoder(
        bool allowsVariableLength = true,
        IReadOnlySet<int>? allowedDecodedLengths = null,
        PacketDirection direction = PacketDirection.ClientToServer) => new(
        "battle-command",
        GameplayCommandFamily.Skill,
        direction,
        new HashSet<GameplayProtocolState> { GameplayProtocolState.Battle },
        allowedDecodedLengths ?? new HashSet<int> { 20 },
        allowsVariableLength: allowsVariableLength,
        decoderVerified: true,
        serializerVerified: false,
        "test-only-semantic-fixture",
        new string('A', 64));

    private static DecodedProtocolFields Fields(long actorId, int flags = 0) => new(
        new Dictionary<string, long>
        {
            ["actorId"] = actorId,
            ["skillId"] = 1,
            ["round"] = 1,
            ["commandWindow"] = 1,
            ["flags"] = flags
        },
        new Dictionary<int, ReadOnlyMemory<byte>>());

    private static AuthoritativeGameplayResult Result(string? captured = null) => new(
        Build,
        "battle-result",
        "Success",
        new Dictionary<string, long> { ["actorId"] = 1 },
        new Dictionary<int, ReadOnlyMemory<byte>>(),
        captured,
        null);
}
