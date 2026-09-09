using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialServerSelectionWireTests
{
    [Fact]
    public void Golden_request_decodes_to_structured_server_selection()
    {
        var result = OfficialServerSelectionWireCodec.DecodeRequest(
            OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span,
            ProtocolStage.CharacterList,
            OfficialServerSelectionWireCodec.ClientBuildId);

        Assert.True(result.Succeeded);
        Assert.Equal(OfficialServerSelectionWireCodec.SupportedServerId, result.Value!.SelectedServerId);
    }

    [Fact]
    public void Structured_request_serializes_to_golden_official_bytes()
    {
        var result = OfficialServerSelectionWireCodec.SerializeRequest(
            new OfficialServerSelectionWireRequest(OfficialServerSelectionWireCodec.SupportedServerId));

        Assert.True(result.Succeeded);
        Assert.Equal(OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray(), result.Value.ToArray());
    }

    [Fact]
    public void Golden_response_round_trips_through_fields_and_opaque_slices()
    {
        var decoded = OfficialServerSelectionWireCodec.DecodeResponse(
            OfficialServerSelectionWireCodec.GoldenEncodedResponse.Span,
            OfficialServerSelectionWireCodec.ClientBuildId);
        Assert.True(decoded.Succeeded);
        Assert.Equal("kero", decoded.Value!.CharacterName);
        Assert.Equal(4, decoded.Value.OpaquePrefixHex.Length);
        Assert.Equal(112, decoded.Value.OpaqueSuffixHex.Length);

        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(decoded.Value);

        Assert.True(serialized.Succeeded);
        Assert.Equal(OfficialServerSelectionWireCodec.GoldenEncodedResponse.ToArray(), serialized.Value.ToArray());
    }

    [Fact]
    public void Dynamic_name_is_rebuilt_while_opaque_slices_are_preserved()
    {
        var golden = OfficialServerSelectionWireCodec.GoldenResponseModel();
        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(golden with { CharacterName = "ray" });
        Assert.True(serialized.Succeeded);
        Assert.NotEqual(OfficialServerSelectionWireCodec.GoldenEncodedResponse.ToArray(), serialized.Value.ToArray());

        var decoded = OfficialServerSelectionWireCodec.DecodeResponse(
            serialized.Value.Span,
            OfficialServerSelectionWireCodec.ClientBuildId);

        Assert.True(decoded.Succeeded);
        Assert.Equal("ray", decoded.Value!.CharacterName);
        Assert.Equal(golden.OpaquePrefixHex, decoded.Value.OpaquePrefixHex);
        Assert.Equal(golden.OpaqueSuffixHex, decoded.Value.OpaqueSuffixHex);
    }

    [Fact]
    public void Single_character_response_uses_existing_current_build_count_and_unused_slot_evidence()
    {
        var model = OfficialServerSelectionWireCodec.SingleCharacterResponseModel() with
        {
            CharacterName = "G2A"
        };
        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(model);

        Assert.True(serialized.Succeeded);
        var decoded = OfficialLoginWireTransform.Decode(serialized.Value.Span);
        Assert.Equal(1, BitConverter.ToUInt16(decoded, 3));
        Assert.Equal("G2A", System.Text.Encoding.ASCII.GetString(decoded, 5, 3));
        Assert.All(decoded.AsSpan(8, 13).ToArray(), value => Assert.Equal(0, value));
        Assert.All(decoded.AsSpan(41, 16).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(
            Convert.FromHexString("0102030020001114016001200120812004000300"),
            decoded.AsSpan(57, 20).ToArray());
    }

    [Fact]
    public void Empty_character_response_round_trips_the_current_build_empty_slot_evidence()
    {
        var model = OfficialServerSelectionWireCodec.EmptyCharacterResponseModel();
        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(model);

        Assert.True(serialized.Succeeded);
        var decodedBytes = OfficialLoginWireTransform.Decode(serialized.Value.Span);
        Assert.Equal(0, BitConverter.ToUInt16(decodedBytes, 3));
        Assert.All(decodedBytes.AsSpan(5, 16).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(
            Convert.FromHexString("0102030020001114016001200120812004000300"),
            decodedBytes.AsSpan(21, 20).ToArray());
        Assert.All(decodedBytes.AsSpan(41, 36).ToArray(), value => Assert.Equal(0, value));

        var roundTrip = OfficialServerSelectionWireCodec.DecodeResponse(
            serialized.Value.Span,
            OfficialServerSelectionWireCodec.ClientBuildId);
        Assert.True(roundTrip.Succeeded);
        Assert.Equal(string.Empty, roundTrip.Value!.CharacterName);
        Assert.Equal("0000", roundTrip.Value.OpaquePrefixHex);
        Assert.Equal(model.OpaqueSuffixHex, roundTrip.Value.OpaqueSuffixHex);
    }

    [Fact]
    public void Swordsman_single_character_response_uses_verified_map_nineteen_class_profile()
    {
        var model = OfficialServerSelectionWireCodec.SingleCharacterResponseModel("Swordsman");

        Assert.True(model.Succeeded);
        var serialized = OfficialServerSelectionWireCodec.SerializeResponse(
            model.Value! with { CharacterName = "G2A" });
        Assert.True(serialized.Succeeded);
        var decoded = OfficialLoginWireTransform.Decode(serialized.Value.Span);
        Assert.Equal(1, BitConverter.ToUInt16(decoded, 3));
        Assert.Equal(
            Convert.FromHexString("020204006000310C018001600160310002020F00"),
            decoded.AsSpan(21, 20).ToArray());
        Assert.All(decoded.AsSpan(41, 16).ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Unsupported_class_profile_fails_closed()
    {
        var model = OfficialServerSelectionWireCodec.SingleCharacterResponseModel("Unknown");

        Assert.False(model.Succeeded);
        Assert.Equal("protocol.character_list.class_profile_evidence_blocked", model.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(4097)]
    public void Invalid_or_oversized_request_lengths_are_rejected(int length)
    {
        var frame = new byte[length];
        if (length >= 2)
        {
            frame[0] = (byte)(length & 0xFF);
            frame[1] = (byte)(length >> 8);
        }

        var result = OfficialServerSelectionWireCodec.DecodeRequest(
            frame,
            ProtocolStage.CharacterList,
            OfficialServerSelectionWireCodec.ClientBuildId);

        Assert.False(result.Succeeded);
        Assert.Equal("protocol.server_selection.length_invalid", result.Error.Code);
    }

    [Fact]
    public void Envelope_length_mismatch_is_rejected()
    {
        var frame = OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray();
        frame[0] = 5;

        var result = Decode(frame);

        Assert.False(result.Succeeded);
        Assert.Equal("protocol.server_selection.envelope_length_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData(2, 0xA5, "protocol.server_selection.opcode_invalid")]
    [InlineData(3, 0x01, "protocol.server_selection.reserved_invalid")]
    [InlineData(4, 0x02, "protocol.server_selection.server_id_invalid")]
    [InlineData(5, 0x00, "protocol.server_selection.checksum_invalid")]
    public void Semantic_mutations_are_rejected(int decodedOffset, byte value, string expectedCode)
    {
        var decoded = OfficialLoginWireTransform.Decode(OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span);
        decoded[decodedOffset] = value;
        var frame = OfficialLoginWireTransform.Encode(decoded);

        var result = Decode(frame);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Invalid_session_state_is_rejected()
    {
        var result = OfficialServerSelectionWireCodec.DecodeRequest(
            OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span,
            ProtocolStage.InWorld,
            OfficialServerSelectionWireCodec.ClientBuildId);

        Assert.False(result.Succeeded);
        Assert.Equal("protocol.server_selection.stage_invalid", result.Error.Code);
    }

    [Fact]
    public void Build_identity_mismatch_invalidates_both_directions()
    {
        var request = OfficialServerSelectionWireCodec.DecodeRequest(
            OfficialServerSelectionWireCodec.GoldenEncodedRequest.Span,
            ProtocolStage.CharacterList,
            "different-build");
        var response = OfficialServerSelectionWireCodec.DecodeResponse(
            OfficialServerSelectionWireCodec.GoldenEncodedResponse.Span,
            "different-build");

        Assert.False(request.Succeeded);
        Assert.False(response.Succeeded);
        Assert.Contains("build_mismatch", request.Error.Code, StringComparison.Ordinal);
        Assert.Contains("build_mismatch", response.Error.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_length_envelope_rejects_actor_or_target_extensions()
    {
        var extension = OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray().Append((byte)1).ToArray();

        var result = Decode(extension);

        Assert.False(result.Succeeded);
        Assert.Equal("protocol.server_selection.length_invalid", result.Error.Code);
    }

    [Fact]
    public void Opaque_slice_mutation_is_visible_and_checksum_protected()
    {
        var frame = OfficialServerSelectionWireCodec.GoldenEncodedResponse.ToArray();
        var decoded = OfficialLoginWireTransform.Decode(frame);
        decoded[30] ^= 0x01;
        var tampered = OfficialLoginWireTransform.Encode(decoded);

        var rejected = OfficialServerSelectionWireCodec.DecodeResponse(
            tampered,
            OfficialServerSelectionWireCodec.ClientBuildId);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var checksumValid = OfficialServerSelectionWireCodec.DecodeResponse(
            OfficialLoginWireTransform.Encode(decoded),
            OfficialServerSelectionWireCodec.ClientBuildId);

        Assert.False(rejected.Succeeded);
        Assert.Equal("protocol.character_bootstrap.checksum_invalid", rejected.Error.Code);
        Assert.True(checksumValid.Succeeded);
        Assert.NotEqual(
            OfficialServerSelectionWireCodec.GoldenResponseModel().OpaqueSuffixHex,
            checksumValid.Value!.OpaqueSuffixHex);
    }

    [Fact]
    public void Frame_accumulator_preserves_fragmentation_batching_and_order()
    {
        var request = OfficialServerSelectionWireCodec.GoldenEncodedRequest.ToArray();
        var accumulator = new FrameAccumulator();

        var partial = accumulator.Append(request.AsSpan(0, 2));
        var completed = accumulator.Append(request.AsSpan(2));
        var batched = new FrameAccumulator().Append(request.Concat(request).ToArray());

        Assert.Empty(partial.Frames);
        Assert.Single(completed.Frames);
        Assert.Equal(request, completed.Frames[0].Bytes.ToArray());
        Assert.Equal(2, batched.Frames.Count);
        Assert.All(batched.Frames, frame => Assert.Equal(request, frame.Bytes.ToArray()));
    }

    [Fact]
    public void Character_response_rejects_non_ascii_before_encoding_fallback_can_replace_it()
    {
        var model = OfficialServerSelectionWireCodec.SingleCharacterResponseModel() with
        {
            CharacterName = "角色"
        };

        var result = OfficialServerSelectionWireCodec.SerializeResponse(model);

        Assert.False(result.Succeeded);
        Assert.Equal("protocol.character_bootstrap.name_invalid", result.Error.Code);
    }

    private static God2.ClassicServer.Application.Common.OperationResult<OfficialServerSelectionWireRequest> Decode(byte[] frame) =>
        OfficialServerSelectionWireCodec.DecodeRequest(
            frame,
            ProtocolStage.CharacterList,
            OfficialServerSelectionWireCodec.ClientBuildId);
}
