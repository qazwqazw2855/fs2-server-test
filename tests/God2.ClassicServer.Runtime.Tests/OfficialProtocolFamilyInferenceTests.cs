using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialProtocolFamilyInferenceTests
{
    [Fact]
    public void Current_build_trace_reconstructs_open_buy_and_sell_as_one_merchant_family()
    {
        var inference = OfficialProtocolFamilyInferenceEngine.CreateVerifiedCurrentBuild().Infer(
        [
            Frame(10, PacketDirection.ServerToClient, "180072720F00005260000081000000CF010000C0036C00A1"),
            Frame(11, PacketDirection.ClientToServer, "0A0085720F000000EE1A"),
            Frame(12, PacketDirection.ServerToClient, "1F007A1C000400720F900000000000000000000000000000000000000000D2"),
            Frame(13, PacketDirection.ServerToClient, "100068720FA000000000006400000081"),
            Frame(20, PacketDirection.ClientToServer, "0C0038720FF51A0101070071"),
            Frame(21, PacketDirection.ServerToClient, "39003B01040000F51A00000000000000000000000000000000000000000000000000000000000027A861000069720F0000010041000007000B"),
            Frame(30, PacketDirection.ClientToServer, "0C0038720FF51A010204006F"),
            Frame(31, PacketDirection.ServerToClient, "1900410000040027AC61000069720F00000100410000040062")
        ]);

        Assert.Equal(["Open", "Buy", "Sell"], inference.Select(result => result.Operation));
        Assert.All(inference, result =>
        {
            Assert.Equal("Merchant", result.Family);
            Assert.Equal(OfficialProtocolFamilyInferenceStatus.Verified, result.Status);
            Assert.Equal<ulong>(3954, result.CorrelatedFields["EntityHandle"]);
        });
        Assert.Equal<ulong>(6901, inference.Single(result => result.Operation == "Buy").CorrelatedFields["ObjectToken"]);
        Assert.Equal<ulong>(4, inference.Single(result => result.Operation == "Sell").CorrelatedFields["SlotToken"]);
    }

    [Fact]
    public void Similar_opcodes_with_a_different_handle_do_not_promote_a_family()
    {
        var inference = OfficialProtocolFamilyInferenceEngine.CreateVerifiedCurrentBuild().Infer(
        [
            Frame(1, PacketDirection.ServerToClient, "180072720F00005260000081000000CF010000C0036C00A1"),
            Frame(2, PacketDirection.ClientToServer, "0A0085720F000000EE1A"),
            Frame(3, PacketDirection.ServerToClient, WithChecksum("1F007A1C000400730F90000000000000000000000000000000000000000000")),
            Frame(4, PacketDirection.ServerToClient, "100068720FA000000000006400000081")
        ]);

        Assert.Empty(inference);
    }

    [Fact]
    public void Live_handle_3793_is_reconstructed_as_a_verified_npc_dialog_open_family()
    {
        var result = OfficialProtocolFamilyInferenceEngine.CreateVerifiedCurrentBuild().Infer(
        [
            Frame(1, PacketDirection.ClientToServer, "080037D10E0000C2"),
            Frame(2, PacketDirection.ServerToClient, "20007A1B000100D10EF8280780000000000000000000000000000000005D16F3")
        ]).Single();

        Assert.Equal("NpcDialog", result.Family);
        Assert.Equal("Open", result.Operation);
        Assert.Equal(OfficialProtocolFamilyInferenceStatus.Verified, result.Status);
        Assert.Equal<ulong>(3793, result.CorrelatedFields["EntityHandle"]);
    }

    [Fact]
    public void Custom_signature_uses_the_same_engine_without_an_operation_branch()
    {
        var signature = new OfficialProtocolFamilySignature(
            "Warehouse",
            "Open",
            [
                new OfficialFamilyFrameRule(
                    "Request",
                    PacketDirection.ClientToServer,
                    0x85,
                    10,
                    [new OfficialFamilyFieldBinding("EntityHandle", 3, 2)]),
                new OfficialFamilyFrameRule(
                    "Response",
                    PacketDirection.ServerToClient,
                    0x68,
                    16,
                    [new OfficialFamilyFieldBinding("EntityHandle", 3, 2)])
            ],
            MinimumCorrelatedFields: 1,
            MaximumSequenceSpan: 8,
            EvidenceVerified: false,
            EvidenceId: "test-candidate");

        var result = new OfficialProtocolFamilyInferenceEngine([signature]).Infer(
        [
            Frame(1, PacketDirection.ClientToServer, "0A0085720F000000EE1A"),
            Frame(2, PacketDirection.ServerToClient, "100068720FA000000000006400000081")
        ]).Single();

        Assert.Equal("Warehouse", result.Family);
        Assert.Equal(OfficialProtocolFamilyInferenceStatus.Correlated, result.Status);
    }

    private static OfficialFamilyObservedFrame Frame(long sequence, PacketDirection direction, string hex) =>
        new(sequence, direction, Convert.FromHexString(hex));

    private static string WithChecksum(string hexWithoutChecksum)
    {
        var frame = Convert.FromHexString(hexWithoutChecksum + "00");
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        return Convert.ToHexString(frame);
    }
}
