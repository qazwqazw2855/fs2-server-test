using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class PublicBetaCompatibilityGameplayWireAdapterTests
{
    [Fact]
    public void Registry_binds_ten_canonical_gameplay_outputs_to_executable_current_writers()
    {
        var outputs = PublicBetaCompatibilityGameplayWireAdapter.CurrentServerOutputs;

        Assert.Equal(10, outputs.Count);
        Assert.Equal(10, outputs.Select(value => value.CanonicalContract).Distinct().Count());
        Assert.Equal(
            ["god_catalog_selector_32", "god_status_snapshot_31", "god_ui_flags_33", "god_vitals_snapshot_39", "monster_spawn_71", "npc_spawn_72", "player_derived_stats_snapshot", "player_god_progress_2b", "player_status_snapshot_22", "player_vitals_snapshot_24"],
            outputs.Select(value => value.CanonicalContract).Order().ToArray());
        Assert.All(outputs, value =>
        {
            Assert.NotEmpty(value.CurrentWriter);
            Assert.NotEmpty(value.Evidence);
            Assert.True(value.CurrentApplicationRecordLength > 0);
        });
    }

    [Fact]
    public void Player_status_encodes_the_exact_current_bootstrap_layout()
    {
        var result = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerStatus(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerStatusLayout(7, 0, 4, 33, 44, 22, 25, 8076, 0, 444, 88));

        Assert.True(result.LayoutDecoded, result.FailureCode);
        Assert.Equal(
            "220700040021002C00160019008C1F0000BC01000058000000",
            Convert.ToHexString(result.Value!));
    }

    [Fact]
    public void Player_vitals_encodes_the_exact_current_bootstrap_layout()
    {
        var result = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerVitals(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerVitalsLayout(333, 77, 444, 88));

        Assert.True(result.LayoutDecoded, result.FailureCode);
        Assert.Equal("244D0100004D000000BC01000058000000", Convert.ToHexString(result.Value!));
    }

    [Fact]
    public void Player_god_progress_encodes_the_exact_current_bootstrap_record()
    {
        var result = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerGodProgress(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerGodProgressLayout(204, 540, 54, 207, 8076, 2435));

        Assert.True(result.LayoutDecoded, result.FailureCode);
        Assert.Equal("2BCC0000001C02000036000000CF0000008C1F8309", Convert.ToHexString(result.Value!));
    }

    [Fact]
    public void Immortal_catalog_selector_and_ui_flags_encode_exact_two_byte_records()
    {
        var selector = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalCatalogSelector(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            0x7A);
        var flags = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalUiFlags(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            0xA5);

        Assert.True(selector.LayoutDecoded, selector.FailureCode);
        Assert.Equal("327A", Convert.ToHexString(selector.Value!));
        Assert.True(flags.LayoutDecoded, flags.FailureCode);
        Assert.Equal("33A5", Convert.ToHexString(flags.Value!));
    }

    [Fact]
    public void Immortal_status_and_vitals_encode_the_exact_current_records()
    {
        var status = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalStatus(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialImmortalStatusLayout(
                17, 1, 0, 10, 0, 0, 0, 0x00000104,
                30, 20, 10, 5, 0, false, 0));
        var vitals = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalVitals(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialImmortalVitalsLayout(400, 156, 400, 156));

        Assert.True(status.LayoutDecoded, status.FailureCode);
        Assert.Equal(
            "31110100000A00000000000000040100001E0014000A00050000000000",
            Convert.ToHexString(status.Value!));
        Assert.True(vitals.LayoutDecoded, vitals.FailureCode);
        Assert.Equal(
            "39900100009C000000900100009C000000",
            Convert.ToHexString(vitals.Value!));
    }

    [Fact]
    public void Npc_spawn_encodes_the_current_21_byte_application_record()
    {
        var result = PublicBetaCompatibilityGameplayWireAdapter.EncodeNpcSpawn(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialNpcSpawnLayout(
                1504, 142, 2, 3, 0, 4, 1,
                Convert.FromHexString("EA680FC2034A01"),
                17, 8, 2));

        Assert.True(result.LayoutDecoded, result.FailureCode);
        Assert.Equal(21, result.Value!.Length);
        Assert.Equal(
            "72E00500008F62000081EA680FC2034A0146001000",
            Convert.ToHexString(result.Value));
    }

    [Fact]
    public void Gameplay_writers_fail_closed_for_wrong_build_state_or_unproven_values()
    {
        var layout = new OfficialNpcSpawnLayout(
            1504, 142, 2, 3, 0, 4, 1,
            Convert.FromHexString("EA680FC2034A01"),
            17, 8, 2);

        var wrongBuild = PublicBetaCompatibilityGameplayWireAdapter.EncodeNpcSpawn(
            "unsupported", GameplayProtocolState.World, layout);
        var wrongState = PublicBetaCompatibilityGameplayWireAdapter.EncodeNpcSpawn(
            OfficialBattleCommandWireCodec.ClientBuildId, GameplayProtocolState.Battle, layout);
        var badOpaque = PublicBetaCompatibilityGameplayWireAdapter.EncodeNpcSpawn(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            layout with { RawOpaqueBytes = new byte[6] });

        Assert.Equal(OfficialPlayerSnapshotWireResultCode.BuildMismatch, wrongBuild.Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidState, wrongState.Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, badOpaque.Code);
        Assert.Null(wrongBuild.Value);
        Assert.Null(wrongState.Value);
        Assert.Null(badOpaque.Value);

        var selectorWrongBuild = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalCatalogSelector(
            "unsupported", GameplayProtocolState.World, 0);
        var flagsWrongState = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalUiFlags(
            OfficialBattleCommandWireCodec.ClientBuildId, GameplayProtocolState.Battle, 0);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.BuildMismatch, selectorWrongBuild.Code);
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.InvalidState, flagsWrongState.Code);

        var progressOutOfRange = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerGodProgress(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerGodProgressLayout(uint.MaxValue, 0, 0, 0, 0, 0));
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, progressOutOfRange.Code);
        Assert.Null(progressOutOfRange.Value);

        var vitalsOutOfRange = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerVitals(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerVitalsLayout(205, 54, 204, 54));
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, vitalsOutOfRange.Code);
        Assert.Null(vitalsOutOfRange.Value);

        var statusOutOfRange = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerStatus(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerStatusLayout(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1));
        Assert.Equal(OfficialPlayerSnapshotWireResultCode.SemanticEvidenceBlocked, statusOutOfRange.Code);
        Assert.Null(statusOutOfRange.Value);
    }

    [Fact]
    public void Player_derived_stats_snapshot_encodes_local_player_panel_update_payload()
    {
        var descriptor = PublicBetaCompatibilityGameplayWireAdapter.CurrentServerOutputs
            .Single(value => value.CanonicalContract == "player_derived_stats_snapshot");

        Assert.Equal(OfficialPlayerDerivedStatsWireCodec.ApplicationRecordLength, descriptor.CurrentApplicationRecordLength);
        var result = OfficialPlayerDerivedStatsWireCodec.EncodeCanonicalLayout(
            OfficialPlayerDerivedStatsWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerDerivedStatsLayout(
                205,
                24,
                813,
                613,
                57,
                57,
                57,
                57,
                57,
                0x0016,
                0,
                94,
                102,
                185,
                318,
                string.Empty,
                string.Empty,
                false));

        Assert.True(result.LayoutDecoded, result.FailureCode);
        Assert.Equal(OfficialPlayerDerivedStatsWireCodec.ApplicationRecordLength, result.Value!.Length);
        Assert.Equal(OfficialPlayerDerivedStatsWireCodec.Opcode, result.Value[0]);
        Assert.Equal("player_derived_stats_snapshot", descriptor.CanonicalContract);
        Assert.Equal(nameof(OfficialPlayerDerivedStatsWireCodec) + ".EncodeCanonicalLayout", descriptor.CurrentWriter);
    }
}
