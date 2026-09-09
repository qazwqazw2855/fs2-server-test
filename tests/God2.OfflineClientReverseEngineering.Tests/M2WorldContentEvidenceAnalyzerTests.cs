using System.Buffers.Binary;
using God2.GameplayContentRecovery;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M2WorldContentEvidenceAnalyzerTests
{
    [Fact]
    public void Incoming_length_table_is_recovered_from_the_pinned_switch_shape()
    {
        var runtime = BuildSyntheticRuntimeCode(fixedLength: 21);

        var lengths = M2WorldContentEvidenceAnalyzer.RecoverIncomingPacketLengths(runtime);

        Assert.Equal(255, lengths.Count);
        Assert.Equal(21, lengths[0x60]);
        Assert.Equal(21, lengths[0x72]);
    }

    [Fact]
    public void Incoming_length_table_rejects_a_different_client_function()
    {
        var runtime = BuildSyntheticRuntimeCode(fixedLength: 21);
        runtime[M2WorldContentEvidenceAnalyzer.IncomingLengthFunctionRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva] ^= 0x01;

        Assert.Throws<InvalidDataException>(() =>
            M2WorldContentEvidenceAnalyzer.RecoverIncomingPacketLengths(runtime));
    }

    [Fact]
    public void Transport_splitter_supports_client_declared_variable_records_without_retaining_an_unknown_body()
    {
        byte[] transport =
        [
            0x0A, 0x00,
            0x2B, 0x11, 0x22,
            0x5C, 0x04, 0x33, 0x44,
            0xAF
        ];

        var messages = M2WorldContentEvidenceAnalyzer.SplitFixedLengthTransportFrame(
            transport,
            opcode => opcode == 0x2B ? 3 : opcode == 0x5C ? -2 : -1);

        Assert.Collection(
            messages,
            first =>
            {
                Assert.Equal(2, first.TransportOffset);
                Assert.Equal(3, first.Bytes.Length);
                Assert.Equal(0x2B, first.Bytes[0]);
            },
            second =>
            {
                Assert.Equal(5, second.TransportOffset);
                Assert.Equal(4, second.Bytes.Length);
                Assert.Equal(0x5C, second.Bytes[0]);
            });
    }

    [Fact]
    public void Transport_splitter_fails_closed_on_truncation_or_a_bad_length_prefix()
    {
        Assert.Throws<InvalidDataException>(() =>
            M2WorldContentEvidenceAnalyzer.SplitFixedLengthTransportFrame(
                [0x09, 0x00, 0x2B, 0x11, 0x22, 0xAF],
                _ => 3));

        Assert.Throws<InvalidDataException>(() =>
            M2WorldContentEvidenceAnalyzer.SplitFixedLengthTransportFrame(
                [0x06, 0x00, 0x2B, 0x11, 0xAF, 0xAF],
                _ => 4));
    }

    [Theory]
    [InlineData(0x00100046u, 17, 8)]
    [InlineData(0x001C004Au, 18, 14)]
    [InlineData(0x001C003Au, 14, 14)]
    public void Packed_position_uses_the_official_client_consumer_formula(uint packed, int expectedX, int expectedY)
    {
        var actual = M2WorldContentEvidenceAnalyzer.DecodePackedPosition(packed);

        Assert.Equal((expectedX, expectedY), actual);
    }

    [Fact]
    public void Entity_record_resolves_npc_resource_by_exact_type_and_ordinal()
    {
        var document = new CsvZDocument(
            "NPC.csvZ",
            new string('A', 64),
            1,
            1,
            0,
            "CP936-strict",
            0,
            [
                new CsvRow(1, "header", ["索引"]),
                NpcRow(2, "data2\\rom\\npc\\npc2000.ROMZ", 2, "第一個", "老闆NPC"),
                NpcRow(3, "data2\\rom\\npc\\npc2643.ROMZ", 2, "雜貨老闆", "老闆NPC")
            ],
            string.Empty);
        var resources = M2WorldContentEvidenceAnalyzer.BuildNpcResourceIndex(document);
        var record = new byte[21];
        record[0] = 0x72;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(1, 4), 1504);
        record[5] = 2; // Client stores ordinal + 1 for this branch.
        record[6] = 0x62; // low five bits select resource type 2.
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(17, 4), 0x00100046);

        var observation = Assert.Single(M2WorldContentEvidenceAnalyzer.DecodeWorldEntityObservations(
            [new M2TransportMessage(0, 2, record)],
            resources));

        Assert.Equal((uint)1504, observation.ObservedEntityHandle);
        Assert.Equal(2, observation.ResourceType);
        Assert.Equal(1, observation.ResourceOrdinal);
        Assert.Equal("data2/rom/npc/npc2643.rom", observation.NpcResource!.ResourceKey);
        Assert.Equal("雜貨老闆", observation.NpcResource.OriginalName);
        Assert.Equal(17, observation.X);
        Assert.Equal(8, observation.Y);
    }

    [Fact]
    public void Entity_record_fails_closed_when_the_type_ordinal_has_no_resource()
    {
        var record = new byte[21];
        record[0] = 0x72;
        record[5] = 1;
        record[6] = 2;

        Assert.Throws<InvalidDataException>(() =>
            M2WorldContentEvidenceAnalyzer.DecodeWorldEntityObservations(
                [new M2TransportMessage(0, 2, record)],
                []));
    }

    private static CsvRow NpcRow(int sourceRow, string resource, int type, string name, string label) =>
        new(sourceRow, string.Empty, ["", resource, "", "", "0", "0", type.ToString(), "", name, label]);

    private static byte[] BuildSyntheticRuntimeCode(int fixedLength)
    {
        const int returnRva = 0x166900;
        var requiredEndRva = returnRva + 7;
        var runtime = new byte[requiredEndRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva];
        var functionOffset = M2WorldContentEvidenceAnalyzer.IncomingLengthFunctionRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        Convert.FromHexString("558BEC0FB645083DFE0000000F873B010000FF2485B4645600").CopyTo(runtime, functionOffset);
        var tableOffset = M2WorldContentEvidenceAnalyzer.IncomingLengthJumpTableRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        for (var opcode = 0; opcode <= 0xFE; opcode++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                runtime.AsSpan(tableOffset + (opcode * 4), 4),
                M2WorldContentEvidenceAnalyzer.ImageBase + returnRva);
        }

        var returnOffset = returnRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        runtime[returnOffset] = 0xB8;
        BinaryPrimitives.WriteInt32LittleEndian(runtime.AsSpan(returnOffset + 1, 4), fixedLength);
        runtime[returnOffset + 5] = 0x5D;
        runtime[returnOffset + 6] = 0xC3;
        return runtime;
    }
}
