namespace God2.ClassicServer.Protocol;

public enum PublicBetaBattleAdapterResultCode
{
    Adapted,
    WireDecodeFailed,
    UnknownAction,
    UnsupportedThirdTargetGroup,
    MissingTarget,
    SkillCatalogMappingRequired
}

public sealed record PublicBetaCompatibilityBattleCommand(
    byte ActingBattlePosition,
    OfficialBattleCommandAction Action,
    IReadOnlyList<byte> TargetBattlePositions,
    ushort RawBattleContext,
    uint RawActionParameter,
    bool Continuation,
    string CurrentWireFrameSha256,
    string CompatibilityEvidence,
    bool ProductionMutationAllowed);

public sealed record PublicBetaBattleAdapterResult(
    PublicBetaBattleAdapterResultCode Code,
    PublicBetaCompatibilityBattleCommand? Command,
    string FailureCode)
{
    public bool Adapted => Code == PublicBetaBattleAdapterResultCode.Adapted && Command is not null;
}

/// <summary>
/// Version adapter from the exact current-build C2S 0x35 layout to the canonical
/// public-beta combat command family. It only promotes fields corroborated by both
/// versions. The current third target-mask word has no two-side public-beta equivalent,
/// so non-zero values fail closed. Skill operands remain catalog-gated.
/// </summary>
public static class PublicBetaCompatibilityBattleWireAdapter
{
    public static IReadOnlySet<byte> CurrentServerOutputOpcodes { get; } = new HashSet<byte>
    {
        OfficialBattleRosterControlWireCodec.RosterOpcode,
        OfficialBattleRosterControlWireCodec.ControlBoundaryOpcode,
        OfficialBattleEffectWireCodec.EffectOpcode,
        OfficialBattleRosterControlWireCodec.SequenceBoundaryOpcode,
        OfficialBattleRosterControlWireCodec.ControlOpcode,
        OfficialBattleRosterControlWireCodec.PackedControlOpcode,
        OfficialBattleVitalSnapshotWireCodec.SnapshotOpcode,
        OfficialBattleSettlementWireCodec.SettlementOpcode
    };

    public const int CanonicalServerCombatContractCount = 9;
    public const int CurrentServerOutputAdapterReadyCount = 8;
    public const int CurrentServerOutputEvidenceBlockedCount = 1;

    public static PublicBetaBattleAdapterResult AdaptClientCommand(
        string clientBuildId,
        ReadOnlySpan<byte> decodedFrame,
        Func<uint, bool>? isKnownSkillOperand = null)
    {
        var decoded = OfficialBattleCommandWireCodec.DecodeLayout(
            clientBuildId,
            GameplayProtocolState.Battle,
            decodedFrame);
        if (!decoded.LayoutDecoded || decoded.Value is null)
        {
            return Failure(
                PublicBetaBattleAdapterResultCode.WireDecodeFailed,
                decoded.FailureCode);
        }

        var candidate = decoded.Value;
        if (candidate.Action == OfficialBattleCommandAction.Unknown)
        {
            return Failure(
                PublicBetaBattleAdapterResultCode.UnknownAction,
                "wire.public_beta_compat.battle_action_unknown");
        }
        if (candidate.TargetMaskGroup2 != 0)
        {
            return Failure(
                PublicBetaBattleAdapterResultCode.UnsupportedThirdTargetGroup,
                "wire.public_beta_compat.third_target_group_not_promoted");
        }
        if (candidate.Action is OfficialBattleCommandAction.BasicAttack or OfficialBattleCommandAction.Skill &&
            candidate.TargetPositionIndices.Count == 0)
        {
            return Failure(
                PublicBetaBattleAdapterResultCode.MissingTarget,
                "wire.public_beta_compat.battle_target_missing");
        }
        if (candidate.Action == OfficialBattleCommandAction.Skill &&
            (isKnownSkillOperand is null || !isKnownSkillOperand(candidate.ActionParameter)))
        {
            return Failure(
                PublicBetaBattleAdapterResultCode.SkillCatalogMappingRequired,
                "wire.public_beta_compat.skill_operand_catalog_mapping_required");
        }

        return new PublicBetaBattleAdapterResult(
            PublicBetaBattleAdapterResultCode.Adapted,
            new PublicBetaCompatibilityBattleCommand(
                candidate.PositionIndex,
                candidate.Action,
                candidate.TargetPositionIndices.Select(position => checked((byte)position)).ToArray(),
                candidate.BattleContext,
                candidate.ActionParameter,
                candidate.Continuation,
                candidate.DecodedFrameSha256,
                "CurrentBuild-0x35-exact-layout+PublicBeta-stable-actor-action-two-side-command-family",
                ProductionMutationAllowed: false),
            string.Empty);
    }

    private static PublicBetaBattleAdapterResult Failure(
        PublicBetaBattleAdapterResultCode code,
        string failureCode) => new(code, null, failureCode);
}
