using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.OfflineClientReverseEngineering;

public sealed record M3NpcReplicationFunctionEvidence(
    string Purpose,
    string Rva,
    string WindowSha256,
    string EvidenceStatus);

public sealed record M3NpcReplicationOpcodeEvidence(
    string Opcode,
    int ApplicationLength,
    string ClientConsumer,
    string DynamicFields,
    string EvidenceStatus);

public sealed record M3NpcReplicationEvidence(
    string SchemaVersion,
    string Status,
    string OfficialClientBuildId,
    string OfficialClientSha256,
    string RuntimeCodeSha256,
    IReadOnlyList<M3NpcReplicationOpcodeEvidence> Opcodes,
    IReadOnlyList<M3NpcReplicationFunctionEvidence> ClientFunctions,
    IReadOnlyList<string> SafetyAssertions,
    IReadOnlyList<string> RemainingGaps,
    bool RawUnknownPacketBodiesRetained,
    int NetworkEmission,
    int FakeNetworkBytes);

public static class M3NpcReplicationEvidenceAnalyzer
{
    public const string SchemaVersion = "god2-roadmap-m3-npc-replication-evidence-v1";
    public const int DespawnDispatchRva = 0x0009057B;
    public const int EntityRemovalRva = 0x000F8FB0;
    public const int EntityCleanupRva = 0x000F94F0;
    public const int EntityRemovalCleanupCallRva = 0x000F9067;

    private static readonly byte[] DespawnDispatchPrefix =
        Convert.FromHexString("0FB747018D8B20C7470150E8258A0600E9F1ECFFFF");

    private static readonly byte[] EntityRemovalPrefix =
        Convert.FromHexString("558BEC568BF133C08B4D088D56706690390A74134081C2D80100003D000100007CEE");

    private static readonly byte[] EntityCleanupPrefix =
        Convert.FromHexString("558BEC0FBF4508535669F0D801000083CBFF578BF903F70FB7467A6685C07838");

    public static async Task<M3NpcReplicationEvidence> AnalyzeAsync(
        string clientExecutablePath,
        string runtimeCodePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = BoundedFile.ReadAllBytes(clientExecutablePath, 8 * 1024 * 1024, "Official Client executable");
        RequireHash(client, M2WorldContentEvidenceAnalyzer.OfficialClientSha256, "Official Client executable");
        var runtimeCode = BoundedFile.ReadAllBytes(runtimeCodePath, 16 * 1024 * 1024, "Read-only runtime code snapshot");
        RequireHash(runtimeCode, M2WorldContentEvidenceAnalyzer.RuntimeCodeSha256, "Read-only runtime code snapshot");

        var opcodes = RecoverContract(runtimeCode);
        await Task.CompletedTask;
        return new M3NpcReplicationEvidence(
            SchemaVersion,
            "PASS_WITH_DOCUMENTED_GAPS",
            M2WorldContentEvidenceAnalyzer.OfficialClientBuildId,
            M2WorldContentEvidenceAnalyzer.OfficialClientSha256,
            M2WorldContentEvidenceAnalyzer.RuntimeCodeSha256,
            opcodes,
            [
                Function("S2C 0x75 dispatch reads the low 16-bit entity handle and invokes the entity-removal routine", runtimeCode, DespawnDispatchRva, DespawnDispatchPrefix.Length, "Verified"),
                Function("Entity-removal routine searches the bounded runtime entity slots and invokes cleanup", runtimeCode, EntityRemovalRva, 192, "Verified"),
                Function("Entity cleanup routine reached by the 0x75 removal path", runtimeCode, EntityCleanupRva, 96, "Verified")
            ],
            [
                "The incoming packet-length switch is recovered from the exact build-pinned runtime snapshot.",
                "0x75 consumes the low 16-bit entity handle; the two remaining fixed-length bytes are not read by this dispatch branch and remain zero.",
                "The entity-removal routine compares the handle against at most 256 runtime entity slots before invoking cleanup.",
                "Known 0x72 and 0x60 application records remain pinned by SHA-256; no unknown packet body is retained here.",
                "Analysis is read-only and emits no network traffic."
            ],
            [
                "No raw official 0x75 application record exists in the retained captures; its serializer is statically derived from the fixed-length and receive-consumer contract.",
                "Official Client lifecycle acceptance for spawn, AOI leave, despawn and reconnect has not yet been executed.",
                "Monster spawn/update/despawn family evidence remains unresolved."
            ],
            RawUnknownPacketBodiesRetained: false,
            NetworkEmission: 0,
            FakeNetworkBytes: 0);
    }

    public static IReadOnlyList<M3NpcReplicationOpcodeEvidence> RecoverContract(ReadOnlySpan<byte> runtimeCode)
    {
        RequireBytes(runtimeCode, DespawnDispatchRva, DespawnDispatchPrefix, "0x75 dispatch");
        RequireBytes(runtimeCode, EntityRemovalRva, EntityRemovalPrefix, "entity removal routine");
        RequireBytes(runtimeCode, EntityCleanupRva, EntityCleanupPrefix, "entity cleanup routine");

        var call = SliceAtRva(runtimeCode, EntityRemovalCleanupCallRva, 5);
        if (call[0] != 0xE8)
        {
            throw new InvalidDataException("The entity-removal cleanup call is not a direct call.");
        }

        var target = checked(EntityRemovalCleanupCallRva + 5 + BinaryPrimitives.ReadInt32LittleEndian(call[1..]));
        if (target != EntityCleanupRva)
        {
            throw new InvalidDataException($"The entity-removal cleanup call targets 0x{target:X8}, expected 0x{EntityCleanupRva:X8}.");
        }

        var lengths = M2WorldContentEvidenceAnalyzer.RecoverIncomingPacketLengths(runtimeCode);
        RequireLength(lengths, 0x72, 21);
        RequireLength(lengths, 0x60, 21);
        RequireLength(lengths, 0x75, 5);
        return
        [
            new("0x72", lengths[0x72], "Runtime entity construction and packed-position consumer", "entity handle, type/ordinal selector, direction/state, packed X/Y", "VerifiedByCapturedRecordAndStaticConsumer"),
            new("0x60", lengths[0x60], "Runtime entity lookup and packed-position update consumer", "entity handle, interpolation, state, packed X/Y", "VerifiedByCapturedRecordAndStaticConsumer"),
            new("0x75", lengths[0x75], "Bounded runtime entity lookup and cleanup", "low 16-bit entity handle; remaining two bytes ignored by this branch", "DerivedFromStaticConsumer")
        ];
    }

    private static M3NpcReplicationFunctionEvidence Function(
        string purpose,
        ReadOnlySpan<byte> runtimeCode,
        int rva,
        int length,
        string status) =>
        new(purpose, $"0x{rva:X8}", Hash(SliceAtRva(runtimeCode, rva, length)), status);

    private static void RequireLength(IReadOnlyDictionary<byte, int> lengths, byte opcode, int expected)
    {
        if (!lengths.TryGetValue(opcode, out var actual) || actual != expected)
        {
            throw new InvalidDataException($"Opcode 0x{opcode:X2} length is {actual}, expected {expected}.");
        }
    }

    private static void RequireBytes(ReadOnlySpan<byte> source, int rva, ReadOnlySpan<byte> expected, string purpose)
    {
        if (!SliceAtRva(source, rva, expected.Length).SequenceEqual(expected))
        {
            throw new InvalidDataException($"The build-pinned {purpose} no longer matches its expected instruction bytes.");
        }
    }

    private static ReadOnlySpan<byte> SliceAtRva(ReadOnlySpan<byte> source, int rva, int length)
    {
        var offset = checked(rva - M2WorldContentEvidenceAnalyzer.SnapshotBaseRva);
        if (offset < 0 || length < 0 || offset > source.Length - length)
        {
            throw new InvalidDataException($"Runtime evidence window 0x{rva:X8} is outside the bounded snapshot.");
        }

        return source.Slice(offset, length);
    }

    private static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value));

    private static void RequireHash(ReadOnlySpan<byte> value, string expected, string purpose)
    {
        var actual = Hash(value);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{purpose} SHA-256 {actual} does not match the evidence pin.");
        }
    }
}
