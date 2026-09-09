using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Protocol;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M5BattleEvidenceRecoveryTests
{
    [Fact]
    public void ParseCommand35ForEvidence_UsesThreeUInt16TargetGroups()
    {
        var payload = Convert.FromHexString("06030119010010000020000001010000");

        var candidate = M5BattleEvidenceRecovery.ParseCommand35ForEvidence(0x35, payload);

        Assert.NotNull(candidate);
        Assert.Equal(6, candidate.PositionIndexCandidate);
        Assert.Equal(3, candidate.ActionCodeCandidate);
        Assert.False(candidate.Continuation);
        Assert.Equal(1, candidate.SideFlagCandidate);
        Assert.Equal(25, candidate.ReservedByteCandidate);
        Assert.Equal((ushort)0x0001, candidate.TargetMaskGroup0);
        Assert.Equal((ushort)0x0010, candidate.TargetMaskGroup1);
        Assert.Equal((ushort)0x2000, candidate.TargetMaskGroup2);
        Assert.Equal((ushort)0, candidate.BattleContextCandidate);
        Assert.Equal((uint)257, candidate.ActionParameterCandidate);
    }

    [Fact]
    public void ParseCommand35ForEvidence_RejectsWrongOpcodeOrLength()
    {
        Assert.Null(M5BattleEvidenceRecovery.ParseCommand35ForEvidence(0x36, new byte[16]));
        Assert.Null(M5BattleEvidenceRecovery.ParseCommand35ForEvidence(0x35, new byte[15]));
        Assert.Null(M5BattleEvidenceRecovery.ParseCommand35ForEvidence(0x35, null));
    }

    [Fact]
    public void CapturedCommand35Metadata_RoundTripsThroughTheProductionLayoutDecoder()
    {
        var payload = Convert.FromHexString("060B0000000000000000000000100000");
        var application = new byte[payload.Length + 1];
        application[0] = OfficialBattleCommandWireCodec.CommandOpcode;
        payload.CopyTo(application, 1);
        Assert.Equal(
            "A360954945CAE1CB00F7893F68F703310134589C3A50C0485595E17DFAA328DD",
            Convert.ToHexString(SHA256.HashData(application)));

        var evidence = M5BattleEvidenceRecovery.ParseCommand35ForEvidence(application[0], payload);
        Assert.NotNull(evidence);

        var frame = new byte[OfficialBattleCommandWireCodec.DecodedFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        application.CopyTo(frame, 2);
        frame[^1] = OfficialLoginWireTransform.ComputeChecksum(frame);
        var decoded = OfficialBattleCommandWireCodec.DecodeLayout(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.Battle,
            frame);

        Assert.True(decoded.LayoutDecoded);
        Assert.NotNull(decoded.Value);
        Assert.Equal(evidence.PositionIndexCandidate, decoded.Value.PositionIndex);
        Assert.Equal(evidence.ActionCodeCandidate, decoded.Value.ActionCode);
        Assert.Equal(evidence.TargetMaskGroup0, decoded.Value.TargetMaskGroup0);
        Assert.Equal(evidence.TargetMaskGroup1, decoded.Value.TargetMaskGroup1);
        Assert.Equal(evidence.TargetMaskGroup2, decoded.Value.TargetMaskGroup2);
        Assert.Equal(evidence.BattleContextCandidate, decoded.Value.BattleContext);
        Assert.Equal(evidence.ActionParameterCandidate, decoded.Value.ActionParameter);
    }
}
