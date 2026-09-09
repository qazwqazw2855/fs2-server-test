using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public sealed record EvidencePackageDecodedOpcodeFamily(
    PacketDirection Direction,
    byte Opcode,
    int ObservedCount,
    int UniquePayloadCount,
    IReadOnlyList<int> FrameLengths);

public sealed record EvidencePackageHandlerFamily(
    byte Opcode,
    int ObservedCount,
    int UniquePayloadCount,
    int PayloadLength,
    uint HandlerAddress);

/// <summary>
/// Payload-free structural catalog derived from the validated v1.0.1 Evidence Package.
/// It can recognize decoded frame families, but never grants gameplay semantics,
/// handler registration, serializer authority, or production mutation permission.
/// </summary>
public sealed class OfficialEvidencePackage20260806Catalog
{
    public const string EvidenceSourceId = "god2-evidence-package-20260806-102928";
    public const string SessionId = "65BBF007-6117-419F-BF8E-F3C7C95B8809";
    public const string AnalysisRunId = "7D843178-F12E-4993-8EF2-991AD9FD324D";
    public const string PackageSha256 = "6C8D52617F652F522610ABDB3D4DB1B381E0A21A08B8E60E4A574AC66F7E9951";
    public const string EvidencePath =
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260806.102928.json";

    public const int CaptureRecordCount = 5797;
    public const int TransportChunkCount = 3482;
    public const int DecodedMessageCount = 1685;
    public const int ClientDecodedMessageCount = 786;
    public const int ServerDecodedMessageCount = 899;
    public const int HandlerObservationCount = 630;
    public const int LengthPrefixValidatedCount = 1685;

    private static readonly ReadOnlyCollection<EvidencePackageDecodedOpcodeFamily> DecodedFamilies =
        Array.AsReadOnly(
        [
            C(0x2E, 367, 367, 10),
            C(0x30, 136, 1, 5),
            C(0x35, 112, 46, 20),
            C(0x6D, 33, 1, 5),
            C(0x36, 33, 3, 12),
            C(0x66, 17, 14, 7),
            C(0x28, 13, 13, 8),
            C(0x1C, 12, 1, 8),
            C(0x85, 9, 6, 10),
            C(0x1F, 8, 1, 5),
            C(0x1D, 8, 5, 6),
            C(0x21, 6, 1, 5),
            C(0x37, 5, 3, 8),
            C(0xC2, 4, 4, 12),
            C(0x20, 4, 1, 20),
            C(0x86, 3, 1, 8),
            C(0x25, 2, 2, 8),
            C(0x39, 2, 1, 8),
            C(0x38, 2, 2, 12),
            C(0xB5, 1, 1, 8),
            C(0x68, 1, 1, 5),
            C(0xAB, 1, 1, 6),
            C(0x00, 1, 1, 208),
            C(0x1A, 1, 1, 41),
            C(0x04, 1, 1, 208),
            C(0xA6, 1, 1, 6),
            C(0x9A, 1, 1, 71),
            C(0x23, 1, 1, 5),
            C(0x0A, 1, 1, 20),

            S(0x5D, 388, 115, 5, 8, 10, 14, 15, 16, 20, 26, 31, 36, 47, 52, 57, 73, 78, 215, 460, 539),
            S(0x5F, 167, 166, 24, 26, 29, 31, 45, 47, 92),
            S(0x86, 131, 41, 20, 37, 54, 69, 86, 103, 105, 107, 110, 112, 114, 136, 201, 243, 258, 275, 290, 292, 295, 520, 528),
            S(0x36, 26, 2, 14, 25),
            S(0x72, 25, 25, 45, 69, 87, 129, 140, 171, 439),
            S(0x77, 17, 14, 10, 31),
            S(0x71, 16, 16, 29, 50, 52, 71, 94),
            S(0x85, 13, 13, 258, 262, 267, 275, 278, 279, 284, 290, 297, 308),
            S(0x41, 13, 13, 25, 45, 145, 155, 161),
            S(0x7A, 11, 8, 30, 31, 58),
            S(0x88, 10, 10, 192, 229),
            S(0xBB, 10, 4, 6, 29, 56),
            S(0x74, 10, 6, 8, 13, 76, 83),
            S(0xE5, 10, 10, 14, 35, 38),
            S(0x22, 9, 9, 28),
            S(0xB2, 7, 7, 120, 146, 148, 167, 220),
            S(0x23, 6, 2, 20, 1254),
            S(0x82, 4, 4, 458, 537, 616),
            S(0xE6, 3, 3, 53, 95),
            S(0x61, 3, 3, 12),
            S(0xF5, 3, 3, 32, 53),
            S(0x02, 2, 2, 26, 320),
            S(0x68, 2, 1, 16),
            S(0x01, 2, 2, 6),
            S(0x3A, 1, 1, 10),
            S(0x5C, 1, 1, 57),
            S(0x42, 1, 1, 6),
            S(0x3F, 1, 1, 6),
            S(0xEE, 1, 1, 100),
            S(0x1F, 1, 1, 128),
            S(0x07, 1, 1, 78),
            S(0x3B, 1, 1, 40),
            S(0x4B, 1, 1, 23),
            S(0x2E, 1, 1, 154),
            S(0x89, 1, 1, 184)
        ]);

    private static readonly ReadOnlyCollection<EvidencePackageHandlerFamily> HandlerFamilies =
        Array.AsReadOnly(
        [
            H(0x86, 337, 103, 17, 0x0054709B),
            H(0x83, 125, 85, 15, 0x0054709B),
            H(0x85, 92, 1, 2, 0x0054709B),
            H(0x88, 33, 33, 121, 0x0054709B),
            H(0x1C, 23, 14, 45, 0x0054709B),
            H(0x89, 8, 7, 57, 0x005470B3),
            H(0x38, 6, 1, 2, 0x0054709B),
            H(0x87, 6, 2, 3, 0x0054709B)
        ]);

    public IReadOnlyList<EvidencePackageDecodedOpcodeFamily> SnapshotDecodedFamilies() => DecodedFamilies;

    public IReadOnlyList<EvidencePackageHandlerFamily> SnapshotHandlerFamilies() => HandlerFamilies;

    public EvidencePackageDecodedOpcodeFamily? MatchDecodedFrame(PacketDirection direction, ReadOnlySpan<byte> frame)
    {
        if (direction is not (PacketDirection.ClientToServer or PacketDirection.ServerToClient) ||
            frame.Length < 3 || BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) != frame.Length)
        {
            return null;
        }

        var opcode = frame[2];
        var frameLength = frame.Length;
        return DecodedFamilies.FirstOrDefault(family =>
            family.Direction == direction && family.Opcode == opcode && family.FrameLengths.Contains(frameLength));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DecodedFamilies.Count != 64 || HandlerFamilies.Count != 8)
        {
            errors.Add("evidence_package.catalog.family_count_mismatch");
        }

        if (DecodedFamilies.Where(value => value.Direction == PacketDirection.ClientToServer).Sum(value => value.ObservedCount) !=
                ClientDecodedMessageCount ||
            DecodedFamilies.Where(value => value.Direction == PacketDirection.ServerToClient).Sum(value => value.ObservedCount) !=
                ServerDecodedMessageCount ||
            DecodedFamilies.Sum(value => value.ObservedCount) != DecodedMessageCount)
        {
            errors.Add("evidence_package.catalog.decoded_count_mismatch");
        }

        if (HandlerFamilies.Sum(value => value.ObservedCount) != HandlerObservationCount)
        {
            errors.Add("evidence_package.catalog.handler_count_mismatch");
        }

        if (DecodedFamilies.Any(value => value.ObservedCount <= 0 || value.UniquePayloadCount <= 0 ||
                value.UniquePayloadCount > value.ObservedCount || value.FrameLengths.Count == 0 ||
                value.FrameLengths.Any(length => length < 3 || length > ushort.MaxValue)) ||
            HandlerFamilies.Any(value => value.ObservedCount <= 0 || value.UniquePayloadCount <= 0 ||
                value.UniquePayloadCount > value.ObservedCount || value.PayloadLength <= 0))
        {
            errors.Add("evidence_package.catalog.invalid_aggregate");
        }

        if (DecodedFamilies.GroupBy(value => (value.Direction, value.Opcode)).Any(group => group.Count() != 1) ||
            HandlerFamilies.Select(value => value.Opcode).Distinct().Count() != HandlerFamilies.Count)
        {
            errors.Add("evidence_package.catalog.duplicate_family");
        }

        return errors.AsReadOnly();
    }

    private static EvidencePackageDecodedOpcodeFamily C(byte opcode, int count, int unique, params int[] lengths) =>
        new(PacketDirection.ClientToServer, opcode, count, unique, Array.AsReadOnly(lengths));

    private static EvidencePackageDecodedOpcodeFamily S(byte opcode, int count, int unique, params int[] lengths) =>
        new(PacketDirection.ServerToClient, opcode, count, unique, Array.AsReadOnly(lengths));

    private static EvidencePackageHandlerFamily H(byte opcode, int count, int unique, int length, uint address) =>
        new(opcode, count, unique, length, address);
}
