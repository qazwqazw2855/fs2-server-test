using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.GameplayContentRecovery;

internal static class God2ConVerifier
{
    internal const int PackedLength = 904;
    internal const int DecodedLength = 1300;

    internal const string SourceHash =
        "8A498103D8A391AC80D49C16572C1140C485F3FCD48A8EC7798CE00CAC9284FE";

    internal const string DecodedHash =
        "489BCE633ED2267A52730E6905163491AFCC2343DC2510DEC1ECB316F1CD80A1";

    internal static byte[] Verify(byte[] packed)
    {
        Require(packed.Length == PackedLength, "Source length mismatch.");

        Require(
            Convert.ToHexString(SHA256.HashData(packed)) == SourceHash,
            "Source SHA256 mismatch.");

        Require(
            packed[0] == 0x05 &&
            packed[1] == 0x16 &&
            packed[6] == 0x5A &&
            BinaryPrimitives.ReadInt32LittleEndian(
                packed.AsSpan(2, 4)) == DecodedLength,
            "Header mismatch.");

        PackedConsumptionVerifier.Verify(packed);

        var decoded = God2PackedFile.Decode(packed);

        Require(decoded.Length == DecodedLength, "Decoded length mismatch.");

        Require(
            Convert.ToHexString(SHA256.HashData(decoded)) == DecodedHash,
            "Decoded SHA256 mismatch.");

        return decoded;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}
