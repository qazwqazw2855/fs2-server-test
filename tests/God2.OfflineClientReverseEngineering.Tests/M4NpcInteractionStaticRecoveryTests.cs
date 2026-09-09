using System.Security.Cryptography;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M4NpcInteractionStaticRecoveryTests
{
    [Theory]
    [InlineData("push 18h", 0x18)]
    [InlineData("push 5", 5)]
    [InlineData("push eax", null)]
    public void ParsesOnlyImmediatePushArguments(string instruction, int? expected)
    {
        Assert.Equal(expected, M4NpcInteractionStaticRecovery.TryParsePushImmediate(instruction));
    }

    [Fact]
    public void RecoversApplicationPayloadGroupWithoutClaimingCaptureAttribution()
    {
        var bytes = Enumerable.Repeat((byte)0xCC, 0x200).ToArray();
        var functionOffset = 0x40;
        var callRva = RuntimeCodeAnalyzer.CodeStartRva + (uint)functionOffset + 12;
        var callTarget = RuntimeCodeAnalyzer.ImageBase + M4NpcInteractionStaticRecovery.OutboundEnqueueRva;
        bytes[functionOffset] = 0x55;
        bytes[functionOffset + 1] = 0x8B;
        bytes[functionOffset + 2] = 0xEC;
        bytes[functionOffset + 3] = 0x6A;
        bytes[functionOffset + 4] = 0x00;
        bytes[functionOffset + 5] = 0x8D;
        bytes[functionOffset + 6] = 0x45;
        bytes[functionOffset + 7] = 0xF8;
        bytes[functionOffset + 8] = 0x50;
        bytes[functionOffset + 9] = 0x6A;
        bytes[functionOffset + 10] = 0x37;
        bytes[functionOffset + 11] = 0x90;
        bytes[functionOffset + 12] = 0xE8;
        var callAddress = RuntimeCodeAnalyzer.ImageBase + callRva;
        BitConverter.GetBytes(checked((int)(callTarget - (callAddress + 5))))
            .CopyTo(bytes, functionOffset + 13);
        bytes[functionOffset + 17] = 0xC3;

        var path = Path.Combine(Path.GetTempPath(), $"god2-m4-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        try
        {
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var snapshot = M4NpcInteractionStaticRecovery.Recover(
                new RuntimeCodeAnalyzer(path),
                sha256);
            var recoveredCallsites = M4NpcInteractionStaticRecovery.RecoverApplicationCallsites(
                new RuntimeCodeAnalyzer(path));

            var group = Assert.Single(snapshot.ApplicationPayloadGroups);
            var callsite = Assert.Single(snapshot.SelectedApplicationCallsites);
            Assert.Single(recoveredCallsites);
            Assert.Equal(0x37, group.Opcode);
            Assert.Equal(0, group.PayloadLength);
            Assert.Equal(1, group.CallsiteCount);
            Assert.Equal(0x37, callsite.Opcode);
            Assert.Equal("ExactBuildStaticContractRecovered", snapshot.NpcSendBranchStatus);
            Assert.Equal("STATIC_RECOVERY_COMPLETE_CAPTURE_CORRELATION_SEPARATE", snapshot.Decision);
            Assert.False(snapshot.NetworkBytesEmitted);
            Assert.False(snapshot.FakeNetworkBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
