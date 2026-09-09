using System.Buffers.Binary;
using System.Security.Cryptography;

namespace God2.OfflineClientReverseEngineering;

public sealed record M5BattleStaticInstruction(
    string Purpose,
    string Rva,
    string Instruction);

public sealed record M5BattleReceiveDispatchEntry(
    string ObservedApplicationOpcode,
    string DecodeCallerRva,
    string DispatchTableRva,
    int DispatchIndex,
    string DispatchTargetRva,
    string DispatchTargetSha256,
    string Classification);

public sealed record M5BattleStaticSnapshot(
    string SchemaVersion,
    string Status,
    string OfficialClientBuildId,
    string OfficialClientSha256,
    string RuntimeCodeSha256,
    bool RuntimeCaptureAttested,
    string BuildAssociationStatus,
    string CommandBuilderRva,
    string OutboundEnqueueRva,
    string InboundDispatchRva,
    IReadOnlyList<M5BattleStaticInstruction> PinnedInstructions,
    IReadOnlyList<M5BattleReceiveDispatchEntry> ReceiveDispatchEntries,
    IReadOnlyList<string> ProvenFacts,
    IReadOnlyList<string> RemainingBlockers,
    bool ClientWriteAccessUsed,
    bool ClientBinaryModified,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

/// <summary>
/// Pins the M5 command builder and receive dispatcher to the exact, read-only official
/// Client runtime snapshot. It intentionally records instructions and hashes only; it does
/// not preserve packet bodies and it never executes Client code.
/// </summary>
public static class M5BattleStaticRecovery
{
    public const string ExpectedOfficialClientSha256 =
        "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string ExpectedRuntimeCodeSha256 =
        "E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A";
    public const string ExpectedLegacyBuildAssociationStatus =
        "LegacyCaptureHashBoundButNotCaptureTimeBuildAttested";
    public const uint CommandBuilderRva = 0x0014E7D0;
    public const uint InboundDispatchRva = 0x0008E536;
    public const uint InboundDispatchTableRva = 0x000911A8;
    public const uint Opcode82DedicatedTargetRva = 0x0009111E;
    public const uint BattleDecodeCallerRva = 0x00146E40;
    public const uint BattleDispatchRva = 0x00146E80;
    public const uint BattleDispatchIndexTableRva = 0x001475A8;
    public const uint BattleDispatchTargetTableRva = 0x001474AC;

    private static readonly byte[] ObservedServerOpcodes = [0x82, 0x83, 0x85, 0x86, 0x88, 0x89, 0xE5];

    private static readonly IReadOnlyDictionary<byte, (uint TargetRva, string TargetSha256)> ExpectedDispatchTargets =
        new Dictionary<byte, (uint, string)>
        {
            [0x82] = (0x0009111E, "8479AE559D5BF766AB5DB8A728FADB71F6F55A28E9A5F7241EC7A16C6C09612A"),
            [0x83] = (0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2"),
            [0x85] = (0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2"),
            [0x86] = (0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2"),
            [0x88] = (0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2"),
            [0x89] = (0x001470AA, "69C353715B9B01716D9E50137DDFE9A46E8EC3823FED86D896A6A2A5D3B494F2"),
            [0xE5] = (0x0008EBE4, "DF530C8BAFED50253886D4CBDEF86EFF4A7E14B56799D89F1E8CF5626EE123C7")
        };

    public static M5BattleStaticSnapshot Recover(
        RuntimeCodeAnalyzer analyzer,
        string runtimeCodePath,
        string runtimeCodeSha256,
        string officialClientSha256,
        bool runtimeCaptureAttested,
        string buildAssociationStatus)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCodeSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(officialClientSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildAssociationStatus);
        ValidateBuildIdentity(runtimeCodeSha256, officialClientSha256);
        ValidateBuildAssociation(runtimeCaptureAttested, buildAssociationStatus);

        using (var stream = File.OpenRead(runtimeCodePath))
        {
            var actualRuntimeCodeSha256 = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualRuntimeCodeSha256, ExpectedRuntimeCodeSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("M5 runtime code bytes do not match the exact pinned snapshot.");
            }
        }

        var pinned = new (string Purpose, uint Rva, string Text)[]
        {
            ("action parameter source", 0x0014E7E4, "mov bl,[ebp+8]"),
            ("position parameter source", 0x0014E80C, "mov bh,[ebp+0Ch]"),
            ("position upper bound", 0x0014E80F, "cmp bh,0Eh"),
            ("target count upper bound", 0x0014E818, "cmp dword [ebp+10h],0Eh"),
            ("payload position", 0x0014E85A, "mov [ebp-14h],bh"),
            ("payload action", 0x0014E87B, "mov [ebp-13h],dl"),
            ("target group divisor", 0x0014E9CB, "mov ecx,0Eh"),
            ("target bit insertion", 0x0014E9C5, "bts eax,edx"),
            ("payload length", 0x0014EA7F, "push 10h"),
            ("application opcode", 0x0014EA85, "push 35h"),
            ("outbound enqueue", 0x0014EA87, "call 0047FC10h"),
            ("continuation flag", 0x0014EA69, "or dl,80h"),
            ("alternate payload length", 0x0014EAD7, "push 10h"),
            ("alternate application opcode", 0x0014EADD, "push 35h"),
            ("alternate outbound enqueue", 0x0014EADF, "call 0047FC10h"),
            ("dedicated 0x82 comparison", 0x0008E500, "cmp esi,82h"),
            ("dedicated 0x82 branch", 0x0008E506, "je near 0049111Eh"),
            ("inbound opcode bound", 0x0008E52A, "cmp esi,0FDh"),
            ("inbound application dispatch", InboundDispatchRva, "jmp dword [esi*4+4911A8h]"),
            ("battle stream decoder", 0x00146E3B, "call 0047C0B0h"),
            ("battle application opcode", 0x00146E69, "mov cl,[esi]"),
            ("battle opcode upper bound", 0x00146E6E, "cmp eax,0FBh"),
            ("battle compressed dispatch index", 0x00146E79, "movzx eax,byte [eax+5475A8h]"),
            ("battle application dispatch", BattleDispatchRva, "jmp dword [eax*4+5474ACh]")
        }
        .Select(item =>
        {
            var instruction = RequireInstruction(analyzer, item.Rva, item.Text);
            return new M5BattleStaticInstruction(item.Purpose, Hex(item.Rva), instruction.Text);
        })
        .ToArray();

        var code = BoundedFile.ReadAllBytes(runtimeCodePath, 16L * 1024 * 1024, "M5 runtime code snapshot");
        try
        {
            var dispatch = ObservedServerOpcodes.Select(opcode =>
            {
                uint targetRva;
                uint decodeCallerRva;
                uint dispatchTableRva;
                int dispatchIndex;
                if (opcode == 0x82)
                {
                    targetRva = Opcode82DedicatedTargetRva;
                    decodeCallerRva = 0x0008E4F1;
                    dispatchTableRva = 0;
                    dispatchIndex = -1;
                }
                else if (opcode is 0x83 or 0x85 or 0x86 or 0x88 or 0x89)
                {
                    var indexOffset = checked((int)(BattleDispatchIndexTableRva - RuntimeCodeAnalyzer.CodeStartRva + opcode));
                    if (indexOffset < 0 || indexOffset >= code.Length)
                    {
                        throw new InvalidDataException($"M5 battle dispatch index 0x{opcode:X2} is outside the runtime snapshot.");
                    }

                    dispatchIndex = code[indexOffset];
                    var battleTargetTableOffset = checked((int)(
                        BattleDispatchTargetTableRva - RuntimeCodeAnalyzer.CodeStartRva + (uint)dispatchIndex * 4u));
                    if (battleTargetTableOffset < 0 || battleTargetTableOffset + sizeof(uint) > code.Length)
                    {
                        throw new InvalidDataException($"M5 battle dispatch target 0x{opcode:X2} is outside the runtime snapshot.");
                    }

                    var absoluteTarget = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(battleTargetTableOffset, sizeof(uint)));
                    if (absoluteTarget < RuntimeCodeAnalyzer.ImageBase)
                    {
                        throw new InvalidDataException($"M5 battle dispatch target 0x{opcode:X2} is not an image address.");
                    }

                    targetRva = absoluteTarget - RuntimeCodeAnalyzer.ImageBase;
                    decodeCallerRva = BattleDecodeCallerRva;
                    dispatchTableRva = BattleDispatchTargetTableRva;
                }
                else
                {
                    var entryOffset = checked((int)(InboundDispatchTableRva - RuntimeCodeAnalyzer.CodeStartRva + opcode * 4u));
                    if (entryOffset < 0 || entryOffset + sizeof(uint) > code.Length)
                    {
                        throw new InvalidDataException($"M5 receive dispatch entry 0x{opcode:X2} is outside the runtime snapshot.");
                    }

                    var absoluteTarget = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(entryOffset, sizeof(uint)));
                    if (absoluteTarget < RuntimeCodeAnalyzer.ImageBase)
                    {
                        throw new InvalidDataException($"M5 receive dispatch entry 0x{opcode:X2} is not an image address.");
                    }

                    targetRva = absoluteTarget - RuntimeCodeAnalyzer.ImageBase;
                    decodeCallerRva = 0x0008E4F1;
                    dispatchTableRva = InboundDispatchTableRva;
                    dispatchIndex = opcode;
                }
                var targetOffset = checked((int)(targetRva - RuntimeCodeAnalyzer.CodeStartRva));
                if (targetOffset < 0 || targetOffset >= code.Length)
                {
                    throw new InvalidDataException($"M5 receive dispatch target 0x{targetRva:X8} is outside the runtime snapshot.");
                }

                var windowLength = Math.Min(64, code.Length - targetOffset);
                var targetSha256 = Convert.ToHexString(SHA256.HashData(code.AsSpan(targetOffset, windowLength)));
                ValidateDispatchIdentity(opcode, targetRva, targetSha256);
                return new M5BattleReceiveDispatchEntry(
                    $"0x{opcode:X2}",
                    Hex(decodeCallerRva),
                    dispatchTableRva == 0 ? "DedicatedPreTableBranch" : Hex(dispatchTableRva),
                    dispatchIndex,
                    Hex(targetRva),
                    targetSha256,
                    opcode == 0x82
                        ? "DedicatedPreTableBranch_RequiresConsumerRecovery"
                        : decodeCallerRva == BattleDecodeCallerRva && targetRva == 0x00147092
                            ? "BattleSharedUpdateQueue_RequiresRecordLayoutRecovery"
                            : decodeCallerRva == BattleDecodeCallerRva
                                ? "BattleDedicatedHandler_RequiresRecordLayoutRecovery"
                                : targetRva == 0x0008F281
                                    ? "SharedDispatcherContinuation_RequiresConsumerRecovery"
                                    : "UniqueDispatchTarget_RequiresFieldRecovery");
            }).ToArray();

            return new M5BattleStaticSnapshot(
                "god2-roadmap-m5-battle-static-v2",
                runtimeCaptureAttested
                    ? "PASS_COMMAND_LAYOUT_PINNED_S2C_SEMANTICS_BLOCKED"
                    : "DERIVED_COMMAND_LAYOUT_PINNED_CAPTURE_TIME_BUILD_ATTESTATION_BLOCKED",
                M5BattleEvidenceRecovery.OfficialClientBuildId,
                officialClientSha256,
                runtimeCodeSha256,
                runtimeCaptureAttested,
                buildAssociationStatus,
                Hex(CommandBuilderRva),
                Hex(M4NpcInteractionStaticRecovery.OutboundEnqueueRva),
                Hex(InboundDispatchRva),
                pinned,
                dispatch,
                [
                    "The official Client sends application opcode 0x35 with an exact 16-byte payload.",
                    "Payload bytes 0..3 are position, action/continuation, side and reserved state.",
                    "Payload bytes 4..9 contain three little-endian UInt16 target masks; each group covers fourteen battle positions.",
                    "Payload bytes 10..11 preserve a battle-context value and bytes 12..15 preserve the action parameter.",
                    "The pinned runtime snapshot handles 0x82 through a dedicated pre-table branch and dispatches E5 through its world table; capture-time build association is recorded separately.",
                    "The independent Battle receive path decodes at RVA 0x00146E3B, reads the application opcode at RVA 0x00146E69 and dispatches through compressed-index tables at RVA 0x001475A8/0x001474AC."
                ],
                [
                    "Action code 1/3 and action-parameter semantics still require unique static consumer plus labelled-capture correlation before runtime mutation.",
                    "S2C 0x82/0x83/0x85/0x86/0x88/0x89 field layouts and Client state transitions are not yet sufficiently reconstructed for a production serializer.",
                    "No evidence-complete MariaDB Monster plus Skill vertical slice exists yet."
                ],
                ClientWriteAccessUsed: false,
                ClientBinaryModified: false,
                NetworkBytesEmitted: false,
                FakeNetworkBytes: false);
        }
        finally
        {
            Array.Clear(code);
        }
    }

    public static void ValidateBuildIdentity(string runtimeCodeSha256, string officialClientSha256)
    {
        if (!string.Equals(runtimeCodeSha256, ExpectedRuntimeCodeSha256, StringComparison.Ordinal) ||
            !string.Equals(officialClientSha256, ExpectedOfficialClientSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("M5 evidence is not bound to the exact official Client/runtime code identity.");
        }
    }

    public static void ValidateDispatchIdentity(byte opcode, uint targetRva, string targetSha256)
    {
        if (!ExpectedDispatchTargets.TryGetValue(opcode, out var expected) ||
            targetRva != expected.TargetRva ||
            !string.Equals(targetSha256, expected.TargetSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"M5 Battle dispatch identity changed for opcode 0x{opcode:X2}.");
        }
    }

    public static void ValidateBuildAssociation(bool captureAttested, string buildAssociationStatus)
    {
        if (captureAttested ||
            !string.Equals(
                buildAssociationStatus,
                ExpectedLegacyBuildAssociationStatus,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The legacy runtime manifest cannot establish capture-time executable attestation.");
        }
    }

    private static StaticInstruction RequireInstruction(RuntimeCodeAnalyzer analyzer, uint rva, string expected)
    {
        var instruction = analyzer.DecodeRange(rva, 16).FirstOrDefault(item => item.Rva == rva)
            ?? throw new InvalidDataException($"M5 pinned instruction at {Hex(rva)} is missing.");
        if (!string.Equals(instruction.Text, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"M5 pinned instruction at {Hex(rva)} changed: expected '{expected}', actual '{instruction.Text}'.");
        }

        return instruction;
    }

    private static string Hex(uint value) => $"0x{value:X8}";
}
