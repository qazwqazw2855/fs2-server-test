using System.Globalization;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public sealed record BattleProtocolRuntimeContext(
    string ClientBuildId,
    string SessionSafeReference,
    long SessionEpoch,
    Guid ActiveBattleId,
    string ActiveBattleClientReference,
    long CharacterId,
    Guid ActingParticipantId,
    IReadOnlyDictionary<string, Guid> ClientParticipantReferences,
    int RoundNumber,
    long CommandWindowVersion,
    long ExpectedBattleVersion,
    int ActionSlot,
    Guid ServerCommandId,
    string IdempotencyKey,
    DateTimeOffset SubmittedAtUtc,
    string CorrelationId,
    OfficialBattleProtocolState ProtocolState);

public sealed record BattleProtocolCommandMappingResult(
    BattleProtocolAdapterResultCode Code,
    BattleActorCommandEnvelope? Command,
    string FailureCode)
{
    public bool Succeeded => Code == BattleProtocolAdapterResultCode.Success && Command is not null;
}

public sealed class BattleProtocolCommandMapper
{
    public BattleProtocolCommandMappingResult MapBasicAttack(
        BattleCommandCandidate candidate,
        BattleProtocolRuntimeContext context)
    {
        if (!string.Equals(candidate.ClientBuildId, context.ClientBuildId, StringComparison.Ordinal))
        {
            return Failed(BattleProtocolAdapterResultCode.UnsupportedClientBuild, "battle.protocol.mapping_client_build_mismatch");
        }

        if (!string.Equals(candidate.SessionSafeReference, context.SessionSafeReference, StringComparison.Ordinal) ||
            candidate.SessionEpoch != context.SessionEpoch)
        {
            return Failed(BattleProtocolAdapterResultCode.InvalidSession, "battle.protocol.mapping_session_mismatch");
        }

        if (context.ProtocolState != OfficialBattleProtocolState.CommandWindowOpen)
        {
            return Failed(BattleProtocolAdapterResultCode.InvalidState, "battle.protocol.mapping_command_window_closed");
        }

        if (candidate.EvidenceConfidence is not
            (BattleEvidenceConfidence.DecoderVerified or BattleEvidenceConfidence.ProductionReady))
        {
            return Failed(BattleProtocolAdapterResultCode.EvidenceBlocked, "battle.protocol.mapping_decoder_not_verified");
        }

        if (candidate.ActionTypeCandidate != BattleCommandActionTypeCandidate.BasicAttack)
        {
            return Failed(BattleProtocolAdapterResultCode.UnsupportedPacket, "battle.protocol.mapping_not_basic_attack");
        }

        if (string.IsNullOrWhiteSpace(candidate.BattleReferenceCandidate) ||
            !string.Equals(
                candidate.BattleReferenceCandidate,
                context.ActiveBattleClientReference,
                StringComparison.Ordinal))
        {
            return Failed(BattleProtocolAdapterResultCode.BattleNotFound, "battle.protocol.mapping_battle_reference_mismatch");
        }

        if (string.IsNullOrWhiteSpace(candidate.ActingParticipantReferenceCandidate) ||
            !context.ClientParticipantReferences.TryGetValue(
                candidate.ActingParticipantReferenceCandidate,
                out var actingParticipantId))
        {
            return Failed(BattleProtocolAdapterResultCode.ParticipantNotFound, "battle.protocol.mapping_acting_participant_unknown");
        }

        if (actingParticipantId != context.ActingParticipantId)
        {
            return Failed(BattleProtocolAdapterResultCode.OwnershipMismatch, "battle.protocol.mapping_acting_participant_ownership");
        }

        if (candidate.RoundCandidate is null || candidate.RoundCandidate != context.RoundNumber)
        {
            return Failed(BattleProtocolAdapterResultCode.StaleRound, "battle.protocol.mapping_round_mismatch");
        }

        if (candidate.CommandWindowCandidate is null ||
            candidate.CommandWindowCandidate != context.CommandWindowVersion)
        {
            return Failed(BattleProtocolAdapterResultCode.InvalidState, "battle.protocol.mapping_window_mismatch");
        }

        if (candidate.TargetReferenceCandidates.Count == 0)
        {
            return Failed(BattleProtocolAdapterResultCode.ParticipantNotFound, "battle.protocol.mapping_target_missing");
        }

        var targets = new List<Guid>(candidate.TargetReferenceCandidates.Count);
        foreach (var targetReference in candidate.TargetReferenceCandidates)
        {
            if (!context.ClientParticipantReferences.TryGetValue(targetReference, out var targetId))
            {
                return Failed(BattleProtocolAdapterResultCode.ParticipantNotFound, "battle.protocol.mapping_target_unknown");
            }

            targets.Add(targetId);
        }

        if (targets.Distinct().Count() != targets.Count)
        {
            return Failed(BattleProtocolAdapterResultCode.ParticipantNotFound, "battle.protocol.mapping_duplicate_target");
        }

        if (!ClientBuildIdentity.IsSha256(candidate.RawPayloadHash))
        {
            return Failed(BattleProtocolAdapterResultCode.DecoderFailure, "battle.protocol.mapping_payload_hash_invalid");
        }

        if (candidate.UnknownFields.Count != 0)
        {
            return Failed(BattleProtocolAdapterResultCode.EvidenceBlocked, "battle.protocol.mapping_unknown_fields_present");
        }

        var command = new BattleActorCommandEnvelope(
            context.ServerCommandId,
            context.ActiveBattleId,
            context.ActingParticipantId,
            context.CharacterId,
            context.SessionSafeReference,
            context.SessionEpoch,
            context.RoundNumber,
            context.CommandWindowVersion,
            context.ActionSlot,
            BattleActorCommandType.BasicAttack,
            SkillDefinitionIdCandidate: null,
            ItemReferenceCandidate: null,
            TargetParticipantIdsCandidate: targets.AsReadOnly(),
            candidate.ClientSequenceCandidate,
            context.ExpectedBattleVersion,
            context.IdempotencyKey,
            candidate.RawPayloadHash.ToLowerInvariant(),
            BattleActorCommandSource.GatewayAdapter,
            context.SubmittedAtUtc,
            context.CorrelationId,
            RawMetadata: "official-client-basic-attack; raw bytes retained only in restricted capture evidence");
        return new BattleProtocolCommandMappingResult(
            BattleProtocolAdapterResultCode.Success,
            command,
            string.Empty);
    }

    private static BattleProtocolCommandMappingResult Failed(
        BattleProtocolAdapterResultCode code,
        string failureCode) =>
        new(code, null, failureCode);
}

public sealed record BattleProtocolCommandSubmissionResult(
    BattleProtocolAdapterResultCode Code,
    BattleEngineOperationResult? EngineResult,
    string FailureCode,
    int PrimarySubmissionCount);

public sealed class BattleProtocolCommandSubmissionCoordinator
{
    private readonly BattleProtocolCommandMapper _mapper;
    private readonly IBattleEngineSelector _engineSelector;

    public BattleProtocolCommandSubmissionCoordinator(
        BattleProtocolCommandMapper mapper,
        IBattleEngineSelector engineSelector)
    {
        _mapper = mapper;
        _engineSelector = engineSelector;
    }

    public async Task<BattleProtocolCommandSubmissionResult> SubmitBasicAttackAsync(
        BattleCommandCandidate candidate,
        BattleProtocolRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var mapped = _mapper.MapBasicAttack(candidate, context);
        if (!mapped.Succeeded || mapped.Command is null)
        {
            return new BattleProtocolCommandSubmissionResult(
                mapped.Code,
                null,
                mapped.FailureCode,
                PrimarySubmissionCount: 0);
        }

        if (_engineSelector.DefaultMode != BattleEngineMode.LegacyPrimary)
        {
            return new BattleProtocolCommandSubmissionResult(
                BattleProtocolAdapterResultCode.InvalidState,
                null,
                "battle.protocol.production_primary_not_legacy",
                PrimarySubmissionCount: 0);
        }

        var primary = _engineSelector.Select();
        var engineResult = await primary.SubmitCommandAsync(mapped.Command, cancellationToken);
        return new BattleProtocolCommandSubmissionResult(
            engineResult.Succeeded
                ? BattleProtocolAdapterResultCode.Success
                : MapEngineFailure(engineResult.Code),
            engineResult,
            engineResult.FailureCode,
            PrimarySubmissionCount: 1);
    }

    private static BattleProtocolAdapterResultCode MapEngineFailure(BattleActorResultCode code) =>
        code switch
        {
            BattleActorResultCode.InvalidSession => BattleProtocolAdapterResultCode.InvalidSession,
            BattleActorResultCode.OwnershipMismatch => BattleProtocolAdapterResultCode.OwnershipMismatch,
            BattleActorResultCode.BattleNotFound => BattleProtocolAdapterResultCode.BattleNotFound,
            BattleActorResultCode.ParticipantNotFound => BattleProtocolAdapterResultCode.ParticipantNotFound,
            BattleActorResultCode.StaleRound or BattleActorResultCode.FutureRound => BattleProtocolAdapterResultCode.StaleRound,
            BattleActorResultCode.EvidenceBlocked => BattleProtocolAdapterResultCode.EvidenceBlocked,
            _ => BattleProtocolAdapterResultCode.InternalFailure
        };
}

public sealed record BattleProtocolActionProjectionContext(
    string ClientBuildId,
    IReadOnlyDictionary<Guid, string> ClientParticipantReferences,
    string SemanticSourceHash);

public sealed record BattleProtocolActionProjectionResult(
    BattleProtocolAdapterResultCode Code,
    IReadOnlyList<BattleSemanticPacketDto> Packets,
    string FailureCode);

public sealed class BattleProtocolActionEventProjection
{
    public BattleProtocolActionProjectionResult Project(
        BattleEventEnvelope battleEvent,
        BattleActionResult? actionResult,
        BattleProtocolActionProjectionContext context)
    {
        if (!ClientBuildIdentity.IsSha256(context.SemanticSourceHash))
        {
            return Failed("battle.protocol.projection_semantic_hash_invalid");
        }

        if (battleEvent.EventType == BattleArchitectureEventKind.ActionStarted)
        {
            if (battleEvent.SourceParticipantId is null ||
                !TryMapParticipants(
                    battleEvent.SourceParticipantId.Value,
                    battleEvent.TargetParticipantIds,
                    context.ClientParticipantReferences,
                    out var source,
                    out var targets))
            {
                return Failed("battle.protocol.projection_participant_mapping_missing");
            }

            return Success(new BattleSemanticPacketDto(
                BattleProtocolPacketFamily.ActionStart,
                context.ClientBuildId,
                battleEvent.EventSequence,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceParticipantReference"] = source,
                    ["targetParticipantReferences"] = string.Join(',', targets),
                    ["roundNumber"] = battleEvent.RoundNumber.ToString(CultureInfo.InvariantCulture)
                },
                new Dictionary<string, BattleUnknownFieldPolicy>(),
                context.SemanticSourceHash));
        }

        if (battleEvent.EventType == BattleArchitectureEventKind.DamageApplied)
        {
            if (actionResult is null ||
                actionResult.ActionId != battleEvent.ActionExecutionId ||
                battleEvent.SourceParticipantId is null ||
                !TryMapParticipants(
                    battleEvent.SourceParticipantId.Value,
                    battleEvent.TargetParticipantIds,
                    context.ClientParticipantReferences,
                    out var source,
                    out var targets))
            {
                return Failed("battle.protocol.projection_damage_result_mismatch");
            }

            return Success(new BattleSemanticPacketDto(
                BattleProtocolPacketFamily.Damage,
                context.ClientBuildId,
                battleEvent.EventSequence,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceParticipantReference"] = source,
                    ["targetParticipantReferences"] = string.Join(',', targets),
                    ["damage"] = actionResult.Damage.ToString(CultureInfo.InvariantCulture),
                    ["hpAfter"] = actionResult.HpAfter.ToString(CultureInfo.InvariantCulture),
                    ["targetDefeated"] = actionResult.TargetDefeated ? "true" : "false"
                },
                new Dictionary<string, BattleUnknownFieldPolicy>
                {
                    ["criticalFlag"] = BattleUnknownFieldPolicy.EvidenceBlocked,
                    ["missFlag"] = BattleUnknownFieldPolicy.EvidenceBlocked,
                    ["animationIdentity"] = BattleUnknownFieldPolicy.EvidenceBlocked
                },
                context.SemanticSourceHash));
        }

        if (battleEvent.EventType == BattleArchitectureEventKind.RoundCompleted)
        {
            return Success(new BattleSemanticPacketDto(
                BattleProtocolPacketFamily.RoundEnd,
                context.ClientBuildId,
                battleEvent.EventSequence,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["roundNumber"] = battleEvent.RoundNumber.ToString(CultureInfo.InvariantCulture)
                },
                new Dictionary<string, BattleUnknownFieldPolicy>
                {
                    ["roundEndWireToken"] = BattleUnknownFieldPolicy.EvidenceBlocked
                },
                context.SemanticSourceHash));
        }

        return new BattleProtocolActionProjectionResult(
            BattleProtocolAdapterResultCode.UnsupportedPacket,
            [],
            "battle.protocol.projection_event_unsupported");
    }

    private static bool TryMapParticipants(
        Guid sourceId,
        IReadOnlyList<Guid> targetIds,
        IReadOnlyDictionary<Guid, string> mappings,
        out string source,
        out IReadOnlyList<string> targets)
    {
        if (!mappings.TryGetValue(sourceId, out source!))
        {
            targets = [];
            return false;
        }

        var mappedTargets = new List<string>(targetIds.Count);
        foreach (var targetId in targetIds)
        {
            if (!mappings.TryGetValue(targetId, out var target))
            {
                targets = [];
                return false;
            }

            mappedTargets.Add(target);
        }

        targets = mappedTargets.AsReadOnly();
        return true;
    }

    private static BattleProtocolActionProjectionResult Success(BattleSemanticPacketDto packet) =>
        new(BattleProtocolAdapterResultCode.Success, [packet], string.Empty);

    private static BattleProtocolActionProjectionResult Failed(string failureCode) =>
        new(BattleProtocolAdapterResultCode.EvidenceBlocked, [], failureCode);
}
