using System.Buffers.Binary;
using System.Net;

namespace God2.LoginEvidenceRecovery;

internal sealed record CapturedTcpSegment(
    string SourcePath,
    int PacketIndex,
    DateTimeOffset TimestampUtc,
    string SourceIp,
    int SourcePort,
    string DestinationIp,
    int DestinationPort,
    uint Sequence,
    uint Acknowledgement,
    byte Flags,
    byte[] Payload)
{
    public string ConnectionTuple =>
        SourcePort == 2592
            ? $"{DestinationIp}:{DestinationPort}->{SourceIp}:{SourcePort}"
            : $"{SourceIp}:{SourcePort}->{DestinationIp}:{DestinationPort}";

    public string Direction => DestinationPort == 2592 ? "ClientToServer" : "ServerToClient";
}

internal sealed record PcapngReadResult(
    IReadOnlyList<CapturedTcpSegment> Segments,
    IReadOnlyDictionary<uint, ushort> LinkTypes,
    int PacketBlockCount,
    int UnsupportedPacketCount,
    IReadOnlyList<string> Issues);

internal static class PcapngReader
{
    private const uint SectionHeaderBlock = 0x0A0D0D0A;
    private const uint InterfaceDescriptionBlock = 0x00000001;
    private const uint SimplePacketBlock = 0x00000003;
    private const uint EnhancedPacketBlock = 0x00000006;

    public static PcapngReadResult Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var segments = new List<CapturedTcpSegment>();
        var interfaces = new Dictionary<uint, InterfaceInfo>();
        var issues = new List<string>();
        var littleEndian = true;
        var offset = 0;
        var packetIndex = 0;
        var unsupported = 0;

        while (offset + 12 <= bytes.Length)
        {
            var blockType = ReadUInt32(bytes.AsSpan(offset, 4), littleEndian);
            if (blockType == SectionHeaderBlock)
            {
                var magicLittle = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
                littleEndian = magicLittle switch
                {
                    0x1A2B3C4D => true,
                    0x4D3C2B1A => false,
                    _ => littleEndian
                };
                blockType = SectionHeaderBlock;
                interfaces.Clear();
            }

            var blockLength = checked((int)ReadUInt32(bytes.AsSpan(offset + 4, 4), littleEndian));
            if (blockLength < 12 || offset + blockLength > bytes.Length)
            {
                issues.Add($"Invalid block length {blockLength} at file offset {offset}.");
                break;
            }

            var trailingLength = ReadUInt32(bytes.AsSpan(offset + blockLength - 4, 4), littleEndian);
            if (trailingLength != blockLength)
            {
                issues.Add($"Block length trailer mismatch at file offset {offset}.");
            }

            if (blockType == InterfaceDescriptionBlock && blockLength >= 20)
            {
                var interfaceId = checked((uint)interfaces.Count);
                var linkType = ReadUInt16(bytes.AsSpan(offset + 8, 2), littleEndian);
                var timestampResolution = ReadTimestampResolution(bytes.AsSpan(offset + 16, blockLength - 20), littleEndian);
                interfaces[interfaceId] = new InterfaceInfo(linkType, timestampResolution);
            }
            else if (blockType == EnhancedPacketBlock && blockLength >= 32)
            {
                packetIndex++;
                var interfaceId = ReadUInt32(bytes.AsSpan(offset + 8, 4), littleEndian);
                var timestampHigh = ReadUInt32(bytes.AsSpan(offset + 12, 4), littleEndian);
                var timestampLow = ReadUInt32(bytes.AsSpan(offset + 16, 4), littleEndian);
                var capturedLength = checked((int)ReadUInt32(bytes.AsSpan(offset + 20, 4), littleEndian));
                var packetStart = offset + 28;
                var packetEnd = packetStart + capturedLength;

                if (packetEnd > offset + blockLength - 4)
                {
                    issues.Add($"Enhanced packet {packetIndex} exceeds its block.");
                }
                else if (!interfaces.TryGetValue(interfaceId, out var interfaceInfo))
                {
                    issues.Add($"Enhanced packet {packetIndex} references unknown interface {interfaceId}.");
                }
                else
                {
                    var timestamp = ToTimestamp(timestampHigh, timestampLow, interfaceInfo.TimestampResolution);
                    if (TryParseTcp(
                        path,
                        packetIndex,
                        timestamp,
                        interfaceInfo.LinkType,
                        bytes.AsSpan(packetStart, capturedLength),
                        out var segment))
                    {
                        segments.Add(segment);
                    }
                    else
                    {
                        unsupported++;
                    }
                }
            }
            else if (blockType == SimplePacketBlock)
            {
                packetIndex++;
                unsupported++;
            }

            offset += blockLength;
        }

        return new PcapngReadResult(
            segments,
            interfaces.ToDictionary(item => item.Key, item => item.Value.LinkType),
            packetIndex,
            unsupported,
            issues);
    }

    private static bool TryParseTcp(
        string sourcePath,
        int packetIndex,
        DateTimeOffset timestamp,
        ushort linkType,
        ReadOnlySpan<byte> packet,
        out CapturedTcpSegment segment)
    {
        segment = null!;
        var ipOffset = linkType switch
        {
            1 => EthernetIpOffset(packet),
            0 => NullLoopbackIpOffset(packet),
            101 => 0,
            113 => packet.Length >= 16 ? 16 : -1,
            276 => packet.Length >= 20 ? 20 : -1,
            _ => -1
        };

        if (ipOffset < 0 || packet.Length < ipOffset + 20)
        {
            return false;
        }

        var version = packet[ipOffset] >> 4;
        if (version != 4)
        {
            return false;
        }

        var ipHeaderLength = (packet[ipOffset] & 0x0F) * 4;
        if (ipHeaderLength < 20 || packet.Length < ipOffset + ipHeaderLength)
        {
            return false;
        }

        if (packet[ipOffset + 9] != 6)
        {
            return false;
        }

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(ipOffset + 2, 2));
        var ipEnd = Math.Min(packet.Length, ipOffset + totalLength);
        var tcpOffset = ipOffset + ipHeaderLength;
        if (ipEnd < tcpOffset + 20)
        {
            return false;
        }

        var tcpHeaderLength = (packet[tcpOffset + 12] >> 4) * 4;
        if (tcpHeaderLength < 20 || ipEnd < tcpOffset + tcpHeaderLength)
        {
            return false;
        }

        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(tcpOffset, 2));
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(tcpOffset + 2, 2));
        if (sourcePort != 2592 && destinationPort != 2592)
        {
            return false;
        }

        var payloadOffset = tcpOffset + tcpHeaderLength;
        var payload = packet.Slice(payloadOffset, Math.Max(0, ipEnd - payloadOffset)).ToArray();
        var sourceIp = new IPAddress(packet.Slice(ipOffset + 12, 4)).ToString();
        var destinationIp = new IPAddress(packet.Slice(ipOffset + 16, 4)).ToString();

        segment = new CapturedTcpSegment(
            sourcePath,
            packetIndex,
            timestamp,
            sourceIp,
            sourcePort,
            destinationIp,
            destinationPort,
            BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(tcpOffset + 4, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(tcpOffset + 8, 4)),
            packet[tcpOffset + 13],
            payload);
        return true;
    }

    private static int EthernetIpOffset(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 14)
        {
            return -1;
        }

        var etherType = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(12, 2));
        var offset = 14;
        while (etherType is 0x8100 or 0x88A8 or 0x9100)
        {
            if (packet.Length < offset + 4)
            {
                return -1;
            }

            etherType = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2));
            offset += 4;
        }

        return etherType == 0x0800 ? offset : -1;
    }

    private static int NullLoopbackIpOffset(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4)
        {
            return -1;
        }

        var familyLittle = BinaryPrimitives.ReadUInt32LittleEndian(packet[..4]);
        var familyBig = BinaryPrimitives.ReadUInt32BigEndian(packet[..4]);
        return familyLittle == 2 || familyBig == 2 ? 4 : -1;
    }

    private static double ReadTimestampResolution(ReadOnlySpan<byte> options, bool littleEndian)
    {
        const double defaultResolution = 1e-6;
        var offset = 0;
        while (offset + 4 <= options.Length)
        {
            var code = ReadUInt16(options.Slice(offset, 2), littleEndian);
            var length = ReadUInt16(options.Slice(offset + 2, 2), littleEndian);
            offset += 4;
            if (code == 0)
            {
                break;
            }

            if (offset + length > options.Length)
            {
                break;
            }

            if (code == 9 && length >= 1)
            {
                var value = options[offset];
                return (value & 0x80) == 0
                    ? Math.Pow(10, -value)
                    : Math.Pow(2, -(value & 0x7F));
            }

            offset += (length + 3) & ~3;
        }

        return defaultResolution;
    }

    private static DateTimeOffset ToTimestamp(uint high, uint low, double resolution)
    {
        var raw = ((ulong)high << 32) | low;
        var ticks = checked((long)Math.Round(raw * resolution * TimeSpan.TicksPerSecond));
        return DateTimeOffset.UnixEpoch.AddTicks(ticks);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private sealed record InterfaceInfo(ushort LinkType, double TimestampResolution);
}
