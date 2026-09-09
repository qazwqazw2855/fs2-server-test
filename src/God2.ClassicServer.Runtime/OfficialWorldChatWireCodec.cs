using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public enum OfficialWorldChatWireResultCode
{
    Success,
    BuildMismatch,
    InvalidState,
    InvalidLength,
    InvalidOpcode,
    InvalidChecksum,
    UnsupportedChannel,
    UnsupportedTarget,
    InvalidText
}

public sealed record OfficialWorldChatWireResult<T>(
    OfficialWorldChatWireResultCode Code,
    T? Value,
    string FailureCode)
{
    public bool Succeeded => Code == OfficialWorldChatWireResultCode.Success;

    public static OfficialWorldChatWireResult<T> Success(T value) =>
        new(OfficialWorldChatWireResultCode.Success, value, string.Empty);

    public static OfficialWorldChatWireResult<T> Failure(
        OfficialWorldChatWireResultCode code,
        string failureCode) =>
        new(code, default, failureCode);
}

public sealed record OfficialWorldChatRequest(
    byte ChannelType,
    byte EncodingFlag,
    byte OpaqueClientByte,
    ushort Target,
    string RuntimeText,
    string DecodedFrameSha256,
    string EvidenceId);

public sealed record OfficialWorldChatProjection(
    ReadOnlyMemory<byte> DecodedFrame,
    uint SenderCharacterId,
    byte ChannelType,
    byte EncodingFlag,
    string EvidenceId);

/// <summary>
/// Exact-current-client codec for the minimum evidence-complete general-chat slice.
/// The client producer at RVA 0x000AFBF0 proves C2S opcode 0x2F, its variable-length
/// envelope, low-seven-bit channel type, target word, and null-terminated text. The
/// world dispatcher maps S2C 0x5C to RVA 0x00090A48, which consumes sender id at
/// opcode+5, channel flags at opcode+9, and text at opcode+13. Only the default
/// general channel (type 0) is enabled; party, targeted/private, and special types
/// remain evidence-blocked. The public-beta 0x2F producer independently matches the
/// embedded length, flag/reserved/context offsets and NUL-terminated body boundary.
/// </summary>
public static class OfficialWorldChatWireCodec
{
    public const string ClientBuildId = OfficialNpcReplicationWireCodec.ClientBuildId;
    public const string ClientSha256 = OfficialNpcReplicationWireCodec.ClientSha256;
    public const string EvidenceId =
        "God2_opt:rva-0x000AFBF0->0x0007FC10+C2S-0x2F-14->S2C-0x5C-28+rva-0x00090A48;" +
        "FS2TW-public-beta@" + OfficialPublicBetaCrossVersionEvidence.ArchiveSha256 +
        ":legacy_text_protocol/0x2F-offsets3-7-body-corroboration";
    public const byte RequestOpcode = 0x2F;
    public const byte ResponseOpcode = 0x5C;
    public const byte GeneralChannelType = 0;
    public const ushort UntargetedRecipient = ushort.MaxValue;
    public const int MinimumRequestFrameLength = 12;
    public const int MaximumClientTextBytes = 254;
    public const int MaximumRequestFrameLength = MaximumClientTextBytes + 11;
    public const int ResponseTextOffset = 15;

    public static bool RecognizesEncodedFrame(ReadOnlySpan<byte> encodedFrame)
    {
        if (encodedFrame.Length is < MinimumRequestFrameLength or > MaximumRequestFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(encodedFrame) != encodedFrame.Length)
        {
            return false;
        }

        var decoded = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame);
        try
        {
            return decoded[2] == RequestOpcode;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static OfficialWorldChatWireResult<OfficialWorldChatRequest> DecodeRequest(
        string clientBuildId,
        GameplayProtocolState state,
        ReadOnlySpan<byte> decodedFrame)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.BuildMismatch,
                "wire.chat.client_build_mismatch");
        }

        if (state != GameplayProtocolState.World)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidState,
                "wire.chat.state_invalid");
        }

        if (decodedFrame.Length is < MinimumRequestFrameLength or > MaximumRequestFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame) != decodedFrame.Length ||
            BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[3..]) != decodedFrame.Length - 4)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidLength,
                "wire.chat.request_length_invalid");
        }

        if (decodedFrame[2] != RequestOpcode)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidOpcode,
                "wire.chat.request_opcode_invalid");
        }

        if (decodedFrame[^1] != OfficialLoginWireTransform.ComputeChecksum(decodedFrame))
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidChecksum,
                "wire.chat.request_checksum_invalid");
        }

        var flags = decodedFrame[5];
        var channelType = (byte)(flags & 0x7F);
        if (channelType != GeneralChannelType)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.UnsupportedChannel,
                "wire.chat.channel_evidence_blocked");
        }

        var target = BinaryPrimitives.ReadUInt16LittleEndian(decodedFrame[7..]);
        if (target != UntargetedRecipient)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.UnsupportedTarget,
                "wire.chat.target_evidence_blocked");
        }

        var textField = decodedFrame[9..^1];
        if (textField.Length < 2 || textField[^1] != 0 || textField[..^1].Contains((byte)0))
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidText,
                "wire.chat.text_termination_invalid");
        }

        var textBytes = textField[..^1];
        if (textBytes.ContainsAnyInRange((byte)0x00, (byte)0x1F) || textBytes.Contains((byte)0x7F))
        {
            return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Failure(
                OfficialWorldChatWireResultCode.InvalidText,
                "wire.chat.text_control_byte_invalid");
        }

        return OfficialWorldChatWireResult<OfficialWorldChatRequest>.Success(
            new OfficialWorldChatRequest(
                channelType,
                (byte)(flags & 0x80),
                decodedFrame[6],
                target,
                DecodeOpaqueClientText(textBytes),
                Convert.ToHexString(SHA256.HashData(decodedFrame)),
                EvidenceId));
    }

    public static OfficialWorldChatWireResult<OfficialWorldChatProjection> SerializeGeneralMessage(
        long senderCharacterId,
        string senderName,
        string runtimeText,
        byte encodingFlag)
    {
        if (senderCharacterId is <= 0 or > uint.MaxValue)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatProjection>.Failure(
                OfficialWorldChatWireResultCode.InvalidText,
                "wire.chat.sender_identity_invalid");
        }

        if (string.IsNullOrWhiteSpace(senderName) ||
            senderName.Any(value => value is < '\u0021' or > '\u007E' or ':'))
        {
            return OfficialWorldChatWireResult<OfficialWorldChatProjection>.Failure(
                OfficialWorldChatWireResultCode.InvalidText,
                "wire.chat.sender_name_invalid");
        }

        byte[] messageBytes;
        try
        {
            messageBytes = EncodeOpaqueClientText(runtimeText);
        }
        catch (ArgumentException)
        {
            return OfficialWorldChatWireResult<OfficialWorldChatProjection>.Failure(
                OfficialWorldChatWireResultCode.InvalidText,
                "wire.chat.runtime_text_invalid");
        }

        var prefix = Encoding.ASCII.GetBytes(senderName + ": ");
        var textLength = checked(prefix.Length + messageBytes.Length);
        var frameLength = checked(ResponseTextOffset + textLength + 2);
        if (messageBytes.Length == 0 || frameLength > 0x0BFF)
        {
            Array.Clear(messageBytes);
            return OfficialWorldChatWireResult<OfficialWorldChatProjection>.Failure(
                OfficialWorldChatWireResultCode.InvalidLength,
                "wire.chat.response_length_invalid");
        }

        var decoded = new byte[frameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)frameLength));
        decoded[2] = ResponseOpcode;
        BinaryPrimitives.WriteUInt16LittleEndian(decoded.AsSpan(3), checked((ushort)(frameLength - 3)));
        // Exact handler 0x00090A48 does not consume decoded offsets 5..6 or 12..14
        // for the default channel. They stay zero instead of acquiring semantics.
        BinaryPrimitives.WriteUInt32LittleEndian(decoded.AsSpan(7), checked((uint)senderCharacterId));
        decoded[11] = (byte)((encodingFlag & 0x80) | GeneralChannelType);
        prefix.CopyTo(decoded, ResponseTextOffset);
        messageBytes.CopyTo(decoded, ResponseTextOffset + prefix.Length);
        decoded[^2] = 0;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        Array.Clear(messageBytes);

        return OfficialWorldChatWireResult<OfficialWorldChatProjection>.Success(
            new OfficialWorldChatProjection(
                decoded,
                checked((uint)senderCharacterId),
                GeneralChannelType,
                (byte)(encodingFlag & 0x80),
                EvidenceId));
    }

    internal static string DecodeOpaqueClientText(ReadOnlySpan<byte> bytes)
    {
        var characters = new char[bytes.Length];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            characters[index] = value < 0x80 ? (char)value : (char)(0xE000 + value);
        }

        return new string(characters);
    }

    internal static byte[] EncodeOpaqueClientText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value is >= '\u0020' and <= '\u007E')
            {
                bytes[index] = checked((byte)value);
            }
            else if (value is >= '\uE080' and <= '\uE0FF')
            {
                bytes[index] = checked((byte)(value - 0xE000));
            }
            else
            {
                Array.Clear(bytes);
                throw new ArgumentException("Runtime chat text is not an opaque official-client byte projection.", nameof(text));
            }
        }

        return bytes;
    }
}

public enum OfficialWorldChatTransactionCode
{
    Delivered,
    DuplicateIgnored,
    Rejected
}

public sealed record OfficialWorldChatDelivery(
    string RecipientSessionId,
    ReadOnlyMemory<byte> EncodedResponse);

public sealed record OfficialWorldChatReceipt(
    OfficialWorldChatTransactionCode Code,
    string FailureCode,
    long IngressOrdinal,
    string RequestSha256,
    IReadOnlyList<OfficialWorldChatDelivery> Deliveries,
    bool RuntimeValidated,
    bool NetworkBytesEmitted);

public sealed class OfficialWorldChatClosedLoop
{
    private sealed record Participant(string SessionId, long CharacterId, Guid RuntimeCharacterId, string Name);

    private readonly ServerOwnedChatRuntime _chat;
    private readonly ConcurrentDictionary<string, Participant> _participantsBySession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _sessionByCharacter = [];

    public OfficialWorldChatClosedLoop(ServerOwnedChatRuntime chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
    }

    public int ActiveParticipantCount => _participantsBySession.Count;

    public OperationResult RegisterSession(string sessionId, long characterId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (characterId is <= 0 or > uint.MaxValue ||
            string.IsNullOrWhiteSpace(name) ||
            name.Any(value => value is < '\u0021' or > '\u007E' or ':'))
        {
            return OperationResult.Failure(
                "wire.chat.participant_invalid",
                "Official chat requires the bound unsigned-32-bit character identity and printable ASCII name.",
                sessionId);
        }

        var runtimeCharacterId = ToRuntimeCharacterId(characterId);
        var participant = new Participant(sessionId, characterId, runtimeCharacterId, name);
        _participantsBySession[sessionId] = participant;
        _sessionByCharacter[runtimeCharacterId] = sessionId;
        _chat.RegisterOrUpdate(new ServerChatParticipant(runtimeCharacterId, name, Online: true));
        return OperationResult.Success;
    }

    public void RemoveSession(string sessionId)
    {
        if (!_participantsBySession.TryRemove(sessionId, out var participant))
        {
            return;
        }

        if (_sessionByCharacter.TryRemove(
                new KeyValuePair<Guid, string>(participant.RuntimeCharacterId, sessionId)))
        {
            _chat.Unregister(participant.RuntimeCharacterId);
        }
    }

    public OfficialWorldChatReceipt ExecuteFrame(
        string sessionId,
        long ingressOrdinal,
        ReadOnlyMemory<byte> encodedFrame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (ingressOrdinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ingressOrdinal));
        }

        var decodedFrame = OfficialClientWorldProtocolFrames.DecodeWorldServerPayload(encodedFrame.Span);
        try
        {
            var requestHash = Convert.ToHexString(SHA256.HashData(decodedFrame));
            if (!_participantsBySession.TryGetValue(sessionId, out var sender))
            {
                return Reject(ingressOrdinal, requestHash, "wire.chat.session_not_registered");
            }

            var decoded = OfficialWorldChatWireCodec.DecodeRequest(
                OfficialWorldChatWireCodec.ClientBuildId,
                GameplayProtocolState.World,
                decodedFrame);
            if (!decoded.Succeeded || decoded.Value is null)
            {
                return Reject(ingressOrdinal, requestHash, decoded.FailureCode);
            }

            var request = decoded.Value;
            var result = _chat.Send(new ServerChatCommand(
                $"official-chat:{sessionId}:{ingressOrdinal}",
                sender.RuntimeCharacterId,
                ServerChatChannel.World,
                request.RuntimeText));
            if (result.ResultCode == ServerChatResultCode.DuplicateCompleted)
            {
                return new OfficialWorldChatReceipt(
                    OfficialWorldChatTransactionCode.DuplicateIgnored,
                    string.Empty,
                    ingressOrdinal,
                    requestHash,
                    [],
                    RuntimeValidated: true,
                    NetworkBytesEmitted: false);
            }

            if (result.ResultCode != ServerChatResultCode.Success || result.Message is null)
            {
                return Reject(ingressOrdinal, requestHash, $"chat.{result.ResultCode.ToString().ToLowerInvariant()}");
            }

            var projection = OfficialWorldChatWireCodec.SerializeGeneralMessage(
                sender.CharacterId,
                sender.Name,
                result.Message.Text,
                request.EncodingFlag);
            if (!projection.Succeeded || projection.Value is null)
            {
                return Reject(ingressOrdinal, requestHash, projection.FailureCode);
            }

            var encodedResponse = OfficialClientWorldProtocolFrames.EncodeWorldServerPayload(
                projection.Value.DecodedFrame.Span);
            var deliveries = result.Deliveries
                .Select(value => _sessionByCharacter.TryGetValue(value.RecipientId, out var recipientSessionId)
                    ? new OfficialWorldChatDelivery(recipientSessionId, encodedResponse.ToArray())
                    : null)
                .Where(value => value is not null)
                .Select(value => value!)
                .OrderBy(value => value.RecipientSessionId, StringComparer.Ordinal)
                .ToArray();
            Array.Clear(encodedResponse);
            if (deliveries.Length != result.Deliveries.Count)
            {
                return Reject(ingressOrdinal, requestHash, "wire.chat.recipient_session_missing");
            }

            return new OfficialWorldChatReceipt(
                OfficialWorldChatTransactionCode.Delivered,
                string.Empty,
                ingressOrdinal,
                requestHash,
                deliveries,
                RuntimeValidated: true,
                NetworkBytesEmitted: false);
        }
        finally
        {
            Array.Clear(decodedFrame);
        }
    }

    internal static Guid ToRuntimeCharacterId(long characterId)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, characterId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], 0x474F443243484154UL);
        return new Guid(bytes);
    }

    private static OfficialWorldChatReceipt Reject(long ingressOrdinal, string requestHash, string failureCode) =>
        new(
            OfficialWorldChatTransactionCode.Rejected,
            failureCode,
            ingressOrdinal,
            requestHash,
            [],
            RuntimeValidated: false,
            NetworkBytesEmitted: false);
}
