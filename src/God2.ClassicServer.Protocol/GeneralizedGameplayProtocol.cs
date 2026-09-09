using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace God2.ClassicServer.Protocol;

public enum GameplayProtocolState
{
    Login,
    CharacterList,
    World,
    Battle,
    Reconnecting
}

public enum GameplayCommandFamily
{
    Unknown,
    BasicAttack,
    Skill,
    Item,
    Defend,
    Flee,
    Formation,
    PositionSwap,
    Inventory,
    Equipment,
    Quest,
    Party,
    Mount,
    Pet,
    Shop,
    Crafting,
    Portal
}

public enum GeneralizedProtocolResultCode
{
    Success,
    EvidenceBlocked,
    BuildMismatch,
    InvalidState,
    InvalidDirection,
    InvalidLength,
    Malformed,
    FixedIdentifierRejected,
    SerializerNotVerified
}

public enum SkillEffectFamily
{
    DirectDamage,
    MultiTargetDamage,
    Heal,
    Buff,
    Debuff,
    DamageOverTime,
    HealOverTime,
    Shield,
    Dispel,
    Revive,
    Drain,
    Stun,
    Silence,
    Taunt,
    Summon,
    Special,
    CooldownManipulation,
    ResourceRestore,
    Cleanse,
    Reflect
}

public sealed class ProtocolFamilyDescriptor
{
    internal ProtocolFamilyDescriptor(
        string familyId,
        GameplayCommandFamily family,
        PacketDirection direction,
        IEnumerable<GameplayProtocolState> allowedStates,
        IEnumerable<int> allowedDecodedLengths,
        bool allowsVariableLength,
        bool decoderVerified,
        bool serializerVerified,
        string evidenceId,
        string evidenceSha256 = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        ArgumentNullException.ThrowIfNull(allowedStates);
        ArgumentNullException.ThrowIfNull(allowedDecodedLengths);
        if (family == GameplayCommandFamily.Unknown)
        {
            throw new ArgumentException("A protocol descriptor cannot promote the unknown family.", nameof(family));
        }
        if (direction == PacketDirection.Unknown)
        {
            throw new ArgumentException("A protocol family must have an authoritative direction.", nameof(direction));
        }
        if ((decoderVerified || serializerVerified) &&
            (evidenceSha256.Length != 64 || evidenceSha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("Verified codecs require a SHA-256 evidence digest.", nameof(evidenceSha256));
        }

        var states = allowedStates.ToFrozenSet();
        var lengths = allowedDecodedLengths.ToFrozenSet();
        if (states.Count == 0 || lengths.Any(length => length < 2) || (!allowsVariableLength && lengths.Count == 0))
        {
            throw new ArgumentException("Protocol descriptor states or decoded lengths are invalid.");
        }

        FamilyId = familyId;
        Family = family;
        Direction = direction;
        AllowedStates = states;
        AllowedDecodedLengths = lengths;
        AllowsVariableLength = allowsVariableLength;
        DecoderVerified = decoderVerified;
        SerializerVerified = serializerVerified;
        EvidenceId = evidenceId;
        EvidenceSha256 = evidenceSha256.ToUpperInvariant();
    }

    public string FamilyId { get; }
    public GameplayCommandFamily Family { get; }
    public PacketDirection Direction { get; }
    public IReadOnlySet<GameplayProtocolState> AllowedStates { get; }
    public IReadOnlySet<int> AllowedDecodedLengths { get; }
    public bool AllowsVariableLength { get; }
    public bool DecoderVerified { get; }
    public bool SerializerVerified { get; }
    public string EvidenceId { get; }
    public string EvidenceSha256 { get; }
}

public sealed record DecodedProtocolFields(
    IReadOnlyDictionary<string, long> KnownNumericFields,
    IReadOnlyDictionary<int, ReadOnlyMemory<byte>> UnknownFields);

public sealed record GeneralizedGameplayCommand(
    string ClientBuildId,
    string FamilyId,
    GameplayCommandFamily Family,
    long ActorId,
    long? TargetId,
    long? SkillId,
    long? ItemId,
    int? Round,
    long? CommandWindow,
    int Flags,
    IReadOnlyDictionary<int, ImmutableArray<byte>> PreservedUnknownFields,
    string SourceHash);

public sealed record AuthoritativeGameplayResult(
    string ClientBuildId,
    string FamilyId,
    string ResultCode,
    IReadOnlyDictionary<string, long> AuthoritativeFields,
    IReadOnlyDictionary<int, ReadOnlyMemory<byte>> PreservedUnknownFields,
    string? CapturedPacketIdentifier,
    string? FixedResponseIdentifier);

public sealed record GeneralizedProtocolResult<T>(
    GeneralizedProtocolResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == GeneralizedProtocolResultCode.Success;

    public static GeneralizedProtocolResult<T> Success(T value) => new(GeneralizedProtocolResultCode.Success, value, string.Empty);

    public static GeneralizedProtocolResult<T> Failure(GeneralizedProtocolResultCode code, string failureCode) => new(code, default, failureCode);
}

public sealed class EvidenceGatedGameplayCommandDecoder
{
    public const string CurrentBuildId = "god2-opt-6b127086e0c0";
    private readonly IReadOnlyDictionary<string, ProtocolFamilyDescriptor> _descriptors;

    public EvidenceGatedGameplayCommandDecoder(IEnumerable<ProtocolFamilyDescriptor> descriptors)
    {
        _descriptors = descriptors.ToDictionary(item => item.FamilyId, StringComparer.Ordinal);
    }

    public GeneralizedProtocolResult<GeneralizedGameplayCommand> Decode(
        string clientBuildId,
        GameplayProtocolState state,
        string familyId,
        int decodedLength,
        DecodedProtocolFields fields)
    {
        if (string.IsNullOrWhiteSpace(familyId))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.EvidenceBlocked,
                "protocol.generalized.family_required");
        }
        if (!string.Equals(clientBuildId, CurrentBuildId, StringComparison.Ordinal))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.BuildMismatch,
                "protocol.generalized.client_build_mismatch");
        }

        if (!_descriptors.TryGetValue(familyId, out var descriptor) || !descriptor.DecoderVerified)
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.EvidenceBlocked,
                "protocol.generalized.decoder_evidence_required");
        }

        if (descriptor.Direction != PacketDirection.ClientToServer)
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.InvalidDirection,
                "protocol.generalized.decoder_direction_invalid");
        }

        if (!descriptor.AllowedStates.Contains(state))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.InvalidState,
                "protocol.generalized.state_invalid");
        }

        if (decodedLength < 2 || (!descriptor.AllowsVariableLength && !descriptor.AllowedDecodedLengths.Contains(decodedLength)))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.InvalidLength,
                "protocol.generalized.decoded_length_invalid");
        }

        if (fields is null || fields.KnownNumericFields is null || fields.UnknownFields is null ||
            fields.KnownNumericFields.Keys.Any(string.IsNullOrWhiteSpace) ||
            !fields.KnownNumericFields.TryGetValue("actorId", out var actorId) || actorId <= 0)
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.Malformed,
                "protocol.generalized.actor_required");
        }
        if (fields.UnknownFields.Any(item =>
            item.Key < 0 || item.Key > decodedLength || item.Value.Length > decodedLength - item.Key))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.Malformed,
                "protocol.generalized.unknown_field_range_invalid");
        }
        var unknownRanges = fields.UnknownFields.OrderBy(item => item.Key).ToArray();
        for (var index = 1; index < unknownRanges.Length; index++)
        {
            var previousEnd = checked(unknownRanges[index - 1].Key + unknownRanges[index - 1].Value.Length);
            if (previousEnd > unknownRanges[index].Key)
            {
                return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                    GeneralizedProtocolResultCode.Malformed,
                    "protocol.generalized.unknown_fields_overlap");
            }
        }

        long? Field(string name) => fields.KnownNumericFields.TryGetValue(name, out var value) ? value : null;
        var targetId = Field("targetId");
        var skillId = Field("skillId");
        var itemId = Field("itemId");
        var round = Field("round");
        var commandWindow = Field("commandWindow");
        var flags = Field("flags") ?? 0;
        if (targetId is <= 0 || skillId is <= 0 || itemId is <= 0 ||
            round is < 0 or > int.MaxValue || commandWindow is <= 0 ||
            flags is < int.MinValue or > int.MaxValue ||
            !HasRequiredFamilyFields(descriptor.Family, skillId, itemId, round, commandWindow))
        {
            return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Failure(
                GeneralizedProtocolResultCode.Malformed,
                "protocol.generalized.family_fields_invalid");
        }

        var unknown = fields.UnknownFields.ToFrozenDictionary(
            item => item.Key,
            item => ImmutableArray.Create(item.Value.ToArray()));
        var hash = HashCommand(clientBuildId, state, descriptor, decodedLength, fields.KnownNumericFields, unknown);
        return GeneralizedProtocolResult<GeneralizedGameplayCommand>.Success(new GeneralizedGameplayCommand(
            clientBuildId,
            familyId,
            descriptor.Family,
            actorId,
            targetId,
            skillId,
            itemId,
            round is null ? null : (int)round.Value,
            commandWindow,
            (int)flags,
            unknown,
            hash));
    }

    private static string HashCommand(
        string clientBuildId,
        GameplayProtocolState state,
        ProtocolFamilyDescriptor descriptor,
        int decodedLength,
        IReadOnlyDictionary<string, long> known,
        IReadOnlyDictionary<int, ImmutableArray<byte>> unknown)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(clientBuildId);
        AppendString(state.ToString());
        AppendString(descriptor.FamilyId);
        AppendString(descriptor.EvidenceSha256);
        AppendString(decodedLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendString("known");
        AppendString(known.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var item in known.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendString("key");
            AppendString(item.Key);
            AppendString("value");
            AppendString(item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        AppendString("unknown");
        AppendString(unknown.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var item in unknown.OrderBy(item => item.Key))
        {
            AppendString("offset");
            AppendString(item.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendString("bytes");
            AppendBytes(item.Value.AsSpan());
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        void AppendString(string value) => AppendBytes(Encoding.UTF8.GetBytes(value));
        void AppendBytes(ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
    }

    private static bool HasRequiredFamilyFields(
        GameplayCommandFamily family,
        long? skillId,
        long? itemId,
        long? round,
        long? commandWindow)
    {
        var battleTimingValid = round is not null && commandWindow is not null;
        return family switch
        {
            GameplayCommandFamily.Skill => battleTimingValid && skillId is not null,
            GameplayCommandFamily.Item => battleTimingValid && itemId is not null,
            GameplayCommandFamily.BasicAttack or GameplayCommandFamily.Defend or GameplayCommandFamily.Flee or
                GameplayCommandFamily.Formation or GameplayCommandFamily.PositionSwap => battleTimingValid,
            _ => true
        };
    }
}

public sealed class EvidenceGatedAuthoritativeResultSerializer
{
    private readonly IReadOnlyDictionary<string, ProtocolFamilyDescriptor> _descriptors;

    public EvidenceGatedAuthoritativeResultSerializer(IEnumerable<ProtocolFamilyDescriptor> descriptors)
    {
        _descriptors = descriptors.ToDictionary(item => item.FamilyId, StringComparer.Ordinal);
    }

    public GeneralizedProtocolResult<ReadOnlyMemory<byte>> Serialize(
        GameplayProtocolState state,
        AuthoritativeGameplayResult result)
    {
        if (!string.Equals(result.ClientBuildId, EvidenceGatedGameplayCommandDecoder.CurrentBuildId, StringComparison.Ordinal))
        {
            return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
                GeneralizedProtocolResultCode.BuildMismatch,
                "protocol.generalized.client_build_mismatch");
        }

        if (!string.IsNullOrWhiteSpace(result.CapturedPacketIdentifier) || !string.IsNullOrWhiteSpace(result.FixedResponseIdentifier))
        {
            return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
                GeneralizedProtocolResultCode.FixedIdentifierRejected,
                "protocol.generalized.fixed_or_captured_identifier_rejected");
        }

        if (!_descriptors.TryGetValue(result.FamilyId, out var descriptor) || !descriptor.SerializerVerified)
        {
            return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
                GeneralizedProtocolResultCode.SerializerNotVerified,
                "protocol.generalized.serializer_client_acceptance_required");
        }

        if (descriptor.Direction != PacketDirection.ServerToClient)
        {
            return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
                GeneralizedProtocolResultCode.InvalidDirection,
                "protocol.generalized.serializer_direction_invalid");
        }

        if (!descriptor.AllowedStates.Contains(state))
        {
            return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
                GeneralizedProtocolResultCode.InvalidState,
                "protocol.generalized.state_invalid");
        }

        // The current build intentionally has no promoted serializer. When one is promoted,
        // a build-locked writer must replace this fail-closed boundary.
        return GeneralizedProtocolResult<ReadOnlyMemory<byte>>.Failure(
            GeneralizedProtocolResultCode.SerializerNotVerified,
            "protocol.generalized.writer_not_installed");
    }
}

public static class GeneralizedGameplayCatalog
{
    public static IReadOnlyList<SkillEffectFamily> SkillFamilies { get; } = Enum.GetValues<SkillEffectFamily>();

    public static ProtocolFamilyDescriptor EvidenceBlocked(
        string familyId,
        GameplayCommandFamily family,
        PacketDirection direction,
        params GameplayProtocolState[] states) =>
        new ProtocolFamilyDescriptor(
            familyId,
            family,
            direction,
            states.ToHashSet(),
            new HashSet<int>(),
            allowsVariableLength: true,
            decoderVerified: false,
            serializerVerified: false,
            "protocol/evidence/current-build/client-dispatch-registry.json");
}
