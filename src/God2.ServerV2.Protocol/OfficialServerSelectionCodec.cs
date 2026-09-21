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

        // Current God2_opt.exe (2026-09-20 runtime evidence):
        // wire 06 00 04 B0 B8 76
        // decoded 06 00 A6 00 00 D8
        var currentDecoded =
            CurrentClientServerWireTransform.Decode(encodedFrame);

        try
        {
            if (currentDecoded[2] == RequestOpcode &&
                currentDecoded[3] == 0 &&
                currentDecoded[4] == 0x00 &&
                currentDecoded[^1] ==
                    CurrentClientServerWireTransform.ComputeChecksum(
                        currentDecoded))
            {
                request =
                    new OfficialServerSelectionRequest(currentDecoded[4]);
                return true;
            }
        }
        finally
        {
            Array.Clear(currentDecoded);
        }

        // Preserve the previously verified legacy protocol.
        var legacyDecoded = OfficialLoginWireTransform.Decode(encodedFrame);

        try
        {
            if (legacyDecoded[2] != RequestOpcode ||
                legacyDecoded[3] != 0 ||
                legacyDecoded[4] != SupportedServerId ||
                legacyDecoded[^1] !=
                    OfficialLoginWireTransform.ComputeChecksum(legacyDecoded))
            {
                return false;
            }

            request =
                new OfficialServerSelectionRequest(legacyDecoded[4]);
            return true;
        }
        finally
        {
            Array.Clear(legacyDecoded);
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

    public static byte[] EncodeCurrentClientCharacterList(
        string? characterName,
        string? classCode)
    {
        var legacyEncoded = EncodeCharacterList(characterName, classCode);
        var decoded = OfficialLoginWireTransform.Decode(legacyEncoded);

        try
        {
            decoded[^1] =
                CurrentClientServerWireTransform.ComputeChecksum(decoded);

            return CurrentClientServerWireTransform.Encode(decoded);
        }
        finally
        {
            Array.Clear(legacyEncoded);
            Array.Clear(decoded);
        }
    }
}
