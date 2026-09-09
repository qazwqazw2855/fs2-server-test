using System.Buffers.Binary;
using System.Text;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialWorldChatWireTests
{
    [Fact]
    public void Exact_general_request_layout_decodes_without_assigning_opaque_byte_semantics()
    {
        var encoded = BuildEncodedRequest("hello", opaqueClientByte: 0xA7, encodingFlag: 0x80);
        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);

        var result = OfficialWorldChatWireCodec.DecodeRequest(
            OfficialWorldChatWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedFrame);

        Assert.True(result.Succeeded);
        Assert.Equal(OfficialWorldChatWireCodec.GeneralChannelType, result.Value!.ChannelType);
        Assert.Equal(0x80, result.Value.EncodingFlag);
        Assert.Equal(0xA7, result.Value.OpaqueClientByte);
        Assert.Equal(ushort.MaxValue, result.Value.Target);
        Assert.Equal("hello", result.Value.RuntimeText);
        Assert.Equal(OfficialWorldChatWireCodec.EvidenceId, result.Value.EvidenceId);
    }

    [Fact]
    public void Targeted_and_special_chat_types_remain_evidence_blocked()
    {
        var special = DecodeRequest(BuildEncodedRequest("secret", channelType: 2));
        var targeted = DecodeRequest(BuildEncodedRequest("target", target: 7));

        Assert.Equal(OfficialWorldChatWireResultCode.UnsupportedChannel, special.Code);
        Assert.Equal("wire.chat.channel_evidence_blocked", special.FailureCode);
        Assert.Equal(OfficialWorldChatWireResultCode.UnsupportedTarget, targeted.Code);
        Assert.Equal("wire.chat.target_evidence_blocked", targeted.FailureCode);
    }

    [Fact]
    public void Client_text_bytes_round_trip_without_a_windows_code_page_dependency()
    {
        byte[] clientBytes = [0xA4, 0x40, 0x20, 0x81, 0xFE];
        var encoded = BuildEncodedRequest(clientBytes);
        var request = DecodeRequest(encoded);
        Assert.True(request.Succeeded);

        var projection = OfficialWorldChatWireCodec.SerializeGeneralMessage(
            73,
            "Alpha",
            request.Value!.RuntimeText,
            request.Value.EncodingFlag);

        Assert.True(projection.Succeeded);
        var frame = projection.Value!.DecodedFrame.Span;
        Assert.Equal(clientBytes, frame[(OfficialWorldChatWireCodec.ResponseTextOffset + "Alpha: ".Length)..^2].ToArray());
    }

    [Fact]
    public void General_message_projection_matches_exact_client_consumer_offsets()
    {
        var projection = OfficialWorldChatWireCodec.SerializeGeneralMessage(73, "Alpha", "hello", 0x80);

        Assert.True(projection.Succeeded);
        var decoded = projection.Value!.DecodedFrame.Span;
        Assert.Equal(decoded.Length, BinaryPrimitives.ReadUInt16LittleEndian(decoded));
        Assert.Equal(OfficialWorldChatWireCodec.ResponseOpcode, decoded[2]);
        Assert.Equal(decoded.Length - 3, BinaryPrimitives.ReadUInt16LittleEndian(decoded[3..]));
        Assert.Equal((uint)73, BinaryPrimitives.ReadUInt32LittleEndian(decoded[7..]));
        Assert.Equal(0x80, decoded[11]);
        Assert.Equal("Alpha: hello", Encoding.ASCII.GetString(decoded[15..^2]));
        Assert.Equal(0, decoded[^2]);
        Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decoded), decoded[^1]);
    }

    [Fact]
    public void Closed_loop_broadcasts_one_server_frame_to_two_registered_world_sessions()
    {
        var chat = new ServerOwnedChatRuntime(_ => null);
        var loop = new OfficialWorldChatClosedLoop(chat);
        Assert.True(loop.RegisterSession("session-a", 73, "Alpha").Succeeded);
        Assert.True(loop.RegisterSession("session-b", 74, "Beta").Succeeded);

        var receipt = loop.ExecuteFrame("session-a", 1, BuildEncodedRequest("hello"));

        Assert.Equal(OfficialWorldChatTransactionCode.Delivered, receipt.Code);
        Assert.Equal(new[] { "session-a", "session-b" }, receipt.Deliveries.Select(value => value.RecipientSessionId));
        Assert.All(receipt.Deliveries, delivery =>
        {
            var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(delivery.EncodedResponse.Span);
            Assert.Equal(OfficialWorldChatWireCodec.ResponseOpcode, decoded[2]);
            Assert.Equal((uint)73, BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(7)));
            Assert.Equal("Alpha: hello", Encoding.ASCII.GetString(decoded.AsSpan(15, decoded.Length - 17)));
            Assert.Equal(OfficialLoginWireTransform.ComputeChecksum(decoded), decoded[^1]);
        });
    }

    [Fact]
    public void Closed_loop_ignores_identical_replay_and_rejects_changed_replay()
    {
        var loop = new OfficialWorldChatClosedLoop(new ServerOwnedChatRuntime(_ => null));
        Assert.True(loop.RegisterSession("session-a", 73, "Alpha").Succeeded);
        var first = loop.ExecuteFrame("session-a", 8, BuildEncodedRequest("hello"));

        var duplicate = loop.ExecuteFrame("session-a", 8, BuildEncodedRequest("hello"));
        var conflict = loop.ExecuteFrame("session-a", 8, BuildEncodedRequest("changed"));

        Assert.Equal(OfficialWorldChatTransactionCode.Delivered, first.Code);
        Assert.Equal(OfficialWorldChatTransactionCode.DuplicateIgnored, duplicate.Code);
        Assert.Empty(duplicate.Deliveries);
        Assert.Equal(OfficialWorldChatTransactionCode.Rejected, conflict.Code);
        Assert.Equal("chat.replayconflict", conflict.FailureCode);
    }

    [Fact]
    public void Removed_session_is_not_a_later_broadcast_recipient()
    {
        var loop = new OfficialWorldChatClosedLoop(new ServerOwnedChatRuntime(_ => null));
        Assert.True(loop.RegisterSession("session-a", 73, "Alpha").Succeeded);
        Assert.True(loop.RegisterSession("session-b", 74, "Beta").Succeeded);
        loop.RemoveSession("session-b");

        var receipt = loop.ExecuteFrame("session-a", 1, BuildEncodedRequest("hello"));

        Assert.Equal(OfficialWorldChatTransactionCode.Delivered, receipt.Code);
        Assert.Single(receipt.Deliveries);
        Assert.Equal("session-a", receipt.Deliveries[0].RecipientSessionId);
        Assert.Equal(1, loop.ActiveParticipantCount);
    }

    [Fact]
    public void Closing_replaced_session_does_not_unregister_current_character_connection()
    {
        var chat = new ServerOwnedChatRuntime(_ => null);
        var loop = new OfficialWorldChatClosedLoop(chat);
        Assert.True(loop.RegisterSession("session-old", 73, "Alpha").Succeeded);
        Assert.True(loop.RegisterSession("session-new", 73, "Alpha").Succeeded);

        loop.RemoveSession("session-old");
        var receipt = loop.ExecuteFrame("session-new", 1, BuildEncodedRequest("hello"));

        Assert.Equal(OfficialWorldChatTransactionCode.Delivered, receipt.Code);
        Assert.Single(receipt.Deliveries);
        Assert.Equal("session-new", receipt.Deliveries[0].RecipientSessionId);
        Assert.Equal(1, chat.ParticipantCount);
    }

    private static OfficialWorldChatWireResult<OfficialWorldChatRequest> DecodeRequest(byte[] encoded)
    {
        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encoded);
        return OfficialWorldChatWireCodec.DecodeRequest(
            OfficialWorldChatWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decoded);
    }

    private static byte[] BuildEncodedRequest(
        string text,
        byte channelType = OfficialWorldChatWireCodec.GeneralChannelType,
        ushort target = ushort.MaxValue,
        byte opaqueClientByte = 0,
        byte encodingFlag = 0) =>
        BuildEncodedRequest(Encoding.ASCII.GetBytes(text), channelType, target, opaqueClientByte, encodingFlag);

    private static byte[] BuildEncodedRequest(
        byte[] text,
        byte channelType = OfficialWorldChatWireCodec.GeneralChannelType,
        ushort target = ushort.MaxValue,
        byte opaqueClientByte = 0,
        byte encodingFlag = 0)
    {
        var frame = new byte[text.Length + 11];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = OfficialWorldChatWireCodec.RequestOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), checked((ushort)(frame.Length - 4)));
        frame[5] = (byte)((encodingFlag & 0x80) | channelType);
        frame[6] = opaqueClientByte;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), target);
        text.CopyTo(frame, 9);
        frame[^2] = 0;
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return OfficialClientWorldProtocolFrames.EncodeWorldTransportFrame(frame);
    }
}
