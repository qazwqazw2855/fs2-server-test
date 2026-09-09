using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialDecryptedPacketCorpusCatalogTests
{
    [Fact]
    public void Embedded_catalog_applies_all_112_definitions_and_only_96_plaintext_samples()
    {
        var catalog = new OfficialDecryptedPacketCorpusCatalog();

        Assert.Empty(catalog.Validate());
        Assert.Equal(112, catalog.Definitions.Count);
        Assert.Equal(96, catalog.Definitions.Count(value => value.PlaintextAvailable));
        Assert.Equal(16, catalog.Definitions.Count(value => !value.PlaintextAvailable));
        Assert.Equal(64, catalog.SourceZipSha256.Length);
        Assert.All(
            catalog.Definitions.Where(value => value.PlaintextAvailable),
            value => Assert.Matches("^[0-9A-F]{64}$", value.PlaintextSha256!));
        Assert.All(
            catalog.Definitions.Where(value => !value.PlaintextAvailable),
            value => Assert.Null(value.PlaintextSha256));
    }

    [Fact]
    public void Exact_match_requires_direction_boundary_opcode_length_and_sha256()
    {
        var catalog = new OfficialDecryptedPacketCorpusCatalog();
        var plaintext = Convert.FromHexString("0500300025");

        var match = catalog.MatchExactPlaintext(PacketDirection.ClientToServer, plaintext);

        Assert.NotNull(match);
        Assert.True(match.ExactSample);
        Assert.Equal("evidence-package-20260806-c2s-opcode-30", match.Definition.Id);
        Assert.Null(catalog.MatchExactPlaintext(PacketDirection.ServerToClient, plaintext));

        plaintext[^1] ^= 0x01;
        Assert.Null(catalog.MatchExactPlaintext(PacketDirection.ClientToServer, plaintext));
        Assert.Equal(2, catalog.MatchPlaintextFamily(PacketDirection.ClientToServer, plaintext).Count);
    }

    [Fact]
    public void Missing_plaintext_definition_never_matches_as_available_family()
    {
        var catalog = new OfficialDecryptedPacketCorpusCatalog();
        var inferred = Convert.FromHexString("0500680000");

        Assert.Empty(catalog.MatchPlaintextFamily(PacketDirection.ClientToServer, inferred));
        Assert.Null(catalog.MatchExactPlaintext(PacketDirection.ClientToServer, inferred));
    }
}
