using God2.ServerV2.Protocol;
using God2.ServerV2.Core;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialPortalWireTests
{
    private const string Build = OfficialPortalWireCodec.ClientBuildId;

    [Fact]
    public void Six_capture_activate_fixture_decodes_and_round_trips()
    {
        var decoded = Convert.FromHexString("08000CA82734FC9C");

        var result = OfficialPortalWireCodec.DecodeActivate(Build, ConnectionStage.InWorld, decoded);
        var serialized = OfficialPortalWireCodec.SerializeActivate(result.Value!);

        Assert.True(result.Succeeded);
        Assert.Equal(OfficialPortalWireCodec.ActivateOpcode, result.Value!.Opcode);
        Assert.Equal("A82734FC9C", Convert.ToHexString(result.Value.PreservedActivationRegion.Span));
        Assert.True(serialized.Succeeded);
        Assert.Equal(decoded, serialized.Value.ToArray());
        Assert.Equal(64, result.Value.DecodedRequestSha256.Length);
    }

    [Theory]
    [InlineData("another-build", ConnectionStage.InWorld, OfficialPortalWireResultCode.BuildMismatch)]
    [InlineData(Build, ConnectionStage.Login, OfficialPortalWireResultCode.InvalidState)]
    [InlineData(Build, ConnectionStage.CharacterSelect, OfficialPortalWireResultCode.InvalidState)]
    public void Activate_is_build_and_state_locked(
        string build,
        ConnectionStage state,
        OfficialPortalWireResultCode expected)
    {
        var result = OfficialPortalWireCodec.DecodeActivate(
            build,
            state,
            Convert.FromHexString("08000CA82734FC9C"));

        Assert.Equal(expected, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Activate_rejects_mutated_preserved_region()
    {
        var frame = Convert.FromHexString("08000CA82734FC9C");
        frame[6] ^= 1;

        var result = OfficialPortalWireCodec.DecodeActivate(Build, ConnectionStage.InWorld, frame);

        Assert.Equal(OfficialPortalWireResultCode.EvidenceMismatch, result.Code);
        Assert.Equal("wire.portal.activate_preserved_region_mismatch", result.FailureCode);
    }

    [Theory]
    [InlineData("07000CA82734FC", OfficialPortalWireResultCode.InvalidLength)]
    [InlineData("08000DA82734FC9C", OfficialPortalWireResultCode.InvalidOpcode)]
    public void Activate_rejects_invalid_envelope_or_opcode(string hex, OfficialPortalWireResultCode expected)
    {
        var result = OfficialPortalWireCodec.DecodeActivate(Build, ConnectionStage.InWorld, Convert.FromHexString(hex));

        Assert.Equal(expected, result.Code);
    }

    [Fact]
    public void Map_three_result_matches_recovered_client_parser_fixture()
    {
        var result = Serialize(map: 3, x: 196, y: 139);

        Assert.True(result.Succeeded);
        Assert.Equal("0600BB0000ED", Convert.ToHexString(result.Value!.PreludeDecodedFrame.Span));
        Assert.Equal("0C0061C400040410031601F7", Convert.ToHexString(result.Value.MapTransitionDecodedFrame.Span));

        var decoded = OfficialPortalWireCodec.DecodeResult(
            result.Value.PreludeDecodedFrame.Span,
            result.Value.MapTransitionDecodedFrame.Span);
        Assert.True(decoded.Succeeded);
        Assert.Equal(4, decoded.Value!.AreaId);
        Assert.Equal(3, decoded.Value.ClientMapId);
        Assert.Equal(196, decoded.Value.X);
        Assert.Equal(139, decoded.Value.Y);
        Assert.Equal(0, decoded.Value.PositionMode);
    }

    [Fact]
    public void Map_nineteen_result_matches_recovered_client_parser_fixture()
    {
        var result = Serialize(map: 19, x: 28, y: 34);

        Assert.True(result.Succeeded);
        Assert.Equal("0600BB880075", Convert.ToHexString(result.Value!.PreludeDecodedFrame.Span));
        Assert.Equal("0C0061C40404047200440087", Convert.ToHexString(result.Value.MapTransitionDecodedFrame.Span));

        var decoded = OfficialPortalWireCodec.DecodeResult(
            result.Value.PreludeDecodedFrame.Span,
            result.Value.MapTransitionDecodedFrame.Span);
        Assert.True(decoded.Succeeded);
        Assert.Equal(19, decoded.Value!.ClientMapId);
        Assert.Equal(28, decoded.Value.X);
        Assert.Equal(34, decoded.Value.Y);
        Assert.Equal(2, decoded.Value.PositionMode);
        Assert.Equal(0x0404, decoded.Value.PreservedOpaqueWord);
    }

    [Theory]
    [InlineData(43, 2, 52, 184, "0C0061C20A0202D300700115", "0600BB0000ED")]
    [InlineData(0, 2, 283, 453, "0C0061020002026D048A0305", "0600BB880075")]
    public void Hongmeng_biyou_results_match_bidirectional_capture_and_transition_first_order(
        ushort map,
        byte area,
        int x,
        int y,
        string expectedTransition,
        string expectedPrelude)
    {
        var result = OfficialPortalWireCodec.SerializeVerifiedClientDestination(map, area, x, y);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedTransition, Convert.ToHexString(result.Value!.MapTransitionDecodedFrame.Span));
        Assert.Equal(expectedPrelude, Convert.ToHexString(result.Value.PreludeDecodedFrame.Span));
        Assert.Equal(expectedTransition, Convert.ToHexString(result.Value.OrderedDecodedFrames[0].Span));
        Assert.Equal(expectedPrelude, Convert.ToHexString(result.Value.OrderedDecodedFrames[1].Span));

        var decoded = OfficialPortalWireCodec.DecodeResult(
            result.Value.PreludeDecodedFrame.Span,
            result.Value.MapTransitionDecodedFrame.Span);
        Assert.True(decoded.Succeeded);
        Assert.Equal(map, decoded.Value!.ClientMapId);
        Assert.Equal(area, decoded.Value.AreaId);
        Assert.Equal(x, decoded.Value.X);
        Assert.Equal(y, decoded.Value.Y);
        Assert.Equal(0x0202, decoded.Value.PreservedOpaqueWord);
    }

    [Fact]
    public void Stage_three_map_seven_result_replays_the_live_transition_exactly()
    {
        var result = OfficialPortalWireCodec.SerializeVerifiedClientDestination(7, 15, 48, 81);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "0C0061CF010F0FC000A20051",
            Convert.ToHexString(result.Value!.MapTransitionDecodedFrame.Span));
        Assert.Equal(
            "0C0061CF010F0FC000A20051",
            Convert.ToHexString(result.Value.OrderedDecodedFrames[0].Span));

        var decoded = OfficialPortalWireCodec.DecodeResult(
            result.Value.PreludeDecodedFrame.Span,
            result.Value.MapTransitionDecodedFrame.Span);
        Assert.True(decoded.Succeeded);
        Assert.Equal(7, decoded.Value!.ClientMapId);
        Assert.Equal(15, decoded.Value.AreaId);
        Assert.Equal(48, decoded.Value.X);
        Assert.Equal(81, decoded.Value.Y);
        Assert.Equal(0, decoded.Value.PositionMode);
        Assert.Equal(0x0F0F, decoded.Value.PreservedOpaqueWord);
    }

    [Fact]
    public void Stage_three_source_identity_coexists_with_map_zero_area_two_and_replays_sequence_14880_exactly()
    {
        var stageThreeSource = OfficialPortalWireCodec.SerializeVerifiedClientDestination(0, 15, 248, 247);
        var areaTwoDestination = OfficialPortalWireCodec.SerializeVerifiedClientDestination(0, 2, 283, 453);

        Assert.True(stageThreeSource.Succeeded);
        Assert.Equal(
            "0C00610F000F0FE103EE0101",
            Convert.ToHexString(stageThreeSource.Value!.MapTransitionDecodedFrame.Span));
        Assert.True(areaTwoDestination.Succeeded);
        Assert.Equal((byte)15, stageThreeSource.Value.Destination.AreaId);
        Assert.Equal((byte)2, areaTwoDestination.Value!.Destination.AreaId);
    }

    [Theory]
    [InlineData(4, 28, 34)]
    [InlineData(19, 29, 34)]
    [InlineData(3, 196, 138)]
    public void Serializer_rejects_unverified_destination_or_coordinates(int map, int x, int y)
    {
        var result = Serialize(map, x, y);

        Assert.Equal(OfficialPortalWireResultCode.UnsupportedDestination, result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(0, 0, "0C0061C404040402000000D3")]
    [InlineData(29, 35, "0C0061C4040404760046008D")]
    [InlineData(32767, 32767, "0C0061C4040404FEFFFFFFCC")]
    public void Test_only_position_evidence_serializer_projects_static_consumer_fields(
        int x,
        int y,
        string expectedTransition)
    {
        var result = OfficialPortalWireCodec.SerializeTestOnlyMap19PositionEvidence(Build, x, y);

        Assert.True(result.Succeeded);
        Assert.Equal("0600BB880075", Convert.ToHexString(result.Value!.PreludeDecodedFrame.Span));
        Assert.Equal(expectedTransition, Convert.ToHexString(result.Value.MapTransitionDecodedFrame.Span));
        Assert.Equal(x, result.Value.Destination.X);
        Assert.Equal(y, result.Value.Destination.Y);
        Assert.Equal(OfficialPortalWireCodec.ClientPositionConsumerEvidence, result.Value.Destination.EvidenceId);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(32768, 0)]
    [InlineData(0, 32768)]
    public void Test_only_position_evidence_serializer_rejects_out_of_range_values(int x, int y)
    {
        var result = OfficialPortalWireCodec.SerializeTestOnlyMap19PositionEvidence(Build, x, y);

        Assert.Equal(OfficialPortalWireResultCode.Malformed, result.Code);
        Assert.Equal("wire.portal.position_evidence_range_invalid", result.FailureCode);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(19, 4, 29, 35, "0600BB880075", "0C0061C4040404760046008D")]
    [InlineData(3, 4, 195, 138, "0600BB0000ED", "0C0061C40004040C031401F1")]
    [InlineData(0, 15, 248, 247, "0600BB0000ED", "0C00610F000F0FE103EE0101")]
    public void World_projection_serializer_projects_arbitrary_in_bounds_coordinates_for_promoted_maps(
        int map,
        byte area,
        int x,
        int y,
        string expectedPrelude,
        string expectedTransition)
    {
        var result = OfficialPortalWireCodec.SerializeWorldProjectionDestination(
            Build,
            checked((ushort)map),
            area,
            x,
            y);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedPrelude, Convert.ToHexString(result.Value!.PreludeDecodedFrame.Span));
        Assert.Equal(expectedTransition, Convert.ToHexString(result.Value.MapTransitionDecodedFrame.Span));
        Assert.Equal(OfficialPortalWireCodec.ClientPositionConsumerEvidence, result.Value.Destination.EvidenceId);
    }

    [Theory]
    [InlineData("wrong-build", 19, 4, 29, 35, "wire.world_projection.client_build_mismatch")]
    [InlineData(Build, 4, 4, 29, 35, "wire.world_projection.map_identity_not_verified")]
    [InlineData(Build, 19, 5, 29, 35, "wire.world_projection.map_identity_not_verified")]
    [InlineData(Build, 19, 4, 32768, 35, "wire.world_projection.position_range_invalid")]
    public void World_projection_serializer_fails_closed_outside_build_map_area_and_packed_position_evidence(
        string build,
        int map,
        byte area,
        int x,
        int y,
        string expectedFailure)
    {
        var result = OfficialPortalWireCodec.SerializeWorldProjectionDestination(
            build,
            checked((ushort)map),
            area,
            x,
            y);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedFailure, result.FailureCode);
    }

    [Fact]
    public void Serializer_rejects_non_success_result_and_missing_correlation()
    {
        var failed = OfficialPortalWireCodec.SerializeResult(new OfficialPortalAuthoritativeResult(
            Build, 3, 196, 139, "Rejected", "correlation"));
        var missingCorrelation = OfficialPortalWireCodec.SerializeResult(new OfficialPortalAuthoritativeResult(
            Build, 3, 196, 139, "Success", ""));

        Assert.Equal(OfficialPortalWireResultCode.Malformed, failed.Code);
        Assert.Equal(OfficialPortalWireResultCode.Malformed, missingCorrelation.Code);
    }

    [Fact]
    public void Result_decoder_rejects_checksum_and_opaque_mutations()
    {
        var result = Serialize(3, 196, 139).Value!;
        var checksumMutation = result.MapTransitionDecodedFrame.ToArray();
        checksumMutation[^1] ^= 1;
        var opaqueMutation = result.MapTransitionDecodedFrame.ToArray();
        opaqueMutation[5] ^= 1;
        opaqueMutation[^1] = OfficialPortalWireCodec.AdditiveChecksum(opaqueMutation.AsSpan(0, opaqueMutation.Length - 1));

        var checksumRejected = OfficialPortalWireCodec.DecodeResult(result.PreludeDecodedFrame.Span, checksumMutation);
        var opaqueRejected = OfficialPortalWireCodec.DecodeResult(result.PreludeDecodedFrame.Span, opaqueMutation);

        Assert.Equal(OfficialPortalWireResultCode.Malformed, checksumRejected.Code);
        Assert.Equal(OfficialPortalWireResultCode.EvidenceMismatch, opaqueRejected.Code);
    }

    [Fact]
    public void Authoritative_serializer_model_has_no_captured_packet_or_fixed_response_identifier()
    {
        var properties = typeof(OfficialPortalAuthoritativeResult).GetProperties().Select(value => value.Name).ToArray();

        Assert.DoesNotContain(properties, value => value.Contains("Captured", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, value => value.Contains("Fixed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(OfficialPortalAuthoritativeResult).GetProperties(), value => value.PropertyType == typeof(byte[]));
    }

    private static OfficialPortalWireResult<OfficialPortalSerializedFrames> Serialize(int map, int x, int y) =>
        OfficialPortalWireCodec.SerializeResult(new OfficialPortalAuthoritativeResult(
            Build,
            map,
            x,
            y,
            "Success",
            "portal-test"));
}
