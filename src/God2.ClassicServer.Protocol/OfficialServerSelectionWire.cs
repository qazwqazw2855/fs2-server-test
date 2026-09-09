using System.Buffers.Binary;
using System.Text;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Protocol;

public static class OfficialLoginWireTransform
{
    private static readonly byte[] CipherKey =
        Convert.FromHexString(
            "405FD0401BB55367D34D90DF1D929883DDAB6D59B34768B050422A49D3AE0AA8E11D9C3FB33CB450E3306C5B167FC8DA067A180A92E1FEC5873871F451B7614BFEB01C0F523350F48F67128A26737E00BAEB20E33370F2018B0967D27510083F6A99DD5CB8156E514CADCAC15F2B288B80654C8EA0DE8D8BE31ED48C46A147ABAD3EA7CFEEC85692A16A5EA8CA8F0EA3E15066B3E111148C17DE80C9CC5366B74D09410FFB3650DD160694E66E88776F631534F9FCF3D22BB0B03232100CAC8DD46175C6E745A45B34E9352453FCAD17901A7E09FB6A0E9135FEB46F19B56252C9AD0999BBDF9933827B080982D5F6C4F0C80E8BE6600FE52E2FCC27EF37D031");

    public static byte[] Decode(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var previousPlain = 0xAF;
        for (var index = 2; index < decoded.Length; index++)
        {
            var encrypted = (decoded[index] + 3 - previousPlain) & 0xFF;
            var plain = CipherKey[(index - 2) & 0xFF] ^ encrypted;
            decoded[index] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    public static byte[] Encode(ReadOnlySpan<byte> decoded)
    {
        var encoded = decoded.ToArray();
        var previousPlain = 0xAF;
        for (var index = 2; index < encoded.Length; index++)
        {
            var plain = decoded[index];
            var encrypted = ((CipherKey[(index - 2) & 0xFF] ^ plain) + previousPlain - 3) & 0xFF;
            encoded[index] = (byte)encrypted;
            previousPlain = plain;
        }

        return encoded;
    }

    public static byte ComputeChecksum(ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < 2)
        {
            throw new ArgumentException("A login frame must contain a length header and checksum byte.", nameof(decoded));
        }

        var checksum = 0;
        for (var index = 0; index < decoded.Length - 1; index++)
        {
            checksum = (checksum + decoded[index] + 0x3C) & 0xFF;
        }

        return (byte)checksum;
    }
}

public sealed record OfficialServerSelectionWireRequest(byte SelectedServerId);

public sealed record OfficialCharacterListBootstrapWireModel(
    string CharacterName,
    string OpaquePrefixHex,
    string OpaqueSuffixHex);

public static class OfficialServerSelectionWireCodec
{
    public const string ClientBuildId = "god2-opt-6b127086e0c0";
    public const string ClientExecutableSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const byte RequestOpcode = 0xA6;
    public const byte ResponseOpcode = 0x07;
    public const byte SupportedServerId = 0x01;
    public const int RequestFrameLength = 6;
    public const int ResponseFrameLength = 78;
    public const int CharacterNameOffset = 5;
    public const int CharacterNameLength = 16;

    private const string GoldenEncodedRequestHex = "06009202CE97";
    private const string GoldenEncodedResponseHex =
        "4E00F361CF28E629ABD3D04A8DDC1A8F9580DAA86A58B04269CDAD705451512C68067F89CA3AB032C05F01328E99537A271A68A915078FDEFBC1833A70D26EA3835B9C0E3A2B70CFEE0D9061123D";
    private const string GoldenOpaquePrefixHex = "0200";
    private const string GoldenOpaqueSuffixHex =
        "020204006000310C018001600160310002020F00323134313030323037320000000000000102030120001114016001200120812004000300";

    // Existing current-build evidence: the character-delete/create run produced
    // an empty-slot response followed by an exact one-character response. The
    // decoded one-character frame has SHA-256
    // A8C099DA3E66043074859C64F1916A7BAF1103385A9770AEBC9610A409B8BF30.
    // Prefix 0x0001 is the observed active-character count. The first 20-byte
    // record tail and the complete unused 36-byte slot are preserved verbatim.
    private const string SingleCharacterOpaquePrefixHex = "0100";
    private const string SingleCharacterOpaqueSuffixHex =
        "0106000020007124010001200120812004000300" +
        "00000000000000000000000000000000" +
        "0102030020001114016001200120812004000300";

    // The historical Map 19 profile rendered the first slot as 劍客. Its
    // 20-byte record tail is paired with the current-build proven empty second
    // slot so the active-character count remains exactly one.
    private const string SwordsmanSingleCharacterOpaqueSuffixHex =
        "020204006000310C018001600160310002020F00" +
        "00000000000000000000000000000000" +
        "0102030020001114016001200120812004000300";

    // Current-build dynamic-key trace, decoded S2C opcode 0x07, length 78,
    // SHA-256 1E087135069D6891DD725F8CF00FB41C591F829EDC1CEA0577CA6B4E36B09567.
    // The zero-character response preserves the observed first empty-slot tail
    // and leaves the second slot completely zeroed.
    private const string EmptyCharacterOpaquePrefixHex = "0000";
    private const string EmptyCharacterOpaqueSuffixHex =
        "0102030020001114016001200120812004000300" +
        "000000000000000000000000000000000000000000000000000000000000000000000000";

    public static ReadOnlyMemory<byte> GoldenEncodedRequest => Convert.FromHexString(GoldenEncodedRequestHex);

    public static ReadOnlyMemory<byte> GoldenEncodedResponse => Convert.FromHexString(GoldenEncodedResponseHex);

    public static OfficialCharacterListBootstrapWireModel GoldenResponseModel() =>
        new("kero", GoldenOpaquePrefixHex, GoldenOpaqueSuffixHex);

    public static OfficialCharacterListBootstrapWireModel SingleCharacterResponseModel() =>
        new("kero", SingleCharacterOpaquePrefixHex, SingleCharacterOpaqueSuffixHex);

    public static OfficialCharacterListBootstrapWireModel EmptyCharacterResponseModel() =>
        new(string.Empty, EmptyCharacterOpaquePrefixHex, EmptyCharacterOpaqueSuffixHex);

    public static OperationResult<OfficialCharacterListBootstrapWireModel> SingleCharacterResponseModel(
        string characterClass)
    {
        if (string.Equals(characterClass, "Swordsman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(characterClass, "Class1", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<OfficialCharacterListBootstrapWireModel>.Success(
                new("kero", SingleCharacterOpaquePrefixHex, SwordsmanSingleCharacterOpaqueSuffixHex));
        }

        return OperationResult<OfficialCharacterListBootstrapWireModel>.Failure(
            "protocol.character_list.class_profile_evidence_blocked",
            "The selected class does not have an evidence-pinned official single-character record profile.",
            characterClass);
    }

    public static OperationResult<OfficialServerSelectionWireRequest> DecodeRequest(
        ReadOnlySpan<byte> encodedFrame,
        ProtocolStage stage,
        string clientBuildId)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure<OfficialServerSelectionWireRequest>(
                "protocol.server_selection.build_mismatch",
                "Server-selection evidence is not valid for the supplied client build.");
        }

        if (stage != ProtocolStage.CharacterList)
        {
            return Failure<OfficialServerSelectionWireRequest>(
                "protocol.server_selection.stage_invalid",
                "Server selection is only valid in CharacterList stage.");
        }

        if (encodedFrame.Length != RequestFrameLength)
        {
            return Failure<OfficialServerSelectionWireRequest>(
                "protocol.server_selection.length_invalid",
                $"Server-selection frame must be exactly {RequestFrameLength} bytes.");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(encodedFrame) != RequestFrameLength)
        {
            return Failure<OfficialServerSelectionWireRequest>(
                "protocol.server_selection.envelope_length_invalid",
                "The little-endian envelope length does not match the received frame.");
        }

        var decoded = OfficialLoginWireTransform.Decode(encodedFrame);
        try
        {
            if (decoded[2] != RequestOpcode)
            {
                return Failure<OfficialServerSelectionWireRequest>(
                    "protocol.server_selection.opcode_invalid",
                    "Decoded opcode is not the verified server-selection opcode.");
            }

            if (decoded[3] != 0)
            {
                return Failure<OfficialServerSelectionWireRequest>(
                    "protocol.server_selection.reserved_invalid",
                    "The verified reserved byte must remain zero.");
            }

            if (decoded[4] != SupportedServerId)
            {
                return Failure<OfficialServerSelectionWireRequest>(
                    "protocol.server_selection.server_id_invalid",
                    "The requested server identifier is outside the evidence-verified enum domain.");
            }

            if (decoded[^1] != OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return Failure<OfficialServerSelectionWireRequest>(
                    "protocol.server_selection.checksum_invalid",
                    "The decoded frame checksum is invalid.");
            }

            return OperationResult<OfficialServerSelectionWireRequest>.Success(
                new OfficialServerSelectionWireRequest(decoded[4]));
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static OperationResult<ReadOnlyMemory<byte>> SerializeRequest(OfficialServerSelectionWireRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SelectedServerId != SupportedServerId)
        {
            return Failure<ReadOnlyMemory<byte>>(
                "protocol.server_selection.server_id_invalid",
                "Only the evidence-verified server identifier can be serialized.");
        }

        byte[] decoded = [RequestFrameLength, 0, RequestOpcode, 0, request.SelectedServerId, 0];
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialLoginWireTransform.Encode(decoded);
        Array.Clear(decoded);
        return OperationResult<ReadOnlyMemory<byte>>.Success(encoded);
    }

    public static OperationResult<OfficialCharacterListBootstrapWireModel> DecodeResponse(
        ReadOnlySpan<byte> encodedFrame,
        string clientBuildId)
    {
        if (!string.Equals(clientBuildId, ClientBuildId, StringComparison.Ordinal))
        {
            return Failure<OfficialCharacterListBootstrapWireModel>(
                "protocol.character_bootstrap.build_mismatch",
                "Character-bootstrap evidence is not valid for the supplied client build.");
        }

        if (encodedFrame.Length != ResponseFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(encodedFrame) != ResponseFrameLength)
        {
            return Failure<OfficialCharacterListBootstrapWireModel>(
                "protocol.character_bootstrap.length_invalid",
                $"Character-bootstrap frame must be exactly {ResponseFrameLength} bytes.");
        }

        var decoded = OfficialLoginWireTransform.Decode(encodedFrame);
        try
        {
            if (decoded[2] != ResponseOpcode)
            {
                return Failure<OfficialCharacterListBootstrapWireModel>(
                    "protocol.character_bootstrap.opcode_invalid",
                    "Decoded opcode is not the verified character-bootstrap opcode.");
            }

            if (decoded[^1] != OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return Failure<OfficialCharacterListBootstrapWireModel>(
                    "protocol.character_bootstrap.checksum_invalid",
                    "The decoded frame checksum is invalid.");
            }

            var nameBytes = decoded.AsSpan(CharacterNameOffset, CharacterNameLength);
            var terminator = nameBytes.IndexOf((byte)0);
            var nameLength = terminator < 0 ? CharacterNameLength : terminator;
            var characterCount = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(3, 2));
            var emptyCharacterList = characterCount == 0 && nameLength == 0;
            if ((!emptyCharacterList && nameLength == 0) ||
                characterCount > 2 ||
                !IsPrintableAscii(nameBytes[..nameLength]) ||
                (terminator >= 0 && nameBytes[(terminator + 1)..].ContainsAnyExcept((byte)0)))
            {
                return Failure<OfficialCharacterListBootstrapWireModel>(
                    "protocol.character_bootstrap.name_invalid",
                    "The fixed-width character-name field is malformed.");
            }

            return OperationResult<OfficialCharacterListBootstrapWireModel>.Success(
                new OfficialCharacterListBootstrapWireModel(
                    Encoding.ASCII.GetString(nameBytes[..nameLength]),
                    Convert.ToHexString(decoded.AsSpan(3, 2)),
                    Convert.ToHexString(decoded.AsSpan(CharacterNameOffset + CharacterNameLength, 56))));
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static OperationResult<ReadOnlyMemory<byte>> SerializeResponse(OfficialCharacterListBootstrapWireModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        byte[] opaquePrefix;
        byte[] opaqueSuffix;
        try
        {
            opaquePrefix = Convert.FromHexString(model.OpaquePrefixHex);
            opaqueSuffix = Convert.FromHexString(model.OpaqueSuffixHex);
        }
        catch (FormatException)
        {
            return Failure<ReadOnlyMemory<byte>>(
                "protocol.character_bootstrap.opaque_invalid",
                "Opaque preservation slices must be valid hexadecimal.");
        }

        if (opaquePrefix.Length != 2 || opaqueSuffix.Length != 56)
        {
            return Failure<ReadOnlyMemory<byte>>(
                "protocol.character_bootstrap.opaque_length_invalid",
                "Opaque preservation slices do not match the verified build-locked layout.");
        }

        if (model.CharacterName.Any(value => value is < '\u0020' or > '\u007E'))
        {
            return Failure<ReadOnlyMemory<byte>>(
                "protocol.character_bootstrap.name_invalid",
                "Character name must contain only printable ASCII characters for this verified wire model.");
        }

        var nameBytes = Encoding.ASCII.GetBytes(model.CharacterName);
        var characterCount = BinaryPrimitives.ReadUInt16LittleEndian(opaquePrefix);
        var emptyCharacterList = characterCount == 0 && nameBytes.Length == 0;
        if (characterCount > 2 ||
            (!emptyCharacterList && nameBytes.Length is < 1 or > CharacterNameLength) ||
            nameBytes.Any(value => value is < 0x20 or > 0x7E))
        {
            return Failure<ReadOnlyMemory<byte>>(
                "protocol.character_bootstrap.name_invalid",
                "Character name must be 1-16 printable ASCII bytes for this verified wire model.");
        }

        var decoded = new byte[ResponseFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, ResponseFrameLength);
        decoded[2] = ResponseOpcode;
        opaquePrefix.CopyTo(decoded, 3);
        nameBytes.CopyTo(decoded, CharacterNameOffset);
        opaqueSuffix.CopyTo(decoded, CharacterNameOffset + CharacterNameLength);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        var encoded = OfficialLoginWireTransform.Encode(decoded);
        Array.Clear(decoded);
        Array.Clear(nameBytes);
        Array.Clear(opaquePrefix);
        Array.Clear(opaqueSuffix);
        return OperationResult<ReadOnlyMemory<byte>>.Success(encoded);
    }

    private static OperationResult<T> Failure<T>(string code, string message) =>
        OperationResult<T>.Failure(code, message, nameof(OfficialServerSelectionWireCodec));

    private static bool IsPrintableAscii(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (character is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
