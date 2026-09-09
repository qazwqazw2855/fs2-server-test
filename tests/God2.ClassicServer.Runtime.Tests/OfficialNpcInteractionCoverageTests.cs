using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialNpcInteractionCoverageTests
{
    [Fact]
    public void Coverage_distinguishes_verified_open_profiles_from_missing_operations()
    {
        var coverage = OfficialNpcInteractionCoverage.Snapshot();

        Assert.Equal([1504, 3793, 3954], coverage.Select(entry => (int)entry.ClientEntityHandle));
        Assert.Equal("Dialog", coverage[0].InteractionFamily);
        Assert.Equal("OpenOnly", coverage[0].CompletionStatus);
        Assert.Contains("0x86 close semantics", coverage[0].MissingOperations, StringComparison.Ordinal);
        Assert.Equal("CompleteObservedDialogSlice", coverage[1].CompletionStatus);
        Assert.Contains("Other selector values", coverage[1].MissingOperations, StringComparison.Ordinal);
        Assert.Contains("0x86 semantics", coverage[1].MissingOperations, StringComparison.Ordinal);
        Assert.Equal("Merchant", coverage[2].InteractionFamily);
        Assert.All(coverage, entry => Assert.Equal("Verified", entry.EvidenceStatus));
        Assert.False(OfficialNpcInteractionCoverage.HasVerifiedOpenProfile(4638));
    }

    [Fact]
    public void Coverage_keeps_request_only_live_handles_blocked()
    {
        var gaps = OfficialNpcInteractionCoverage.SnapshotBlockedGaps();

        Assert.Equal([3784, 3960, 5004], gaps.Select(entry => (int)entry.ClientEntityHandle));
        Assert.All(gaps, entry =>
        {
            Assert.Equal("BlockedMissingResponse", entry.EvidenceStatus);
            Assert.Contains("No correlated S2C", entry.MissingEvidence, StringComparison.Ordinal);
            Assert.False(OfficialNpcInteractionCoverage.HasVerifiedOpenProfile(entry.ClientEntityHandle));
        });
    }
}
