using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class OfficialMerchantSaleResultCodecTests
{
    private static OfficialMerchantTransactionSelection Selection(
        ushort handle = 3954, ushort item = 6901, byte quantity = 1,
        OfficialMerchantTransactionOperation operation =
            OfficialMerchantTransactionOperation.Sell,
        ushort slot = 4) => new(handle, item, quantity, operation, slot);

    [Fact]
    public void ReplaysExistingClassicStageSixFrame()
    {
        Assert.True(OfficialMerchantSaleResultCodec.TryEncode(
            OfficialMerchantSaleResultCodec.ClientBuildId,
            Selection(), 25004, out var encoded, out var failure));
        Assert.Empty(failure);
        Assert.NotNull(encoded);
        Assert.Equal(25, encoded!.Length);
        Assert.Equal(
            OfficialWorldBootstrapCodec.EncodeFrame(Convert.FromHexString(
                "1900410000040027AC61000069720F00000100410000040062")),
            encoded);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(4294967295L)]
    public void AcceptsWireWalletBounds(long balance)
    {
        Assert.True(OfficialMerchantSaleResultCodec.TryEncode(
            OfficialMerchantSaleResultCodec.ClientBuildId,
            Selection(), balance, out var encoded, out var failure));
        Assert.Empty(failure);
        Assert.Equal(25, encoded!.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void RejectsUnsupportedProfilesWithoutFrame(int scenario)
    {
        var build = OfficialMerchantSaleResultCodec.ClientBuildId;
        var selection = Selection();
        long balance = 25004;
        switch (scenario)
        {
            case 0: build = "other-build"; break;
            case 1: selection = Selection(
                operation: OfficialMerchantTransactionOperation.Buy); break;
            case 2: selection = Selection(handle: 4000); break;
            case 3: selection = Selection(item: 6906); break;
            case 4: selection = Selection(quantity: 2); break;
            case 5: selection = Selection(slot: 0); break;
            case 6: balance = -1; break;
            case 7: balance = 4294967296L; break;
        }
        Assert.False(OfficialMerchantSaleResultCodec.TryEncode(
            build, selection, balance, out var encoded, out var failure));
        Assert.Null(encoded);
        Assert.NotEmpty(failure);
    }
}
