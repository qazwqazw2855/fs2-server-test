using System.Buffers.Binary;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialMerchantPurchaseResultCodecTests
{
    private static readonly OfficialMerchantTransactionSelection Selection =
        new(3954, 6901, 1, OfficialMerchantTransactionOperation.Buy, 7);

    [Fact]
    public void ReproducesExistingClassicStageFiveCapture()
    {
        var captured = Convert.FromHexString(
            "39003B01040000F51A00000000000000000000000000000000000000000000000000000000000027A861000069720F0000010041000007000B");

        Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            Selection, 25000, out var encoded, out var failure));
        Assert.Equal("", failure);
        Assert.NotNull(encoded);
        Assert.Equal(
            OfficialWorldBootstrapCodec.EncodeFrame(captured), encoded);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(4294967295L)]
    public void WalletProjectionPreservesAllOtherCapturedBytes(long balance)
    {
        Assert.True(OfficialMerchantPurchaseResultCodec.TryEncode(
            OfficialMerchantPurchaseResultCodec.ClientBuildId,
            Selection, balance, out var encoded, out _));

        var expected = Convert.FromHexString(
            "39003B01040000F51A00000000000000000000000000000000000000000000000000000000000027A861000069720F0000010041000007000B");
        BinaryPrimitives.WriteUInt32LittleEndian(
            expected.AsSpan(40), checked((uint)balance));
        expected[^1] = OfficialLoginWireTransform.ComputeChecksum(expected);
        Assert.Equal(OfficialWorldBootstrapCodec.EncodeFrame(expected), encoded);
    }

    [Theory]
    [InlineData(0, "BuildMismatch")]
    [InlineData(1, "UnsupportedOperation")]
    [InlineData(2, "ProfileEvidenceBlocked")]
    [InlineData(3, "ProfileEvidenceBlocked")]
    [InlineData(4, "QuantityEvidenceBlocked")]
    [InlineData(5, "SelectionEvidenceBlocked")]
    [InlineData(6, "WalletBalanceOutOfRange")]
    [InlineData(7, "WalletBalanceOutOfRange")]
    public void UnsupportedProfilesProduceNoFrame(int scenario, string expected)
    {
        var build = OfficialMerchantPurchaseResultCodec.ClientBuildId;
        var selection = Selection;
        long balance = 25000;
        switch (scenario)
        {
            case 0: build = "unknown-build"; break;
            case 1: selection = selection with
                { Operation = OfficialMerchantTransactionOperation.Sell }; break;
            case 2: selection = selection with { ClientEntityHandle = 5042 }; break;
            case 3: selection = selection with { ClientItemId = 6906 }; break;
            case 4: selection = selection with { Quantity = 2 }; break;
            case 5: selection = selection with
                { CatalogIndexOrClientInventorySlot = 8 }; break;
            case 6: balance = -1; break;
            case 7: balance = 4294967296L; break;
        }

        Assert.False(OfficialMerchantPurchaseResultCodec.TryEncode(
            build, selection, balance, out var encoded, out var failure));
        Assert.Null(encoded);
        Assert.Equal(expected, failure);
    }
}
