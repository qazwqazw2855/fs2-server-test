using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialPublicBetaCrossVersionEvidenceTests
{
    [Fact]
    public void Complete_catalog_inventory_separates_matching_and_changed_current_outbound_contracts()
    {
        var outbound = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons();
        var inbound = OfficialPublicBetaCrossVersionEvidence.SnapshotInboundInventory();

        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.ClientToServerEntryCount, outbound.Count);
        Assert.All(outbound, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.GameFeature));
            Assert.NotEqual("待補功能對照", value.GameFeature);
        });
        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.ServerToClientEntryCount,
            inbound.Sum(value => value.EntryCount));
        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.CatalogEntryCount,
            outbound.Count + inbound.Sum(value => value.EntryCount));
        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.MatchingOutboundLengthCount,
            outbound.Count(value => value.LengthComparison is
                PublicBetaLengthComparison.ExactMatch or PublicBetaLengthComparison.BothVariable));
        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.ChangedOutboundLengthCount,
            outbound.Count(value => value.LengthComparison == PublicBetaLengthComparison.Changed));
        Assert.Equal(64, OfficialPublicBetaCrossVersionEvidence.ArchiveSha256.Length);
    }

    [Fact]
    public void Inbound_contracts_have_complete_feature_mappings()
    {
        var inbound = OfficialPublicBetaCrossVersionEvidence.SnapshotInboundContracts();
        var inventory = OfficialPublicBetaCrossVersionEvidence.SnapshotInboundInventory();

        Assert.Equal(OfficialPublicBetaCrossVersionEvidence.ServerToClientEntryCount, inbound.Count);
        Assert.All(inbound, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Domain));
            Assert.False(string.IsNullOrWhiteSpace(value.PublicBetaName));
            Assert.False(string.IsNullOrWhiteSpace(value.GameFeature));
        });
        Assert.All(inventory, inventoryRow =>
        {
            Assert.Equal(
                inventoryRow.EntryCount,
                inbound.Count(value => value.Domain == inventoryRow.Domain));
        });

        Assert.Equal("玩家派生面板數值",
            Assert.Single(
                inbound,
                value => value.Domain == "gameplay" && value.Opcode == 0x2C)
            .GameFeature);
        Assert.Equal("戰鬥控制紀錄",
            Assert.Single(
                inbound,
                value => value.Domain == "combat" && value.Opcode == 0x86)
            .GameFeature);
        Assert.Equal("寵物紀錄新增",
            Assert.Single(
                inbound,
                value => value.Domain == "pet" && value.Opcode == 0xE7)
            .GameFeature);
    }

    [Fact]
    public void Current_verified_non_battle_rows_require_independent_current_evidence()
    {
        var verified = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons()
            .Where(value => value.PromotionStatus == PublicBetaPromotionStatus.CurrentBuildVerified)
            .ToArray();

        Assert.Equal(5, verified.Length);
        Assert.Contains(verified, value => value.Opcode == 0x17 && value.Domain == "character");
        Assert.Contains(verified, value => value.Opcode == 0x18 && value.Domain == "character");
        Assert.Contains(verified, value => value.Opcode == 0x25 && value.Domain == "mission");
        Assert.Contains(verified, value => value.Opcode == 0x28 && value.Domain == "mission");
        Assert.Contains(verified, value => value.Opcode == 0x2F && value.Domain == "social");
        Assert.All(verified, value => Assert.DoesNotContain("client-dispatch-registry.json", value.CurrentEvidence));

        var characterCreate = Assert.Single(verified, value => value.Opcode == 0x17);
        Assert.Equal(45, characterCreate.CurrentApplicationLength);
        Assert.Contains("production Runtime character-create handler", characterCreate.CurrentEvidence, StringComparison.Ordinal);

        var characterSelector = Assert.Single(verified, value => value.Opcode == 0x18);
        Assert.Equal(2, characterSelector.CurrentApplicationLength);
        Assert.Contains("050092B3C2", characterSelector.CurrentEvidence, StringComparison.Ordinal);
        Assert.Contains("TryDecodeCharacterSlotAction", characterSelector.CurrentEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_party_pet_and_vendor_contracts_are_rejected_instead_of_relabelled()
    {
        var rows = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons();

        AssertChanged(rows, "party", 0x8F, 3, 25);
        AssertChanged(rows, "pet", 0xBB, 5, 85);
        AssertChanged(rows, "vendor_cart", 0xB2, 13, 5);

        var battle35 = OfficialPublicBetaCrossVersionEvidence.FindOutbound("combat", 0x35);
        Assert.NotNull(battle35);
        Assert.Equal(PublicBetaLengthComparison.ExactMatch, battle35.LengthComparison);
        Assert.Equal(PublicBetaPromotionStatus.RejectedAsChanged, battle35.PromotionStatus);
        Assert.Contains("changed field offsets", battle35.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_completion_pending_rows_are_exactly_the_five_structural_candidates_that_require_runtime_evidence()
    {
        var pending = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons()
            .Where(value => value.PromotionStatus == PublicBetaPromotionStatus.StructuralCorroborationOnly &&
                ((value.Domain == "mission" && (value.Opcode == 0x24 || value.Opcode == 0x26 || value.Opcode == 0x27)) ||
                 (value.Domain == "party" && (value.Opcode == 0x93 || value.Opcode == 0x95))))
            .ToArray();

        Assert.Equal(5, pending.Length);
        Assert.All(pending, value => Assert.Equal(PublicBetaLengthComparison.ExactMatch, value.LengthComparison));
        Assert.All(pending, value => Assert.Equal(PublicBetaCurrentWireDisposition.CurrentWireCompatibilityAdapterReady, PublicBetaCompatibilityProfile.Production.FindClientRequest(value.Domain, value.Opcode)!.CurrentWireDisposition));
        Assert.All(pending, value => Assert.NotEqual("待補功能對照", value.GameFeature));
        Assert.All(pending, value => Assert.NotEqual(PublicBetaPromotionStatus.CurrentBuildVerified, value.PromotionStatus));
    }

    [Fact]
    public void Structurally_matched_contracts_record_current_producer_boundaries()
    {
        var rows = OfficialPublicBetaCrossVersionEvidence.SnapshotOutboundComparisons();
        var byKey = rows
            .Where(value =>
                (value.Domain == "mission" && value.Opcode == 0x24) ||
                (value.Domain == "mission" && value.Opcode == 0x26) ||
                (value.Domain == "mission" && value.Opcode == 0x27) ||
                (value.Domain == "combination" && value.Opcode == 0xA4) ||
                (value.Domain == "party" && value.Opcode == 0x93) ||
                (value.Domain == "party" && value.Opcode == 0x95) ||
                (value.Domain == "team" && value.Opcode == 0x5A))
            .Select(value => value.Boundary)
            .ToArray();

        Assert.Equal(7, byKey.Length);
        Assert.All(byKey, rowBoundary => Assert.Contains("RVA", rowBoundary, StringComparison.Ordinal));
        Assert.All(byKey, rowBoundary => Assert.DoesNotContain("Opcode and application-record length match", rowBoundary, StringComparison.Ordinal));
    }

    [Fact]
    public void Inbound_mission_inventory_notes_three_byte_df_boundary()
    {
        var mission = Assert.Single(
            OfficialPublicBetaCrossVersionEvidence.SnapshotInboundInventory(),
            value => value.Domain == "mission");

        Assert.Contains("three-byte", mission.CurrentBuildDisposition, StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_increment_contract_stays_candidate_only_pending_button_capture()
    {
        var row = OfficialPublicBetaCrossVersionEvidence.FindOutbound("gameplay", 0x21);
        Assert.NotNull(row);
        Assert.Equal(PublicBetaPromotionStatus.StructuralCorroborationOnly, row.PromotionStatus);
        Assert.Contains("candidate-only", row.Boundary, StringComparison.Ordinal);
        Assert.Contains("current dispatcher/consumer boundaries", row.Boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_layouts_enable_verified_status_and_vitals_projections()
    {
        var status = new byte[OfficialPlayerSnapshotCandidateWireCodec.StatusRecordLength];
        status[0] = OfficialPlayerSnapshotCandidateWireCodec.StatusOpcode;
        status[1] = 87;
        status[2] = 0xAA;
        WriteU16(status, 3, 9);
        WriteU16(status, 5, 205);
        WriteU16(status, 7, 24);
        WriteU16(status, 9, 813);
        WriteU16(status, 11, 613);
        WriteU16(status, 13, 2191);
        WriteU16(status, 15, 0xBEEF);
        WriteI32(status, 17, 2226);
        WriteI32(status, 21, 1408);

        var decodedStatus = OfficialPlayerSnapshotCandidateWireCodec.DecodeStatusLayout(
            OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            status);

        Assert.True(decodedStatus.LayoutDecoded);
        Assert.NotNull(decodedStatus.Value);
        Assert.Equal(87, decodedStatus.Value.Level);
        Assert.Equal((ushort)9, decodedStatus.Value.UnspentAttributePoints);
        Assert.Equal((ushort)205, decodedStatus.Value.Vitality);
        Assert.Equal((ushort)24, decodedStatus.Value.Strength);
        Assert.Equal((ushort)813, decodedStatus.Value.Intelligence);
        Assert.Equal((ushort)613, decodedStatus.Value.Speed);
        Assert.Equal((ushort)2191, decodedStatus.Value.ExperienceBasisPoints);
        Assert.Equal(2226, decodedStatus.Value.MaximumHitPoints);
        Assert.Equal(1408, decodedStatus.Value.MaximumMagicPoints);
        Assert.True(decodedStatus.Value.RuntimeMutationAllowed);

        var vitals = new byte[OfficialPlayerSnapshotCandidateWireCodec.VitalsRecordLength];
        vitals[0] = OfficialPlayerSnapshotCandidateWireCodec.VitalsOpcode;
        WriteI32(vitals, 1, 1881);
        WriteI32(vitals, 5, 1071);
        WriteI32(vitals, 9, 1881);
        WriteI32(vitals, 13, 1306);
        var decodedVitals = OfficialPlayerSnapshotCandidateWireCodec.DecodeVitalsLayout(
            OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            vitals);

        Assert.True(decodedVitals.LayoutDecoded);
        Assert.Equal(1881, decodedVitals.Value!.CurrentHitPoints);
        Assert.Equal(1071, decodedVitals.Value.CurrentMagicPoints);
        Assert.Equal(1306, decodedVitals.Value.MaximumMagicPoints);
        Assert.True(decodedVitals.Value.RuntimeMutationAllowed);
    }

    [Theory]
    [InlineData(1, OfficialPlayerAttributeCandidate.Intelligence)]
    [InlineData(2, OfficialPlayerAttributeCandidate.Vitality)]
    [InlineData(3, OfficialPlayerAttributeCandidate.Strength)]
    [InlineData(4, OfficialPlayerAttributeCandidate.Speed)]
    public void Attribute_increment_mapping_is_exposed_as_candidate_only(
        byte rawCode,
        OfficialPlayerAttributeCandidate expected)
    {
        var decoded = OfficialPlayerSnapshotCandidateWireCodec.DecodeAttributeIncrementLayout(
            OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            [OfficialPlayerSnapshotCandidateWireCodec.AttributeIncrementOpcode, rawCode]);

        Assert.True(decoded.LayoutDecoded);
        Assert.Equal(expected, decoded.Value!.PublicBetaCandidate);
        Assert.False(decoded.Value.RuntimeMutationAllowed);
        Assert.Contains("CurrentConsumerConfirmationRequired", decoded.Value.EvidenceStatus, StringComparison.Ordinal);

        var runtime = OfficialPlayerSnapshotCandidateWireCodec.RejectRuntimeMutation<OfficialAttributeIncrementCandidate>();
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, runtime.Code);
        Assert.Null(runtime.Value);
    }

    [Fact]
    public void Player_candidate_decoders_fail_closed_on_wrong_boundaries()
    {
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidAttributeCode,
            OfficialPlayerSnapshotCandidateWireCodec.DecodeAttributeIncrementLayout(
                OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                [0x21, 0]).Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidState,
            OfficialPlayerSnapshotCandidateWireCodec.DecodeAttributeIncrementLayout(
                OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
                GameplayProtocolState.Battle,
                [0x21, 1]).Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidLength,
            OfficialPlayerSnapshotCandidateWireCodec.DecodeStatusLayout(
                OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                [0x22]).Code);

        var wrongOpcode = new byte[OfficialPlayerSnapshotCandidateWireCodec.VitalsRecordLength];
        wrongOpcode[0] = 0x23;
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidOpcode,
            OfficialPlayerSnapshotCandidateWireCodec.DecodeVitalsLayout(
                OfficialPlayerSnapshotCandidateWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                wrongOpcode).Code);
    }

    [Fact]
    public void Non_network_data_inventory_covers_all_reconstructed_feature_domains_without_direct_import_authority()
    {
        var domains = OfficialPublicBetaDataEvidence.SnapshotDomains();

        Assert.Equal(OfficialPublicBetaDataEvidence.DomainCount, domains.Count);
        Assert.Equal(domains.Count, domains.Select(value => value.DomainId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(domains, value =>
        {
            Assert.True(value.CanGuideCurrentExtractor);
            Assert.False(value.DirectGameplayImportAllowed);
            Assert.NotEmpty(value.PublicBetaSources);
            Assert.Contains("current-build files or captures", value.ImportBoundary, StringComparison.Ordinal);
        });
        Assert.Equal(50, OfficialPublicBetaDataEvidence.SuppliedCsvZFileCount);
        Assert.Equal(41_510, OfficialPublicBetaDataEvidence.SuppliedCsvZRowCount);
        Assert.Equal(384_162, OfficialPublicBetaDataEvidence.SuppliedCsvZFieldCount);
    }

    [Fact]
    public void Data_inventory_preserves_negative_authority_boundaries_for_quests_monsters_skills_and_gods()
    {
        var quest = OfficialPublicBetaDataEvidence.Find("activity_mission_requirements");
        var monster = OfficialPublicBetaDataEvidence.Find("monster_presentation");
        var skill = OfficialPublicBetaDataEvidence.Find("skill_book_effect");
        var god = OfficialPublicBetaDataEvidence.Find("god_menu");

        Assert.NotNull(quest);
        Assert.Contains("completion, consumption and rewards", quest.CurrentServerUse, StringComparison.Ordinal);
        Assert.NotNull(monster);
        Assert.Contains("forbid stat derivation", monster.CurrentServerUse, StringComparison.Ordinal);
        Assert.NotNull(skill);
        Assert.Contains("formulas blocked", skill.CurrentServerUse, StringComparison.Ordinal);
        Assert.NotNull(god);
        Assert.Contains("do not infer growth", god.CurrentServerUse, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_output_opcode_0x84_stays_hypothesis_only_until_current_build_capture_exists()
    {
        var row = OfficialBattleCrossVersionEvidence.Find(0x84, PacketDirection.ServerToClient);
        Assert.NotNull(row);
        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, row.Status);
        Assert.Equal(15, row.PublicBetaRecordLength);
        Assert.Null(row.CurrentBuildRecordLength);
        Assert.Contains("entireCurrentBuildRecord", row.BlockedFields);

        Assert.DoesNotContain(
            (byte)0x84,
            PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes);
        Assert.Equal(
            PublicBetaCompatibilityBattleWireAdapter.CanonicalServerCombatContractCount,
            PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputOpcodes.Count +
            PublicBetaCompatibilityBattleWireAdapter.CurrentServerOutputEvidenceBlockedCount);
    }

    [Fact]
    public void Local_player_derived_stats_output_0x2C_stays_hypothesis_only_until_current_build_derived_panel_capture_exists()
    {
        var row = OfficialBattleCrossVersionEvidence.Find(0x2C, PacketDirection.ServerToClient);
        Assert.NotNull(row);
        Assert.Equal(BattleCrossVersionPromotionStatus.HypothesisOnly, row.Status);
        Assert.Equal(31, row.PublicBetaRecordLength);
        Assert.Null(row.CurrentBuildRecordLength);
        Assert.Contains("currentBuildPrivateStatOpcodeAndLayout", row.BlockedFields);
        Assert.Equal(
            "no current-build network record carries a confirmed local-player derived-stats panel vector",
            row.CurrentBuildEvidence);

        var gameplayInventory = OfficialPublicBetaCrossVersionEvidence.SnapshotInboundInventory()
            .Single(value => value.Domain == "gameplay");
        Assert.Equal(17, gameplayInventory.EntryCount);
        Assert.Contains(
            "0x2C local-panel consumer",
            gameplayInventory.CurrentBuildDisposition,
            StringComparison.Ordinal);
    }

    private static void AssertChanged(
        IReadOnlyList<PublicBetaOutboundContractComparison> rows,
        string domain,
        byte opcode,
        int publicBetaLength,
        int currentLength)
    {
        var row = rows.Single(value => value.Domain == domain && value.Opcode == opcode);
        Assert.Equal(publicBetaLength, row.PublicBetaApplicationLength);
        Assert.Equal(currentLength, row.CurrentApplicationLength);
        Assert.Equal(PublicBetaLengthComparison.Changed, row.LengthComparison);
        Assert.Equal(PublicBetaPromotionStatus.RejectedAsChanged, row.PromotionStatus);
    }

    private static void WriteU16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteI32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
}

