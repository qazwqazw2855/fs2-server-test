using System.Buffers.Binary;
using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M3NpcReplicationEvidenceAnalyzerTests
{
    [Fact]
    public void Pinned_runtime_contract_recovers_spawn_update_and_despawn_lengths_and_consumers()
    {
        var runtime = BuildSyntheticRuntimeCode();

        var result = M3NpcReplicationEvidenceAnalyzer.RecoverContract(runtime);

        Assert.Collection(
            result,
            spawn =>
            {
                Assert.Equal("0x72", spawn.Opcode);
                Assert.Equal(21, spawn.ApplicationLength);
                Assert.Equal("VerifiedByCapturedRecordAndStaticConsumer", spawn.EvidenceStatus);
            },
            update =>
            {
                Assert.Equal("0x60", update.Opcode);
                Assert.Equal(21, update.ApplicationLength);
                Assert.Equal("VerifiedByCapturedRecordAndStaticConsumer", update.EvidenceStatus);
            },
            despawn =>
            {
                Assert.Equal("0x75", despawn.Opcode);
                Assert.Equal(5, despawn.ApplicationLength);
                Assert.Equal("DerivedFromStaticConsumer", despawn.EvidenceStatus);
            });
    }

    [Fact]
    public void Pinned_runtime_contract_fails_closed_when_despawn_consumer_changes()
    {
        var runtime = BuildSyntheticRuntimeCode();
        runtime[M3NpcReplicationEvidenceAnalyzer.DespawnDispatchRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva] ^= 0x01;

        Assert.Throws<InvalidDataException>(() =>
            M3NpcReplicationEvidenceAnalyzer.RecoverContract(runtime));
    }

    [Fact]
    public void Pinned_runtime_contract_fails_closed_when_cleanup_target_changes()
    {
        var runtime = BuildSyntheticRuntimeCode();
        BinaryPrimitives.WriteInt32LittleEndian(
            runtime.AsSpan(
                M3NpcReplicationEvidenceAnalyzer.EntityRemovalCleanupCallRva -
                M2WorldContentEvidenceAnalyzer.SnapshotBaseRva + 1,
                4),
            0);

        Assert.Throws<InvalidDataException>(() =>
            M3NpcReplicationEvidenceAnalyzer.RecoverContract(runtime));
    }

    private static byte[] BuildSyntheticRuntimeCode()
    {
        const int defaultReturnRva = 0x166900;
        const int spawnUpdateReturnRva = 0x166910;
        const int despawnReturnRva = 0x166920;
        var requiredEndRva = Math.Max(despawnReturnRva + 7, M3NpcReplicationEvidenceAnalyzer.EntityCleanupRva + 96);
        var runtime = new byte[requiredEndRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva];

        Convert.FromHexString("558BEC0FB645083DFE0000000F873B010000FF2485B4645600").CopyTo(
            runtime,
            M2WorldContentEvidenceAnalyzer.IncomingLengthFunctionRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva);
        var tableOffset = M2WorldContentEvidenceAnalyzer.IncomingLengthJumpTableRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        for (var opcode = 0; opcode <= 0xFE; opcode++)
        {
            var returnRva = opcode is 0x60 or 0x72
                ? spawnUpdateReturnRva
                : opcode == 0x75 ? despawnReturnRva : defaultReturnRva;
            BinaryPrimitives.WriteUInt32LittleEndian(
                runtime.AsSpan(tableOffset + (opcode * 4), 4),
                checked((uint)(M2WorldContentEvidenceAnalyzer.ImageBase + returnRva)));
        }

        WriteReturn(runtime, defaultReturnRva, 1);
        WriteReturn(runtime, spawnUpdateReturnRva, 21);
        WriteReturn(runtime, despawnReturnRva, 5);
        Convert.FromHexString("0FB747018D8B20C7470150E8258A0600E9F1ECFFFF").CopyTo(
            runtime,
            M3NpcReplicationEvidenceAnalyzer.DespawnDispatchRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva);
        Convert.FromHexString("558BEC568BF133C08B4D088D56706690390A74134081C2D80100003D000100007CEE").CopyTo(
            runtime,
            M3NpcReplicationEvidenceAnalyzer.EntityRemovalRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva);
        Convert.FromHexString("558BEC0FBF4508535669F0D801000083CBFF578BF903F70FB7467A6685C07838").CopyTo(
            runtime,
            M3NpcReplicationEvidenceAnalyzer.EntityCleanupRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva);

        var callOffset = M3NpcReplicationEvidenceAnalyzer.EntityRemovalCleanupCallRva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        runtime[callOffset] = 0xE8;
        BinaryPrimitives.WriteInt32LittleEndian(
            runtime.AsSpan(callOffset + 1, 4),
            M3NpcReplicationEvidenceAnalyzer.EntityCleanupRva -
            (M3NpcReplicationEvidenceAnalyzer.EntityRemovalCleanupCallRva + 5));
        return runtime;
    }

    private static void WriteReturn(byte[] runtime, int rva, int value)
    {
        var offset = rva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva;
        runtime[offset] = 0xB8;
        BinaryPrimitives.WriteInt32LittleEndian(runtime.AsSpan(offset + 1, 4), value);
        runtime[offset + 5] = 0x5D;
        runtime[offset + 6] = 0xC3;
    }
}
