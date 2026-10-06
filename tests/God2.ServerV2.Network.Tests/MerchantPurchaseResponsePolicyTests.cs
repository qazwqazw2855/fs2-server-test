using God2.ServerV2.Application;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantPurchaseResponsePolicyTests
{
    private const string Build = OfficialMerchantPurchaseResultCodec.ClientBuildId;
    private static readonly Guid InventoryId = Guid.NewGuid();
    private static readonly Guid TransactionId = Guid.NewGuid();
    private static readonly OfficialMerchantTransactionSelection Selection =
        new(3954, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);
    private static readonly MerchantPurchaseCommand Command =
        new(TransactionId, "fixture-key", 253231541, 1, InventoryId, 3, 4, 5);
    private static readonly CharacterInventorySnapshot Before =
        new(InventoryId, 7, 8, 3, 4, "Clean", []);
    private static readonly CharacterInventorySnapshot After =
        new(InventoryId, 7, 8, 4, 5, "Clean",
            [new CharacterInventorySlot(0, 253231541, 1)]);
    private static readonly MerchantPurchaseResult Result =
        new(MerchantPurchaseStatus.Purchased, TransactionId, 50000, 25000, 6, 3, 4);

    [Fact]
    public void MatchingCommitProjectsExistingCapturedResponse()
    {
        Assert.True(MerchantPurchaseResponsePolicy.ValidateBeforeExecution(
            Build, Selection, Command, Before, out _));
        Assert.True(MerchantPurchaseResponsePolicy.TryEncodeCommitted(
            Build, Selection, Command, Before, After, Result,
            out var actual, out var failure));
        Assert.Equal("", failure);
        Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
            Build, Selection, 25000, out var expected, out _));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, "InventoryLayoutEvidenceBlocked")]
    [InlineData(1, "InventorySnapshotMismatch")]
    [InlineData(2, "FreshCommittedPurchaseRequired")]
    [InlineData(3, "CommittedResultMismatch")]
    [InlineData(4, "CommittedResultMismatch")]
    [InlineData(5, "CommittedInventoryMismatch")]
    [InlineData(6, "CommittedInventoryMismatch")]
    [InlineData(7, "CommittedInventoryMismatch")]
    public void UnsupportedOrInconsistentStateProducesNoResponse(
        int scenario, string expected)
    {
        var before = Before;
        var after = After;
        var result = Result;
        switch (scenario)
        {
            case 0: before = before with
                { Slots = [new CharacterInventorySlot(0, 253231541, 1)] }; break;
            case 1: before = before with { Version = 2 }; break;
            case 2: result = result with { Status = MerchantPurchaseStatus.Replayed }; break;
            case 3: result = result with { TransactionId = Guid.NewGuid() }; break;
            case 4: result = result with { WalletVersionAfter = 7 }; break;
            case 5: after = after with
                { Slots = [new CharacterInventorySlot(1, 253231541, 1)] }; break;
            case 6: after = after with
                { Slots = [new CharacterInventorySlot(0, 253231541, 2)] }; break;
            case 7: after = after with { MutationSequence = 6 }; break;
        }

        Assert.False(MerchantPurchaseResponsePolicy.TryEncodeCommitted(
            Build, Selection, Command, before, after, result,
            out var encoded, out var failure));
        Assert.Null(encoded);
        Assert.Equal(expected, failure);
    }
}
