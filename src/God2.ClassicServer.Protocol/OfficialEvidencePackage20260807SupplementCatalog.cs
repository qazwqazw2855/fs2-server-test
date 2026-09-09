using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace God2.ClassicServer.Protocol;

public sealed record SupplementalDecodedFrameFamily(
    PacketDirection Direction,
    byte Opcode,
    int FrameLength,
    int ObservedCount,
    int UniqueFrameCount);

/// <summary>
/// Payload-free structural additions observed in the validated v1.0.2 and
/// v1.0.3 Evidence Packages. These entries only prove direction, decoded
/// opcode and exact frame length. They grant no gameplay meaning, serializer,
/// handler registration or production mutation authority.
/// </summary>
public sealed class OfficialEvidencePackage20260807SupplementCatalog
{
    public const string EvidenceSourceId = "god2-evidence-package-20260807-supplement";
    public const string NewSessionId = "870DB7D9-B96A-4F38-B225-8C7EA3D9A7E6";
    public const string OldSessionId = "2C820B07-0A81-459A-B04A-7C5936A015F5";
    public const string NewPackageSha256 = "48C85FA534DB6C221A8AD06B24DFE4326E83DF70BB27640C8C534C6F431C3B2E";
    public const string OldPackageSha256 = "0778A6AA3C7F0EEB6F48A99BEE0015F0F6AB43C399F7BB3203A272425D506A57";
    public const string EvidencePath =
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json";

    public const int SourceCaptureRecordCount = 4729;
    public const int SourceDecodedMessageCount = 1344;
    public const int SourceHandlerObservationCount = 552;
    public const int SupplementalFamilyCount = 62;
    public const int SupplementalObservationCount = 87;
    public const int SupplementalUniqueFrameCount = 85;

    private static readonly ReadOnlyCollection<SupplementalDecodedFrameFamily> Families =
        Array.AsReadOnly(
        [
            C(0x02, 5, 2, 1),
            C(0x2F, 14, 1, 1),
            C(0x67, 5, 1, 1),
            C(0xB0, 6, 1, 1),
            C(0xB4, 5, 2, 2),
            S(0x03, 6, 2, 1),
            S(0x23, 115, 1, 1),
            S(0x23, 969, 1, 1),
            S(0x23, 1058, 1, 1),
            S(0x23, 1137, 1, 1),
            S(0x23, 1173, 1, 1),
            S(0x24, 20, 7, 7),
            S(0x24, 41, 1, 1),
            S(0x36, 35, 1, 1),
            S(0x3A, 31, 1, 1),
            S(0x41, 50, 2, 2),
            S(0x4C, 20, 1, 1),
            S(0x4C, 41, 1, 1),
            S(0x5C, 26, 1, 1),
            S(0x5C, 36, 1, 1),
            S(0x5C, 37, 1, 1),
            S(0x5C, 42, 2, 2),
            S(0x5C, 44, 1, 1),
            S(0x5C, 46, 1, 1),
            S(0x5C, 48, 1, 1),
            S(0x5C, 69, 1, 1),
            S(0x5C, 78, 1, 1),
            S(0x5D, 7, 1, 1),
            S(0x5D, 104, 1, 1),
            S(0x5D, 618, 1, 1),
            S(0x5F, 35, 1, 1),
            S(0x5F, 42, 1, 1),
            S(0x73, 8, 2, 2),
            S(0x85, 241, 1, 1),
            S(0x85, 243, 1, 1),
            S(0x85, 252, 1, 1),
            S(0x85, 260, 1, 1),
            S(0x85, 263, 1, 1),
            S(0x85, 299, 2, 2),
            S(0x85, 314, 2, 2),
            S(0x85, 322, 1, 1),
            S(0x86, 39, 1, 1),
            S(0x86, 71, 1, 1),
            S(0x86, 95, 1, 1),
            S(0x86, 260, 1, 1),
            S(0x86, 264, 1, 1),
            S(0x86, 277, 1, 1),
            S(0x86, 279, 1, 1),
            S(0x86, 311, 1, 1),
            S(0x86, 316, 1, 1),
            S(0x86, 348, 1, 1),
            S(0x86, 373, 1, 1),
            S(0x86, 440, 1, 1),
            S(0xAE, 28, 1, 1),
            S(0xAE, 61, 1, 1),
            S(0xB2, 8, 1, 1),
            S(0xC8, 161, 1, 1),
            S(0xDA, 215, 1, 1),
            S(0xE4, 154, 1, 1),
            S(0xE5, 21, 11, 11),
            S(0xE6, 62, 1, 1),
            S(0xE8, 5, 2, 2)
        ]);

    public IReadOnlyList<SupplementalDecodedFrameFamily> SnapshotFamilies() => Families;

    public SupplementalDecodedFrameFamily? Match(PacketDirection direction, ReadOnlySpan<byte> frame)
    {
        if (direction is not (PacketDirection.ClientToServer or PacketDirection.ServerToClient) ||
            frame.Length < 3 || BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) != frame.Length)
        {
            return null;
        }

        var opcode = frame[2];
        var frameLength = frame.Length;
        return Families.FirstOrDefault(value =>
            value.Direction == direction && value.Opcode == opcode && value.FrameLength == frameLength);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Families.Count != SupplementalFamilyCount ||
            Families.Sum(value => value.ObservedCount) != SupplementalObservationCount ||
            Families.Sum(value => value.UniqueFrameCount) != SupplementalUniqueFrameCount)
        {
            errors.Add("evidence_package_20260807.supplemental_count_mismatch");
        }

        if (Families.Any(value => value.FrameLength < 3 || value.ObservedCount <= 0 ||
                value.UniqueFrameCount <= 0 || value.UniqueFrameCount > value.ObservedCount) ||
            Families.GroupBy(value => (value.Direction, value.Opcode, value.FrameLength))
                .Any(group => group.Count() != 1))
        {
            errors.Add("evidence_package_20260807.invalid_supplemental_family");
        }

        return errors.AsReadOnly();
    }

    private static SupplementalDecodedFrameFamily C(byte opcode, int length, int count, int unique) =>
        new(PacketDirection.ClientToServer, opcode, length, count, unique);

    private static SupplementalDecodedFrameFamily S(byte opcode, int length, int count, int unique) =>
        new(PacketDirection.ServerToClient, opcode, length, count, unique);
}
