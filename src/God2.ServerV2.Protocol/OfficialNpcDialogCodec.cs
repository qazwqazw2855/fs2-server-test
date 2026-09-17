using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ServerV2.Protocol;

public enum OfficialNpcDialogEncodingStatus
{
    Encoded,
    BlockedByEvidence
}

public sealed record OfficialNpcDialogEncodingResult(
    OfficialNpcDialogEncodingStatus Status,
    byte[] Frame,
    string Reason)
{
    public bool Succeeded =>
        Status == OfficialNpcDialogEncodingStatus.Encoded;
}

public static class OfficialNpcDialogCodec
{
    public const string ClientBuildId =
        OfficialNpcSpawnCodec.ClientBuildId;

    public const ushort LiveDialogHandle = 3793;
    public const byte DialogResultOpcode = 0x7A;
    public const int ExactOpenResponseLength = 32;

    public const string EvidenceId =
        "LiveRecovery/attempt-759-decode-2594";

    public const string ExactDecodedHex =
        "20007A1B000100D10EF8280780000000000000000000000000000000005D16F3";

    public const string ApplicationSha256 =
        "4BE23F716235C472597A79803F3CC22A134ED30160F96134FA6613F2F5B2F718";

    public static OfficialNpcDialogEncodingResult
        EncodeOpenResponse(
            string? clientBuildId,
            uint clientEntityHandle)
    {
        if (!string.Equals(
                clientBuildId,
                ClientBuildId,
                StringComparison.Ordinal))
        {
            return Blocked("ClientBuildMismatch");
        }

        if (clientEntityHandle != LiveDialogHandle)
        {
            return Blocked("DialogProfileEvidenceBlocked");
        }

        var decoded =
            Convert.FromHexString(ExactDecodedHex);

        try
        {
            if (decoded.Length != ExactOpenResponseLength ||
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded) != ExactOpenResponseLength ||
                decoded[2] != DialogResultOpcode ||
                BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(7, sizeof(ushort))) !=
                    LiveDialogHandle ||
                decoded[^1] !=
                    OfficialLoginWireTransform.ComputeChecksum(
                        decoded))
            {
                return Blocked("ExactProfileIntegrityFailure");
            }

            var applicationHash =
                Convert.ToHexString(
                    SHA256.HashData(
                        decoded.AsSpan(
                            2,
                            decoded.Length - 3)));

            if (!string.Equals(
                    applicationHash,
                    ApplicationSha256,
                    StringComparison.Ordinal))
            {
                return Blocked("ApplicationHashMismatch");
            }

            return new OfficialNpcDialogEncodingResult(
                OfficialNpcDialogEncodingStatus.Encoded,
                OfficialWorldBootstrapCodec.EncodeFrame(
                    decoded),
                "ExactEvidenceHashMatched");
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecodeExactOpenResponse(
        ReadOnlySpan<byte> encodedFrame,
        out ushort clientEntityHandle)
    {
        clientEntityHandle = 0;

        if (encodedFrame.Length != ExactOpenResponseLength)
        {
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(
                encodedFrame);

        try
        {
            var expected =
                Convert.FromHexString(ExactDecodedHex);

            try
            {
                if (!decoded.AsSpan().SequenceEqual(expected))
                {
                    return false;
                }

                clientEntityHandle =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        decoded.AsSpan(
                            7,
                            sizeof(ushort)));

                return clientEntityHandle ==
                    LiveDialogHandle;
            }
            finally
            {
                Array.Clear(expected);
            }
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private static OfficialNpcDialogEncodingResult Blocked(
        string reason) =>
        new(
            OfficialNpcDialogEncodingStatus.BlockedByEvidence,
            [],
            reason);
}
