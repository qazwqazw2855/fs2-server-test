using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public enum OfficialCapturedOperation
{
    LoginAuthenticationRequestCandidate,
    CharacterCreateRequestCandidate,
    MerchantInsufficientFundsRequestCandidate,
    OutOfBattleHealingRequestCandidate,
    SynthesisRequestCandidate,
    EquipmentToggleRequestCandidateA,
    EquipmentToggleRequestCandidateB,
    BattleCommandEnvelopeCandidate,
    BattleBoundaryAcknowledgementCandidate,
    BattleResultDismissAcknowledgementCandidate,
    MapBootstrapCandidate,
    BattleBootstrapCandidate,
    BattleEffectCandidate,
    BattleResultCandidate
}

public enum OfficialPacketEvidenceMatchKind
{
    ExactRawSignature,
    StructuralCandidate,
    DecodedServerFamily
}

public sealed record OfficialCurrentBuildPacketSignature(
    string Id,
    OfficialCapturedOperation Operation,
    PacketDirection Direction,
    int FrameLength,
    string? RawFrameSha256,
    byte? DecodedFamily,
    IReadOnlyList<ProtocolStage> AllowedStages,
    BattleEvidenceConfidence Confidence,
    int ObservationCount,
    OfficialPacketEvidenceMatchKind MatchKind,
    bool SemanticMappingVerified,
    bool ServerMutationAllowed,
    string EvidenceId,
    string Notes);

public sealed record OfficialCurrentBuildPacketMatch(
    OfficialCurrentBuildPacketSignature Signature,
    string RawFrameSha256)
{
    public bool IsEvidenceOnly => !Signature.SemanticMappingVerified || !Signature.ServerMutationAllowed;
}

/// <summary>
/// Build-scoped evidence recovered from the official 2026-02-04 v0.30.33 client trace.
/// This catalog recognizes packets without granting gameplay mutation or serializer authority.
/// </summary>
public sealed class OfficialCurrentBuildPacketEvidenceCatalog
{
    public const string ClientBuildLabel = "XianJieZhuan Build Feb 4 2026 Ver 0.30.33";
    public const string EvidenceId = "attempt-215359-trace";
    public const string EvidencePackageId = OfficialEvidencePackage20260806Catalog.EvidenceSourceId;
    public const string EvidenceArchiveSha256 = "7ACBF327126505D312633B8DE9112DA914EDB8DF7A0E12ACAB304B3C18D7DA8D";

    private static readonly IReadOnlyList<ProtocolStage> CharacterListStages =
        Array.AsReadOnly([ProtocolStage.Authenticated, ProtocolStage.CharacterList]);

    private static readonly IReadOnlyList<ProtocolStage> InWorldStages =
        Array.AsReadOnly([ProtocolStage.InWorld]);

    private static readonly ReadOnlyCollection<OfficialCurrentBuildPacketSignature> RawSignatures =
        Array.AsReadOnly(
        [
            Exact(
                "login-authentication-request-candidate",
                OfficialCapturedOperation.LoginAuthenticationRequestCandidate,
                57,
                "8820E11D5D650E1705BE8B749608E731A66BEF2CD4D41DEEC4856D6D61C3C7D2",
                CharacterListStages,
                BattleEvidenceConfidence.ObservedOnce,
                1,
                "Static current-build branch recovery identifies the decoded request as opcode 0x19 with a 53-byte three-string authentication payload. It is not CharacterDelete evidence; credential fields remain opaque and mutation is disabled."),
            Exact(
                "character-create-request-candidate",
                OfficialCapturedOperation.CharacterCreateRequestCandidate,
                48,
                "D3BC28936D009125B0ED76FDFF1B3A3BE9AE887BA81B7D425DBBF9BB0A262731",
                CharacterListStages,
                BattleEvidenceConfidence.ObservedOnce,
                1,
                "Static current-build branch recovery identifies opcode 0x17 with a 44-byte create payload and the paired 0x3B/0x18 receive path. Required field semantics and serializer values remain blocked."),
            Exact(
                "merchant-insufficient-funds-request-candidate",
                OfficialCapturedOperation.MerchantInsufficientFundsRequestCandidate,
                7,
                "56D6A4C8E7CFE7481798879BCC1EE132C3427B9AF62ED02F9840363EDF6691FC",
                InWorldStages,
                BattleEvidenceConfidence.ObservedOnce,
                1,
                "Unique request correlated with an immediate 25-byte failure response."),
            Exact(
                "pharmacist-out-of-battle-heal-request-candidate",
                OfficialCapturedOperation.OutOfBattleHealingRequestCandidate,
                20,
                "5424D07422B55CCF4E2816D2DEE9DE59E2220B74A661EA034B94B2AEAC9F6274",
                InWorldStages,
                BattleEvidenceConfidence.ObservedRepeated,
                2,
                "Byte-identical request observed twice outside battle."),
            Exact(
                "synthesis-request-candidate",
                OfficialCapturedOperation.SynthesisRequestCandidate,
                85,
                "3CEBEDCEFF9EAD2D7EE609BB869C033A1B3649FD61010BB580413FD8156E64C9",
                InWorldStages,
                BattleEvidenceConfidence.ObservedOnce,
                1,
                "Unique request followed by an inventory/state update burst."),
            Exact(
                "equipment-toggle-request-candidate-a",
                OfficialCapturedOperation.EquipmentToggleRequestCandidateA,
                8,
                "E47FB4451668FB27AEBC7AF96351236F9834B36F419179D0CA9474B6F3BC1C97",
                InWorldStages,
                BattleEvidenceConfidence.ObservedRepeated,
                2,
                "One half of the observed A/B/A/B wear/remove pair; direction is not yet frozen."),
            Exact(
                "equipment-toggle-request-candidate-b",
                OfficialCapturedOperation.EquipmentToggleRequestCandidateB,
                8,
                "B73D32F0C8617FD49D201B982C997EC10FF0A53A979FF0502307D0498949FD41",
                InWorldStages,
                BattleEvidenceConfidence.ObservedRepeated,
                2,
                "One half of the observed A/B/A/B wear/remove pair; direction is not yet frozen."),
            Exact(
                "battle-result-dismiss-acknowledgement-candidate",
                OfficialCapturedOperation.BattleResultDismissAcknowledgementCandidate,
                8,
                "24EB163505AB3369F07A506A81AF267549D8D6D63F982DCC64567258E4139164",
                InWorldStages,
                BattleEvidenceConfidence.ObservedRepeated,
                2,
                "Repeated immediately after two terminal battle result sequences."),
            Structural(
                "battle-command-20-byte-envelope-candidate",
                OfficialCapturedOperation.BattleCommandEnvelopeCandidate,
                20,
                InWorldStages,
                41,
                "Shared battle action carrier with 22 payload variants; individual action semantics remain unresolved."),
            Structural(
                "battle-boundary-12-byte-acknowledgement-candidate",
                OfficialCapturedOperation.BattleBoundaryAcknowledgementCandidate,
                12,
                InWorldStages,
                6,
                "Battle/round acknowledgement or command boundary family."),
        ]);

    private static readonly ReadOnlyCollection<OfficialCurrentBuildPacketSignature> DecodedServerFamilies =
        Array.AsReadOnly(
        [
            Decoded(
                "map-bootstrap-family-6f",
                OfficialCapturedOperation.MapBootstrapCandidate,
                0x6F,
                InWorldStages,
                1,
                "Decoded 2,560-byte map/state bootstrap at the confirmed transfer boundary."),
            Decoded(
                "battle-bootstrap-family-8d",
                OfficialCapturedOperation.BattleBootstrapCandidate,
                0x8D,
                InWorldStages,
                5,
                "Observed across complete battle session bootstraps/state initialization."),
            Decoded(
                "battle-bootstrap-family-d6",
                OfficialCapturedOperation.BattleBootstrapCandidate,
                0xD6,
                InWorldStages,
                5,
                "Observed across complete battle session bootstraps/state initialization."),
            Decoded("battle-effect-family-d5", OfficialCapturedOperation.BattleEffectCandidate, 0xD5, InWorldStages, 1, "Battle effect/state family; exact action label unresolved."),
            Decoded("battle-effect-family-d7", OfficialCapturedOperation.BattleEffectCandidate, 0xD7, InWorldStages, 2, "Battle effect/state family; defend is a candidate, not frozen."),
            Decoded("battle-effect-family-d8", OfficialCapturedOperation.BattleEffectCandidate, 0xD8, InWorldStages, 1, "Battle effect/state family; exact action label unresolved."),
            Decoded("battle-effect-family-d9", OfficialCapturedOperation.BattleEffectCandidate, 0xD9, InWorldStages, 1, "Battle state/result transition; flee is a candidate, not frozen."),
            Decoded("battle-effect-family-da", OfficialCapturedOperation.BattleEffectCandidate, 0xDA, InWorldStages, 1, "Battle effect/state family; exact action label unresolved."),
            Decoded(
                "battle-result-family-e6",
                OfficialCapturedOperation.BattleResultCandidate,
                0xE6,
                InWorldStages,
                8,
                "Terminal result family observed in five prior complete battle sessions and three additional PostDecrypt frames in the validated v1.0.1 Evidence Package."),
        ]);

    public IReadOnlyList<OfficialCurrentBuildPacketSignature> Snapshot() =>
        RawSignatures.Concat(DecodedServerFamilies).ToArray();

    public OfficialCurrentBuildPacketMatch? MatchClientFrame(ProtocolStage stage, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2 || (frame[0] | (frame[1] << 8)) != frame.Length)
        {
            return null;
        }

        var frameLength = frame.Length;
        var hash = PacketEvidenceHash.Sha256Hex(frame);
        var exact = RawSignatures.FirstOrDefault(signature =>
            signature.Direction == PacketDirection.ClientToServer &&
            signature.MatchKind == OfficialPacketEvidenceMatchKind.ExactRawSignature &&
            signature.FrameLength == frameLength &&
            signature.AllowedStages.Contains(stage) &&
            string.Equals(signature.RawFrameSha256, hash, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new OfficialCurrentBuildPacketMatch(exact, hash);
        }

        var structural = RawSignatures.FirstOrDefault(signature =>
            signature.Direction == PacketDirection.ClientToServer &&
            signature.MatchKind == OfficialPacketEvidenceMatchKind.StructuralCandidate &&
            signature.FrameLength == frameLength &&
            signature.AllowedStages.Contains(stage));
        return structural is null ? null : new OfficialCurrentBuildPacketMatch(structural, hash);
    }

    public OfficialCurrentBuildPacketSignature? MatchDecodedServerFamily(
        ProtocolStage stage,
        byte decodedFamily) =>
        DecodedServerFamilies.FirstOrDefault(signature =>
            signature.DecodedFamily == decodedFamily && signature.AllowedStages.Contains(stage));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var signatures = Snapshot();
        if (signatures.Select(signature => signature.Id).Distinct(StringComparer.Ordinal).Count() != signatures.Count)
        {
            errors.Add("official.current_build_evidence.duplicate_id");
        }

        foreach (var signature in signatures)
        {
            if (signature.ObservationCount <= 0 || signature.AllowedStages.Count == 0)
            {
                errors.Add($"official.current_build_evidence.invalid_metadata:{signature.Id}");
            }

            if (signature.MatchKind == OfficialPacketEvidenceMatchKind.ExactRawSignature &&
                (signature.RawFrameSha256 is null || !ClientBuildIdentity.IsSha256(signature.RawFrameSha256)))
            {
                errors.Add($"official.current_build_evidence.invalid_hash:{signature.Id}");
            }

            if (signature.ServerMutationAllowed || signature.SemanticMappingVerified)
            {
                errors.Add($"official.current_build_evidence.unsafe_promotion:{signature.Id}");
            }
        }

        return errors.AsReadOnly();
    }

    public BattlePacketEvidenceRegistry CreateBattleEvidenceRegistry(string clientBuildId)
    {
        var initial = BattlePacketEvidenceRegistry.CreateInitial(clientBuildId).Snapshot();
        return new BattlePacketEvidenceRegistry(initial.Select(row => row.Family switch
        {
            BattleProtocolPacketFamily.BattleEnter => CapturedBattleRow(
                row,
                "0x8D/0xD6",
                5,
                "Five complete battle session bootstraps observed; fields and serializer remain blocked."),
            BattleProtocolPacketFamily.ActionStart => CapturedBattleRow(
                row,
                "0xD5/0xD7/0xD8/0xD9/0xDA",
                5,
                "Battle effect/state families observed; per-action semantic mapping remains blocked."),
            BattleProtocolPacketFamily.BattleEnd => CapturedBattleRow(
                row,
                "0xE6",
                8,
                "Terminal result family has eight capture-backed observations including three in the validated v1.0.1 Evidence Package; serializer fields remain blocked.",
                EvidencePackageId),
            _ => row
        }));
    }

    private static OfficialCurrentBuildPacketSignature Exact(
        string id,
        OfficialCapturedOperation operation,
        int frameLength,
        string hash,
        IReadOnlyList<ProtocolStage> stages,
        BattleEvidenceConfidence confidence,
        int observations,
        string notes) =>
        new(
            id,
            operation,
            PacketDirection.ClientToServer,
            frameLength,
            hash,
            null,
            stages,
            confidence,
            observations,
            OfficialPacketEvidenceMatchKind.ExactRawSignature,
            SemanticMappingVerified: false,
            ServerMutationAllowed: false,
            EvidenceId,
            notes);

    private static OfficialCurrentBuildPacketSignature Structural(
        string id,
        OfficialCapturedOperation operation,
        int frameLength,
        IReadOnlyList<ProtocolStage> stages,
        int observations,
        string notes) =>
        new(
            id,
            operation,
            PacketDirection.ClientToServer,
            frameLength,
            null,
            null,
            stages,
            BattleEvidenceConfidence.Candidate,
            observations,
            OfficialPacketEvidenceMatchKind.StructuralCandidate,
            SemanticMappingVerified: false,
            ServerMutationAllowed: false,
            EvidenceId,
            notes);

    private static OfficialCurrentBuildPacketSignature Decoded(
        string id,
        OfficialCapturedOperation operation,
        byte family,
        IReadOnlyList<ProtocolStage> stages,
        int observations,
        string notes) =>
        new(
            id,
            operation,
            PacketDirection.ServerToClient,
            0,
            null,
            family,
            stages,
            observations > 1 ? BattleEvidenceConfidence.ObservedRepeated : BattleEvidenceConfidence.ObservedOnce,
            observations,
            OfficialPacketEvidenceMatchKind.DecodedServerFamily,
            SemanticMappingVerified: false,
            ServerMutationAllowed: false,
            EvidenceId,
            notes);

    private static BattlePacketEvidence CapturedBattleRow(
        BattlePacketEvidence row,
        string candidateOpcode,
        int sampleCount,
        string status,
        string? additionalEvidenceId = null) =>
        row with
        {
            CandidateOpcode = candidateOpcode,
            DecodeStatus = "Capture-backed family identified; field decoder not verified.",
            SemanticMappingStatus = status,
            Confidence = BattleEvidenceConfidence.ObservedRepeated,
            SampleCount = sampleCount,
            FirstSeenUtc = new DateTimeOffset(2026, 7, 31, 13, 58, 19, TimeSpan.Zero),
            LastSeenUtc = new DateTimeOffset(2026, 7, 31, 14, 3, 9, TimeSpan.Zero),
            StatePreconditions = ["Official client in world", "Battle session active"],
            EvidenceIds = additionalEvidenceId is null ? [EvidenceId] : [EvidenceId, additionalEvidenceId],
            ProductionGateReason = "Packet family is capture-backed, but field semantics and serializer acceptance are not verified."
        };
}
