using System.Buffers.Binary;
using System.Text;

namespace God2.ServerV2.Protocol;

public readonly record struct OfficialServerSelectionRequest(
    byte SelectedServerId);

public static class OfficialServerSelectionCodec
{
    public const byte RequestOpcode = 0xA6;
    public const byte ResponseOpcode = 0x07;
    public const byte SupportedServerId = 0x01;
    public const int RequestFrameLength = 6;
    public const int ResponseFrameLength = 78;
    public const int CharacterNameOffset = 5;
    public const int CharacterNameLength = 16;

    private const string SingleCharacterPrefixHex = "0100";
    private const string SingleCharacterSuffixHex =
        "020204006000310C018001600160310002020F00" +
        "00000000000000000000000000000000" +
        "0102030020001114016001200120812004000300";

    private const string EmptyCharacterPrefixHex = "0000";
    private const string EmptyCharacterSuffixHex =
        "0102030020001114016001200120812004000300" +
        "000000000000000000000000000000000000000000000000000000000000000000000000";

    public static bool TryDecodeRequest(
        ReadOnlySpan<byte> encodedFrame,
        out OfficialServerSelectionRequest request)
    {
        request = default;

        if (encodedFrame.Length != RequestFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(encodedFrame) !=
            RequestFrameLength)
        {
            return false;
        }

        var decoded = OfficialLoginWireTransform.Decode(encodedFrame);

        try
        {
            if (decoded[2] != RequestOpcode ||
                decoded[3] != 0 ||
                decoded[4] != SupportedServerId ||
                decoded[^1] !=
                OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return false;
            }

            request = new OfficialServerSelectionRequest(decoded[4]);
            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static byte[] EncodeCharacterList(
        string? characterName,
        string? classCode)
    {
        var empty = string.IsNullOrEmpty(characterName);

        if (!empty &&
            !string.Equals(
                classCode,
                "Swordsman",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                classCode,
                "Class1",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Only the evidence-verified Swordsman profile is supported.");
        }

        var nameBytes = empty
            ? []
            : Encoding.ASCII.GetBytes(characterName!);

        if (nameBytes.Length > CharacterNameLength ||
            nameBytes.Any(value => value is < 0x20 or > 0x7E))
        {
            Array.Clear(nameBytes);
            throw new ArgumentException(
                "Character name must contain 1-16 printable ASCII bytes.",
                nameof(characterName));
        }

        var prefix = Convert.FromHexString(
            empty
                ? EmptyCharacterPrefixHex
                : SingleCharacterPrefixHex);

        var suffix = Convert.FromHexString(
            empty
                ? EmptyCharacterSuffixHex
                : SingleCharacterSuffixHex);

        var decoded = new byte[ResponseFrameLength];

        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(0, 2),
                ResponseFrameLength);

            decoded[2] = ResponseOpcode;
            prefix.CopyTo(decoded, 3);
            nameBytes.CopyTo(decoded, CharacterNameOffset);
            suffix.CopyTo(
                decoded,
                CharacterNameOffset + CharacterNameLength);

            decoded[^1] =
                OfficialLoginWireTransform.ComputeChecksum(decoded);

            return OfficialLoginWireTransform.Encode(decoded);
        }
        finally
        {
            Array.Clear(decoded);
            Array.Clear(nameBytes);
            Array.Clear(prefix);
            Array.Clear(suffix);
        }
    }
}
