using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialCurrentBuildPacketEvidenceTests
{
    private const string BuildId = "god2-opt-current-build-test";

    [Fact]
    public void Catalog_is_valid_and_keeps_every_recovered_signature_evidence_only()
    {
        var catalog = new OfficialCurrentBuildPacketEvidenceCatalog();
        var signatures = catalog.Snapshot();

        Assert.Empty(catalog.Validate());
        Assert.Equal(19, signatures.Count);
        Assert.All(signatures, signature =>
        {
            Assert.False(signature.SemanticMappingVerified);
            Assert.False(signature.ServerMutationAllowed);
            Assert.NotEqual(BattleEvidenceConfidence.ProductionReady, signature.Confidence);
            Assert.Equal(OfficialCurrentBuildPacketEvidenceCatalog.EvidenceId, signature.EvidenceId);
        });
    }

    [Fact]
    public void Exact_signature_metadata_is_frozen_without_storing_raw_payloads()
    {
        var signatures = new OfficialCurrentBuildPacketEvidenceCatalog().Snapshot();
        var heal = Assert.Single(
            signatures,
            signature => signature.Operation == OfficialCapturedOperation.OutOfBattleHealingRequestCandidate);
        var equipment = signatures.Where(signature =>
            signature.Operation is OfficialCapturedOperation.EquipmentToggleRequestCandidateA or
                OfficialCapturedOperation.EquipmentToggleRequestCandidateB).ToArray();

        Assert.Equal(20, heal.FrameLength);
        Assert.Equal("5424D07422B55CCF4E2816D2DEE9DE59E2220B74A661EA034B94B2AEAC9F6274", heal.RawFrameSha256);
        Assert.Equal(2, heal.ObservationCount);
        Assert.Equal(2, equipment.Length);
        Assert.All(equipment, signature => Assert.Equal(2, signature.ObservationCount));
        Assert.All(
            signatures.Where(signature => signature.MatchKind == OfficialPacketEvidenceMatchKind.ExactRawSignature),
            signature => Assert.True(ClientBuildIdentity.IsSha256(signature.RawFrameSha256!)));

        var correctedAuthentication = Assert.Single(
            signatures,
            signature => signature.Operation == OfficialCapturedOperation.LoginAuthenticationRequestCandidate);
        Assert.Equal(57, correctedAuthentication.FrameLength);
        Assert.Contains("not CharacterDelete evidence", correctedAuthentication.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain(
            signatures,
            signature => signature.Id.Contains("character-delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Structural_battle_carrier_matches_only_a_valid_frame_in_the_in_world_stage()
    {
        var catalog = new OfficialCurrentBuildPacketEvidenceCatalog();
        var frame = new byte[20];
        frame[0] = 20;

        var match = catalog.MatchClientFrame(ProtocolStage.InWorld, frame);
        var wrongStage = catalog.MatchClientFrame(ProtocolStage.CharacterList, frame);
        frame[0] = 19;
        var invalidLengthPrefix = catalog.MatchClientFrame(ProtocolStage.InWorld, frame);

        Assert.NotNull(match);
        Assert.Equal(OfficialCapturedOperation.BattleCommandEnvelopeCandidate, match.Signature.Operation);
        Assert.Equal(OfficialPacketEvidenceMatchKind.StructuralCandidate, match.Signature.MatchKind);
        Assert.True(match.IsEvidenceOnly);
        Assert.Null(wrongStage);
        Assert.Null(invalidLengthPrefix);
    }

    [Theory]
    [InlineData(0x6F, OfficialCapturedOperation.MapBootstrapCandidate)]
    [InlineData(0x8D, OfficialCapturedOperation.BattleBootstrapCandidate)]
    [InlineData(0xD7, OfficialCapturedOperation.BattleEffectCandidate)]
    [InlineData(0xE6, OfficialCapturedOperation.BattleResultCandidate)]
    public void Decoded_server_families_are_recognized_but_never_serializer_enabled(
        byte family,
        OfficialCapturedOperation operation)
    {
        var catalog = new OfficialCurrentBuildPacketEvidenceCatalog();

        var signature = catalog.MatchDecodedServerFamily(ProtocolStage.InWorld, family);

        Assert.NotNull(signature);
        Assert.Equal(operation, signature.Operation);
        Assert.Equal(OfficialPacketEvidenceMatchKind.DecodedServerFamily, signature.MatchKind);
        Assert.False(signature.SemanticMappingVerified);
        Assert.False(signature.ServerMutationAllowed);
        Assert.Null(catalog.MatchDecodedServerFamily(ProtocolStage.CharacterList, family));
    }

    [Fact]
    public void Capture_backed_battle_registry_records_observations_without_opening_any_gate()
    {
        var catalog = new OfficialCurrentBuildPacketEvidenceCatalog();
        var registry = catalog.CreateBattleEvidenceRegistry(BuildId);
        var gate = new EvidenceBackedBattleProtocolGate(registry);

        Assert.Equal(5, registry.Get(BattleProtocolPacketFamily.BattleEnter).SampleCount);
        Assert.Equal("0xE6", registry.Get(BattleProtocolPacketFamily.BattleEnd).CandidateOpcode);
        Assert.Equal(8, registry.Get(BattleProtocolPacketFamily.BattleEnd).SampleCount);
        Assert.Contains(
            OfficialCurrentBuildPacketEvidenceCatalog.EvidencePackageId,
            registry.Get(BattleProtocolPacketFamily.BattleEnd).EvidenceIds);
        Assert.Equal(
            BattleEvidenceConfidence.ObservedRepeated,
            registry.Get(BattleProtocolPacketFamily.ActionStart).Confidence);
        Assert.All(registry.Snapshot(), row => Assert.NotEqual(BattleProtocolGateStatus.ProductionReady, row.Gate));
        Assert.False(gate.CanSerialize(BattleProtocolPacketFamily.BattleEnter, BuildId).Allowed);
        Assert.False(gate.CanDecode(BattleProtocolPacketFamily.BasicAttackClientCommand, BuildId).Allowed);
    }
}
