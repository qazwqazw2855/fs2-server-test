namespace God2.ClassicServer.Protocol;

public enum BattleCommandActionTypeCandidate
{
    Unknown,
    BasicAttack,
    Skill,
    Item,
    Defend,
    Flee
}

public sealed record BattleCommandCandidate(
    string ClientBuildId,
    string SessionSafeReference,
    long SessionEpoch,
    long ProtocolSequence,
    string? BattleReferenceCandidate,
    string? ActingParticipantReferenceCandidate,
    IReadOnlyList<string> TargetReferenceCandidates,
    BattleCommandActionTypeCandidate ActionTypeCandidate,
    int? RoundCandidate,
    long? CommandWindowCandidate,
    long? ClientSequenceCandidate,
    string RawPayloadHash,
    BattleEvidenceConfidence EvidenceConfidence,
    IReadOnlyDictionary<int, string> UnknownFields,
    DateTimeOffset ReceivedAtUtc);

public sealed record BattlePacketDecodeContext(
    string ClientBuildId,
    string ConnectionSafeReference,
    string SessionSafeReference,
    long SessionEpoch,
    OfficialBattleProtocolState ProtocolState,
    long ProtocolSequence);

public sealed record BattlePacketDecodeResult(
    BattleProtocolAdapterResultCode Code,
    BattleCommandCandidate? Candidate,
    string FailureCode)
{
    public bool Succeeded => Code == BattleProtocolAdapterResultCode.Success && Candidate is not null;
}

public sealed record BattleSemanticPacketDto(
    BattleProtocolPacketFamily Family,
    string ClientBuildId,
    long EventSequence,
    IReadOnlyDictionary<string, string> VerifiedSemanticFields,
    IReadOnlyDictionary<string, BattleUnknownFieldPolicy> UnknownFieldPolicies,
    string SemanticSourceHash);

public sealed record BattlePacketSerializeContext(
    string ClientBuildId,
    string ConnectionSafeReference,
    long SessionEpoch,
    OfficialBattleProtocolState ProtocolState,
    long ProtocolSequence);

public sealed record BattlePacketSerializeResult(
    BattleProtocolAdapterResultCode Code,
    ReadOnlyMemory<byte> Bytes,
    string BytesHash,
    string FailureCode)
{
    public bool Succeeded => Code == BattleProtocolAdapterResultCode.Success && !Bytes.IsEmpty;
}

public interface IBattlePacketDecoder
{
    BattleProtocolPacketFamily Family { get; }

    string ClientBuildId { get; }

    BattlePacketDecodeResult Decode(
        BattlePacketDecodeContext context,
        ReadOnlyMemory<byte> frame);
}

public interface IBattlePacketSerializer
{
    BattleProtocolPacketFamily Family { get; }

    string ClientBuildId { get; }

    BattlePacketSerializeResult Serialize(
        BattlePacketSerializeContext context,
        BattleSemanticPacketDto packet);
}

public interface IBattlePacketValidator
{
    BattleProtocolAdapterResultCode ValidateInbound(
        BattlePacketDecodeContext context,
        BattleProtocolPacketFamily family,
        ReadOnlyMemory<byte> frame,
        out string failureCode);

    BattleProtocolAdapterResultCode ValidateOutbound(
        BattlePacketSerializeContext context,
        BattleSemanticPacketDto packet,
        out string failureCode);
}

public interface IGod2BattleProtocolAdapter
{
    BattlePacketDecodeResult DecodeInbound(
        BattleProtocolPacketFamily family,
        BattlePacketDecodeContext context,
        ReadOnlyMemory<byte> frame);

    BattlePacketSerializeResult SerializeOutbound(
        BattlePacketSerializeContext context,
        BattleSemanticPacketDto packet);
}

public sealed class BattlePacketValidator : IBattlePacketValidator
{
    private const int MaximumBattlePacketBytes = 64 * 1024;

    public BattleProtocolAdapterResultCode ValidateInbound(
        BattlePacketDecodeContext context,
        BattleProtocolPacketFamily family,
        ReadOnlyMemory<byte> frame,
        out string failureCode)
    {
        if (string.IsNullOrWhiteSpace(context.ClientBuildId) ||
            string.IsNullOrWhiteSpace(context.ConnectionSafeReference) ||
            string.IsNullOrWhiteSpace(context.SessionSafeReference) ||
            context.SessionEpoch < 0)
        {
            failureCode = "battle.protocol.inbound_context_invalid";
            return BattleProtocolAdapterResultCode.InvalidSession;
        }

        if (frame.Length is < 2 or > MaximumBattlePacketBytes)
        {
            failureCode = "battle.protocol.inbound_length_invalid";
            return BattleProtocolAdapterResultCode.InvalidLength;
        }

        if (family == BattleProtocolPacketFamily.BasicAttackClientCommand &&
            context.ProtocolState != OfficialBattleProtocolState.CommandWindowOpen)
        {
            failureCode = "battle.protocol.basic_attack_wrong_state";
            return BattleProtocolAdapterResultCode.InvalidState;
        }

        failureCode = string.Empty;
        return BattleProtocolAdapterResultCode.Success;
    }

    public BattleProtocolAdapterResultCode ValidateOutbound(
        BattlePacketSerializeContext context,
        BattleSemanticPacketDto packet,
        out string failureCode)
    {
        if (string.IsNullOrWhiteSpace(context.ClientBuildId) ||
            !string.Equals(context.ClientBuildId, packet.ClientBuildId, StringComparison.Ordinal))
        {
            failureCode = "battle.protocol.outbound_client_build_mismatch";
            return BattleProtocolAdapterResultCode.UnsupportedClientBuild;
        }

        if (packet.UnknownFieldPolicies.Values.Any(policy => policy == BattleUnknownFieldPolicy.EvidenceBlocked))
        {
            failureCode = "battle.protocol.outbound_unknown_field_blocked";
            return BattleProtocolAdapterResultCode.SerializerBlockedByEvidence;
        }

        failureCode = string.Empty;
        return BattleProtocolAdapterResultCode.Success;
    }
}

public sealed class EvidenceGatedGod2BattleProtocolAdapter : IGod2BattleProtocolAdapter
{
    private readonly IBattleProtocolGate _gate;
    private readonly IBattlePacketValidator _validator;
    private readonly IReadOnlyDictionary<BattleProtocolPacketFamily, IBattlePacketDecoder> _decoders;
    private readonly IReadOnlyDictionary<BattleProtocolPacketFamily, IBattlePacketSerializer> _serializers;

    public EvidenceGatedGod2BattleProtocolAdapter(
        IBattleProtocolGate gate,
        IEnumerable<IBattlePacketDecoder>? decoders = null,
        IEnumerable<IBattlePacketSerializer>? serializers = null,
        IBattlePacketValidator? validator = null)
    {
        _gate = gate;
        _validator = validator ?? new BattlePacketValidator();
        _decoders = BuildUnique(decoders ?? [], decoder => decoder.Family, nameof(decoders));
        _serializers = BuildUnique(serializers ?? [], serializer => serializer.Family, nameof(serializers));
    }

    public BattlePacketDecodeResult DecodeInbound(
        BattleProtocolPacketFamily family,
        BattlePacketDecodeContext context,
        ReadOnlyMemory<byte> frame)
    {
        var validation = _validator.ValidateInbound(context, family, frame, out var validationFailure);
        if (validation != BattleProtocolAdapterResultCode.Success)
        {
            return new BattlePacketDecodeResult(validation, null, validationFailure);
        }

        var gate = _gate.CanDecode(family, context.ClientBuildId);
        if (!gate.Allowed)
        {
            return new BattlePacketDecodeResult(gate.ResultCode, null, gate.FailureCode);
        }

        if (!_decoders.TryGetValue(family, out var decoder))
        {
            return new BattlePacketDecodeResult(
                BattleProtocolAdapterResultCode.UnsupportedPacket,
                null,
                "battle.protocol.decoder_not_registered");
        }

        if (!string.Equals(decoder.ClientBuildId, context.ClientBuildId, StringComparison.Ordinal))
        {
            return new BattlePacketDecodeResult(
                BattleProtocolAdapterResultCode.UnsupportedClientBuild,
                null,
                "battle.protocol.decoder_client_build_mismatch");
        }

        try
        {
            return decoder.Decode(context, frame);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new BattlePacketDecodeResult(
                BattleProtocolAdapterResultCode.DecoderFailure,
                null,
                $"battle.protocol.decoder_failure:{exception.GetType().Name}");
        }
    }

    public BattlePacketSerializeResult SerializeOutbound(
        BattlePacketSerializeContext context,
        BattleSemanticPacketDto packet)
    {
        var validation = _validator.ValidateOutbound(context, packet, out var validationFailure);
        if (validation != BattleProtocolAdapterResultCode.Success)
        {
            return BlockedSerialize(validation, validationFailure);
        }

        var gate = _gate.CanSerialize(packet.Family, context.ClientBuildId);
        if (!gate.Allowed)
        {
            return BlockedSerialize(gate.ResultCode, gate.FailureCode);
        }

        if (!_serializers.TryGetValue(packet.Family, out var serializer))
        {
            return BlockedSerialize(
                BattleProtocolAdapterResultCode.UnsupportedPacket,
                "battle.protocol.serializer_not_registered");
        }

        if (!string.Equals(serializer.ClientBuildId, context.ClientBuildId, StringComparison.Ordinal))
        {
            return BlockedSerialize(
                BattleProtocolAdapterResultCode.UnsupportedClientBuild,
                "battle.protocol.serializer_client_build_mismatch");
        }

        try
        {
            var result = serializer.Serialize(context, packet);
            if (result.Code != BattleProtocolAdapterResultCode.Success && !result.Bytes.IsEmpty)
            {
                return BlockedSerialize(
                    BattleProtocolAdapterResultCode.SerializerFailure,
                    "battle.protocol.serializer_emitted_bytes_on_failure");
            }

            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return BlockedSerialize(
                BattleProtocolAdapterResultCode.SerializerFailure,
                $"battle.protocol.serializer_failure:{exception.GetType().Name}");
        }
    }

    private static IReadOnlyDictionary<BattleProtocolPacketFamily, T> BuildUnique<T>(
        IEnumerable<T> source,
        Func<T, BattleProtocolPacketFamily> family,
        string parameterName)
    {
        var items = source.ToArray();
        if (items.Select(family).Distinct().Count() != items.Length)
        {
            throw new ArgumentException("Only one implementation may be registered for a packet family.", parameterName);
        }

        return items.ToDictionary(family);
    }

    private static BattlePacketSerializeResult BlockedSerialize(
        BattleProtocolAdapterResultCode code,
        string failureCode) =>
        new(code, ReadOnlyMemory<byte>.Empty, string.Empty, failureCode);
}
