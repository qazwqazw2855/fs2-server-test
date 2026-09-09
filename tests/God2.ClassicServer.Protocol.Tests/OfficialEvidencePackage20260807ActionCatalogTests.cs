using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Protocol.Tests;

public sealed class OfficialEvidencePackage20260807ActionCatalogTests
{
    [Fact]
    public void Catalog_exposes_reviewed_structural_action_and_candidate_counts_without_authority()
    {
        var catalog = new OfficialEvidencePackage20260807ActionCatalog();

        Assert.Empty(catalog.Validate());
        Assert.Equal(64, catalog.SnapshotNewDecodedFrameFamilies().Count);
        Assert.Equal(88, catalog.SnapshotNewDecodedFrameFamilies().Sum(value => value.ObservedCount));
        Assert.Equal(83, catalog.SnapshotNewDecodedFrameFamilies().Sum(value => value.UniqueFrameCount));
        Assert.Equal(116, catalog.SnapshotActionPatterns().Count);
        Assert.Equal(655, catalog.SnapshotActionPatterns().Sum(value => value.Occurrences));
        Assert.All(catalog.SnapshotActionPatterns(), value =>
        {
            Assert.Equal("Unknown", value.SemanticStatus);
            Assert.False(value.ProductionEligible);
        });
        Assert.Equal(3, catalog.SnapshotLifecycleFieldCandidates().Count);
        Assert.All(catalog.SnapshotLifecycleFieldCandidates(), value => Assert.False(value.ProductionEnabled));
    }

    [Fact]
    public void Structural_match_and_action_lookup_require_exact_direction_opcode_and_length()
    {
        var catalog = new OfficialEvidencePackage20260807ActionCatalog();
        var frame = new byte[48];
        frame[0] = 48;
        frame[2] = 0x17;

        var match = catalog.MatchNewDecodedFrame(PacketDirection.ClientToServer, frame);

        Assert.NotNull(match);
        Assert.Equal(2, match.ObservedCount);
        Assert.Null(catalog.MatchNewDecodedFrame(PacketDirection.ServerToClient, frame));
        Assert.Equal(2, catalog.FindActionPatterns(0x17, 48).Count);
        Assert.Single(catalog.FindActionPatterns(0x18, 5));
        Assert.Empty(catalog.FindActionPatterns(0x18, 48));

        frame[0] = 47;
        Assert.Null(catalog.MatchNewDecodedFrame(PacketDirection.ClientToServer, frame));
    }

    [Fact]
    public void Lifecycle_candidate_decoder_is_checksum_gated_and_never_allows_mutation()
    {
        var catalog = new OfficialEvidencePackage20260807ActionCatalog();
        var create = CreateFrame(48, 0x17);
        "RayCatTest"u8.CopyTo(create.AsSpan(3));
        create[^1] = OfficialLoginWireTransform.ComputeChecksum(create);

        Assert.True(catalog.TryDecodeCharacterLifecycleCandidate(create, out var createCandidate));
        Assert.NotNull(createCandidate);
        Assert.Equal("CharacterCreateRequestCandidate", createCandidate.OperationCandidate);
        Assert.Equal("RayCatTest", createCandidate.CharacterNameCandidate);
        Assert.True(createCandidate.ChecksumVerified);
        Assert.False(createCandidate.RuntimeMutationAllowed);

        var slot = CreateFrame(5, 0x18);
        slot[3] = 1;
        slot[^1] = OfficialLoginWireTransform.ComputeChecksum(slot);

        Assert.True(catalog.TryDecodeCharacterLifecycleCandidate(slot, out var slotCandidate));
        Assert.Equal((byte)1, slotCandidate!.SlotOrIndexCandidate);
        Assert.Equal("CharacterLifecycleSlotActionCandidate", slotCandidate.OperationCandidate);
        Assert.False(slotCandidate.RuntimeMutationAllowed);

        slot[^1]++;
        Assert.False(catalog.TryDecodeCharacterLifecycleCandidate(slot, out _));
    }

    private static byte[] CreateFrame(int length, byte opcode)
    {
        var frame = new byte[length];
        frame[0] = (byte)(length & 0xFF);
        frame[1] = (byte)(length >> 8);
        frame[2] = opcode;
        return frame;
    }
}
