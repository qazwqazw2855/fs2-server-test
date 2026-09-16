using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.ServerV2.Protocol;

public sealed record OfficialNpcSpawnEvidence(
    string? ClientBuildId,
    uint? EntityHandle,
    byte? ResourceType,
    byte? ResourceOrdinal,
    byte? SelectorHighBits,
    byte? DirectionCode,
    byte? StateCode,
    int? PositionX,
    int? PositionY,
    string? SpawnMessageSha256,
    string? OpaqueTemplateSha256,
    string? EvidenceStatus,
    string? EvidenceReference);

public enum OfficialNpcSpawnEncodingStatus
{
    Encoded,
    BlockedByEvidence
}

public sealed record OfficialNpcSpawnEncodingResult(
    OfficialNpcSpawnEncodingStatus Status,
    byte[] Frame,
    string Reason)
{
    public bool Succeeded =>
        Status == OfficialNpcSpawnEncodingStatus.Encoded;
}

public static class OfficialNpcSpawnCodec
{
    public const string ClientBuildId =
        "god2-opt-6b127086e0c0";

    public const byte SpawnOpcode = 0x72;
    public const int FrameLength = 24;
    public const int ApplicationRecordLength = 21;

    public const string TypeZeroOpaqueTemplateSha256 =
        "C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838";

    private static readonly byte[] TypeZeroOpaqueTemplate =
        Convert.FromHexString("000000CF010000");

    public static OfficialNpcSpawnEncodingResult Encode(
        OfficialNpcSpawnEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var failure = Validate(evidence);

        if (failure is not null)
        {
            return Blocked(failure);
        }

        var decoded = new byte[FrameLength];

        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded,
                FrameLength);

            decoded[2] = SpawnOpcode;

            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(3, sizeof(uint)),
                evidence.EntityHandle!.Value);

            decoded[7] = checked(
                (byte)(evidence.ResourceOrdinal!.Value + 1));

            decoded[8] = checked((byte)(
                (evidence.SelectorHighBits!.Value << 5) |
                evidence.ResourceType!.Value));

            BinaryPrimitives.WriteUInt16LittleEndian(
                decoded.AsSpan(9, sizeof(ushort)),
                0);

            decoded[11] = checked((byte)(
                (evidence.DirectionCode!.Value << 5) |
                evidence.StateCode!.Value));

            TypeZeroOpaqueTemplate.CopyTo(decoded, 12);

            var packedPosition = checked(
                ((uint)evidence.PositionY!.Value << 17) |
                ((uint)evidence.PositionX!.Value << 2));

            BinaryPrimitives.WriteUInt32LittleEndian(
                decoded.AsSpan(19, sizeof(uint)),
                packedPosition);

            var actualApplicationHash =
                Convert.ToHexString(
                    SHA256.HashData(
                        decoded.AsSpan(
                            2,
                            ApplicationRecordLength)));

            if (!string.Equals(
                    actualApplicationHash,
                    evidence.SpawnMessageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Blocked(
                    "ApplicationRecordHashMismatch");
            }

            decoded[^1] =
                OfficialLoginWireTransform.ComputeChecksum(decoded);

            var encoded =
                OfficialWorldBootstrapCodec.EncodeFrame(decoded);

            return new OfficialNpcSpawnEncodingResult(
                OfficialNpcSpawnEncodingStatus.Encoded,
                encoded,
                "EvidenceHashMatched");
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool TryDecodeHandle(
        ReadOnlySpan<byte> frame,
        out uint entityHandle)
    {
        entityHandle = 0;

        if (frame.Length != FrameLength)
        {
            return false;
        }

        var decoded =
            OfficialWorldBootstrapCodec.DecodeFrame(frame);

        try
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(decoded) !=
                    FrameLength ||
                decoded[2] != SpawnOpcode ||
                decoded[^1] !=
                    OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return false;
            }

            entityHandle =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    decoded.AsSpan(3, sizeof(uint)));

            return entityHandle is > 0 and <= ushort.MaxValue;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private static string? Validate(
        OfficialNpcSpawnEvidence evidence)
    {
        if (!string.Equals(
                evidence.ClientBuildId,
                ClientBuildId,
                StringComparison.Ordinal))
        {
            return "ClientBuildMismatch";
        }

        if (evidence.EvidenceStatus is not
            ("Derived" or "Verified"))
        {
            return "EvidenceStatusBlocked";
        }

        if (string.IsNullOrWhiteSpace(
                evidence.EvidenceReference))
        {
            return "EvidenceReferenceMissing";
        }

        if (evidence.EntityHandle is not
            (> 0 and <= ushort.MaxValue))
        {
            return "EntityHandleInvalid";
        }

        if (evidence.ResourceType != 0 ||
            evidence.ResourceOrdinal is null or byte.MaxValue ||
            evidence.SelectorHighBits != 3 ||
            evidence.DirectionCode != 4 ||
            evidence.StateCode != 1)
        {
            return "DerivedTypeZeroProfileMismatch";
        }

        if (evidence.PositionX is not
                (>= 0 and <= 0x7FFF) ||
            evidence.PositionY is not
                (>= 0 and <= 0x7FFF))
        {
            return "PositionInvalid";
        }

        if (!IsSha256(evidence.SpawnMessageSha256))
        {
            return "SpawnMessageHashMissing";
        }

        if (!string.Equals(
                evidence.OpaqueTemplateSha256,
                TypeZeroOpaqueTemplateSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "OpaqueTemplateHashMismatch";
        }

        Span<byte> ignoredRegions = stackalloc byte[9];
        TypeZeroOpaqueTemplate.CopyTo(ignoredRegions[2..]);

        var actualOpaqueHash =
            Convert.ToHexString(
                SHA256.HashData(ignoredRegions));

        if (!string.Equals(
                actualOpaqueHash,
                evidence.OpaqueTemplateSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "OpaqueTemplateIntegrityFailure";
        }

        return null;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');

    private static OfficialNpcSpawnEncodingResult Blocked(
        string reason) =>
        new(
            OfficialNpcSpawnEncodingStatus.BlockedByEvidence,
            [],
            reason);
}
