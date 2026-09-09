using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialEvidencePackage20260807SupplementCatalogTests
{
    [Fact]
    public void Supplemental_catalog_is_payload_free_complete_and_evidence_only()
    {
        var catalog = new OfficialEvidencePackage20260807SupplementCatalog();
        var families = catalog.SnapshotFamilies();

        Assert.Empty(catalog.Validate());
        Assert.Equal(62, families.Count);
        Assert.Equal(5, families.Count(value => value.Direction == PacketDirection.ClientToServer));
        Assert.Equal(57, families.Count(value => value.Direction == PacketDirection.ServerToClient));
        Assert.Equal(87, families.Sum(value => value.ObservedCount));
        Assert.Equal(85, families.Sum(value => value.UniqueFrameCount));
        Assert.All(families, value => Assert.InRange(value.UniqueFrameCount, 1, value.ObservedCount));
    }

    [Fact]
    public void Supplemental_match_requires_direction_opcode_length_and_valid_prefix()
    {
        var catalog = new OfficialEvidencePackage20260807SupplementCatalog();
        var frame = new byte[21];
        frame[0] = 21;
        frame[2] = 0xE5;

        var match = catalog.Match(PacketDirection.ServerToClient, frame);

        Assert.NotNull(match);
        Assert.Equal(11, match.ObservedCount);
        Assert.Null(catalog.Match(PacketDirection.ClientToServer, frame));

        frame[0] = 20;
        Assert.Null(catalog.Match(PacketDirection.ServerToClient, frame));

        frame[0] = 21;
        frame[2] = 0xE6;
        Assert.Null(catalog.Match(PacketDirection.ServerToClient, frame));
    }
}
