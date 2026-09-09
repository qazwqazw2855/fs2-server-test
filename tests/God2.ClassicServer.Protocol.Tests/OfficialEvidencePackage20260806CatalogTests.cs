using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialEvidencePackage20260806CatalogTests
{
    [Fact]
    public void Payload_free_catalog_preserves_every_validated_structural_aggregate()
    {
        var catalog = new OfficialEvidencePackage20260806Catalog();
        var decoded = catalog.SnapshotDecodedFamilies();
        var handlers = catalog.SnapshotHandlerFamilies();

        Assert.Empty(catalog.Validate());
        Assert.Equal(64, decoded.Count);
        Assert.Equal(29, decoded.Count(value => value.Direction == PacketDirection.ClientToServer));
        Assert.Equal(35, decoded.Count(value => value.Direction == PacketDirection.ServerToClient));
        Assert.Equal(OfficialEvidencePackage20260806Catalog.DecodedMessageCount, decoded.Sum(value => value.ObservedCount));
        Assert.Equal(8, handlers.Count);
        Assert.Equal(OfficialEvidencePackage20260806Catalog.HandlerObservationCount, handlers.Sum(value => value.ObservedCount));
        Assert.All(decoded, value => Assert.InRange(value.UniquePayloadCount, 1, value.ObservedCount));
        Assert.All(handlers, value => Assert.InRange(value.UniquePayloadCount, 1, value.ObservedCount));
    }

    [Fact]
    public void Decoded_match_requires_direction_opcode_observed_length_and_valid_prefix()
    {
        var catalog = new OfficialEvidencePackage20260806Catalog();
        var frame = new byte[10];
        frame[0] = 10;
        frame[2] = 0x2E;

        var match = catalog.MatchDecodedFrame(PacketDirection.ClientToServer, frame);

        Assert.NotNull(match);
        Assert.Equal(367, match.ObservedCount);
        Assert.Null(catalog.MatchDecodedFrame(PacketDirection.ServerToClient, frame));

        frame[0] = 9;
        Assert.Null(catalog.MatchDecodedFrame(PacketDirection.ClientToServer, frame));

        frame[0] = 10;
        frame[2] = 0x30;
        Assert.Null(catalog.MatchDecodedFrame(PacketDirection.ClientToServer, frame));
    }

    [Fact]
    public void Official_registry_applies_only_six_fixed_length_decoded_families_as_evidence()
    {
        var expected = new (string Id, int Length, byte Opcode)[]
        {
            ("evidence-package-20260806-c2s-opcode-2e-10", 10, 0x2E),
            ("evidence-package-20260806-c2s-opcode-30-5", 5, 0x30),
            ("evidence-package-20260806-c2s-opcode-35-20", 20, 0x35),
            ("evidence-package-20260806-c2s-opcode-6d-5", 5, 0x6D),
            ("evidence-package-20260806-c2s-opcode-36-12", 12, 0x36),
            ("evidence-package-20260806-c2s-opcode-66-7", 7, 0x66)
        };

        foreach (var item in expected)
        {
            Assert.True(ProtocolRegistry.Official.TryGetPacket(item.Id, out var knowledge));
            Assert.NotNull(knowledge);
            Assert.Equal("DecodedOpcode", knowledge.Family);
            Assert.Equal(PacketDirection.ClientToServer, knowledge.Direction);
            Assert.Equal(item.Length, knowledge.Length);
            Assert.False(knowledge.Verified);
            Assert.True(knowledge.Recovered);
            Assert.Equal(ProtocolConfidence.Recovered, knowledge.Confidence);
            Assert.Equal(PacketRecoveryStatus.EvidenceOnly, knowledge.RecoveryStatus);
            Assert.Contains(knowledge.KnownFields, field =>
                field.Name == "decodedOpcodeByte" && field.Offset == 2 && field.Length == 1);
            Assert.Contains(knowledge.EvidenceSources, source =>
                source.Path == OfficialEvidencePackage20260806Catalog.EvidencePath);

            var frame = new byte[item.Length];
            frame[0] = (byte)item.Length;
            frame[1] = (byte)(item.Length >> 8);
            frame[2] = item.Opcode;
            var deserialized = new PacketDeserializer(ProtocolRegistry.Official).Read(frame);
            Assert.True(deserialized.Succeeded);
            Assert.Equal(item.Id, deserialized.Value!.Knowledge!.Id);
        }
    }
}
