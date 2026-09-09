using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.OfflineClientReverseEngineering;

public sealed record M4EvidenceSource(string Kind, string Path, string Sha256, long Length);

public sealed record M4ClientFunctionEvidence(
    string Purpose,
    string Rva,
    string WindowSha256,
    string EvidenceStatus);

public sealed record M4TransportCipherEvidence(
    string InitialPreviousByte,
    string KeyTableSha256,
    int KeyTableLength,
    string OutboundCipherRva,
    string InboundCipherRva,
    string AlgorithmEvidenceStatus,
    int TargetFramesDecoded,
    int TargetChecksumsValid,
    bool KeyTableRetainedInArtifact);

public sealed record M4CapturedNpcApplicationObservation(
    ulong Sequence,
    string TimestampUtc,
    int TransportFrameLength,
    string TransportFrameSha256,
    string RecoveredApplicationOpcode,
    int ApplicationPayloadLength,
    int NpcEntityHandle,
    int? SecondaryOfficialIdentity,
    string ApplicationPayloadSha256,
    bool ChecksumValid,
    string StaticSenderRva,
    string ActionLabel,
    string ActionBoundaryConfidence,
    string SemanticStatus,
    bool RawPacketBodyRetained);

public sealed record M4DialogMessageObservation(
    int TransportOffset,
    string Opcode,
    int DeclaredApplicationLength,
    int MessageType,
    int Subtype,
    int NpcEntityHandle,
    int ClientLookupIdentity,
    int Field8,
    bool InlineTextPresent,
    int OpaqueLength,
    string OpaqueSha256,
    bool OpaqueAllZero,
    string EvidenceStatus);

public sealed record M4DialogResponseCorrelation(
    string TimestampUtc,
    int DecodedTransportLength,
    string Opcode,
    string ApplicationSha256,
    bool ChecksumValid,
    ulong? PriorInteractionSequence,
    long? LatencyMilliseconds,
    IReadOnlyList<M4DialogMessageObservation> Messages,
    string PairingStatus,
    bool RawPacketBodyRetained);

public sealed record M4MerchantStateResponseObservation(
    string TimestampUtc,
    int DecodedTransportLength,
    string Opcode,
    string ApplicationSha256,
    bool ChecksumValid,
    ulong? PriorTransactionSequence,
    long? LatencyMilliseconds,
    string StaticHandlerRva,
    string StaticConsumerRva,
    string SemanticStatus,
    bool RawPacketBodyRetained);

public sealed record M4EvidenceGate(string Gate, string Status, string EvidenceOrBlocker);

public sealed record M4NpcInteractionEvidenceSnapshot(
    string SchemaVersion,
    string Decision,
    string OfficialClientBuildId,
    string OfficialClientSha256,
    string RuntimeCodeSha256,
    IReadOnlyList<M4EvidenceSource> Sources,
    IReadOnlyList<M4ClientFunctionEvidence> ClientFunctions,
    M4TransportCipherEvidence TransportCipher,
    IReadOnlyList<M4CapturedNpcApplicationObservation> CapturedApplicationFrames,
    IReadOnlyList<M4DialogResponseCorrelation> DialogResponses,
    IReadOnlyList<M4MerchantStateResponseObservation> MerchantStateResponses,
    IReadOnlyList<M4EvidenceGate> Gates,
    string FirstBrokenNode,
    string FirstBrokenEdge,
    IReadOnlyList<string> ProvenFacts,
    IReadOnlyList<string> RemainingBlockers,
    bool ClientWriteAccessUsed,
    bool ClientBinaryModified,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

public sealed record M4DecodedTransportFrame(
    int DeclaredLength,
    byte ApplicationOpcode,
    byte[] ApplicationPayload,
    string ApplicationSha256,
    bool ChecksumValid);

/// <summary>
/// Reconstructs M4 contracts from an existing restricted capture. Output is deliberately
/// limited to hashes, lengths, opcodes and fields consumed by exact, build-pinned Client
/// branches. Neither the key table nor complete packet bodies are written to artifacts.
/// </summary>
public static partial class M4NpcInteractionEvidenceRecovery
{
    public const string SchemaVersion = "god2-roadmap-m4-npc-interaction-evidence-v2";
    public const string MerchantCaptureSession = "host-run-20260801-045830-開啟商店賣出寶石";

    private const uint RuntimeImageBase = 0x00400000;
    private const uint RuntimeSnapshotBaseRva = 0x00001000;
    private const int TraceHeaderLength = 24;
    private const int TraceRecordHeaderLength = 188;
    private const uint TraceRecordMagic = 0x31523247;
    private const ushort ClientToServerDirection = 1;
    private const ushort ServerToClientDirection = 2;
    private const int ExpectedNpcHandle = 1504;
    private const int ExpectedMerchantItemId = 3604;
    public const long ExpectedDialogPairingLatencyMilliseconds = 136;
    private const string ExpectedKeyTableSha256 = "50C1ACC6064E006C2C629BD88347E316EA00E854A4DD01901209D8DE9DD0D308";
    private const string ExpectedNpcPayloadSha256 = "612FAAF8854120C839BEB1A95BF215DACF5DADA73DFC7E2E8C309A18701532C7";
    private const string ExpectedMerchantPayloadSha256 = "A3F0BEADD89968B094EC0803D03C63E9E98403016F19635795E5E7773D705329";
    private const string ExpectedDialogApplicationSha256 = "18BD3B82419B71C43BD7B84AC32FBE0B84320BC8AF473759C1D838EDE807560E";

    public static M4NpcInteractionEvidenceSnapshot Analyze(string workspaceRoot, string runtimeCodePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCodePath);

        workspaceRoot = Path.GetFullPath(workspaceRoot);
        runtimeCodePath = Path.GetFullPath(runtimeCodePath);
        var runtimeCode = BoundedFile.ReadAllBytes(runtimeCodePath, 16L * 1024 * 1024, "Runtime code snapshot");
        RequireHash(runtimeCode, M2WorldContentEvidenceAnalyzer.RuntimeCodeSha256, "Runtime code snapshot");

        var analyzer = new RuntimeCodeAnalyzer(runtimeCodePath);
        VerifyStaticContract(analyzer, runtimeCode);

        var sessionRoot = Path.Combine(
            workspaceRoot,
            "Artifacts",
            "ClientInstrumentation",
            "ElevatedAutomationHost",
            MerchantCaptureSession);
        var attempt = Directory.EnumerateDirectories(sessionRoot, "attempt-*-trace", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Single();
        var tracePath = Path.Combine(attempt, "sensitive", "trace.bin");
        var generalLogPath = Path.Combine(attempt, "general.log");
        var transactionPath = Path.Combine(
            workspaceRoot,
            "protocol",
            "evidence",
            "current-build",
            "chinese-labeled-captures",
            "action-transactions.json");

        var traceBytes = BoundedFile.ReadAllBytes(tracePath, 4L * 1024 * 1024, "M4 restricted trace");
        var generalLogBytes = BoundedFile.ReadAllBytes(generalLogPath, 4L * 1024 * 1024, "M4 general log");
        var transactionBytes = BoundedFile.ReadAllBytes(transactionPath, 16L * 1024 * 1024, "M4 transaction metadata");
        var cipher = ReadCipherEvidence(generalLogBytes);
        RequireHash(cipher.KeyTable, ExpectedKeyTableSha256, "M4 exact-session cipher table");
        if (cipher.InitialPreviousByte != 0x10)
        {
            throw new InvalidDataException($"Expected M4 initial previous byte 0x10, found 0x{cipher.InitialPreviousByte:X2}.");
        }

        var actionMetadata = ReadActionMetadata(transactionBytes);
        var frames = ReadTraceFrames(traceBytes);
        var expectedOutbound = new Dictionary<ulong, (byte Opcode, int PayloadLength, int? SecondaryIdentity, string SenderRva)>
        {
            [5] = (0x37, 4, null, "0x000B0800"),
            [40] = (0x38, 8, ExpectedMerchantItemId, "0x000B0730"),
            [45] = (0x39, 4, null, "0x000B07D0"),
            [46] = (0x37, 4, null, "0x000B0800"),
            [70] = (0x39, 4, null, "0x000B07D0")
        };
        var interactions = frames
            .Where(frame => frame.Direction == ClientToServerDirection && expectedOutbound.ContainsKey(frame.Sequence))
            .Select(frame =>
            {
                var expected = expectedOutbound[frame.Sequence];
                var decoded = DecodeTransportFrameForEvidence(frame.Payload, cipher.KeyTable, cipher.InitialPreviousByte);
                if (decoded.ApplicationOpcode != expected.Opcode ||
                    decoded.ApplicationPayload.Length != expected.PayloadLength ||
                    !decoded.ChecksumValid)
                {
                    throw new InvalidDataException(
                        $"M4 C2S sequence {frame.Sequence} recovered opcode 0x{decoded.ApplicationOpcode:X2}, " +
                        $"payload length {decoded.ApplicationPayload.Length}, checksum {decoded.ChecksumValid}; " +
                        $"expected 0x{expected.Opcode:X2}/{expected.PayloadLength}/true.");
                }

                var handle = BinaryPrimitives.ReadUInt16LittleEndian(decoded.ApplicationPayload);
                var secondary = decoded.ApplicationPayload.Length >= 4
                    ? BinaryPrimitives.ReadUInt16LittleEndian(decoded.ApplicationPayload.AsSpan(2))
                    : 0;
                if (handle != ExpectedNpcHandle ||
                    (expected.SecondaryIdentity is not null && secondary != expected.SecondaryIdentity))
                {
                    throw new InvalidDataException($"M4 C2S sequence {frame.Sequence} identity fields changed.");
                }

                var expectedPayloadHash = expected.Opcode == 0x38
                    ? ExpectedMerchantPayloadSha256
                    : ExpectedNpcPayloadSha256;
                RequireHash(decoded.ApplicationPayload, expectedPayloadHash, $"M4 C2S 0x{expected.Opcode:X2} payload");
                var hasAction = actionMetadata.TryGetValue(frame.Sequence, out var action);
                return new M4CapturedNpcApplicationObservation(
                    frame.Sequence,
                    FormatTimestamp(frame.WallUnixMs),
                    frame.Payload.Length,
                    Hash(frame.Payload),
                    $"0x{decoded.ApplicationOpcode:X2}",
                    decoded.ApplicationPayload.Length,
                    handle,
                    expected.SecondaryIdentity,
                    Hash(decoded.ApplicationPayload),
                    decoded.ChecksumValid,
                    expected.SenderRva,
                    hasAction ? action.Label : "Unassigned",
                    hasAction ? action.Confidence : "None",
                    expected.Opcode switch
                    {
                        0x37 => "VerifiedNpcInteractionOpen",
                        0x38 => "VerifiedMerchantItemTransaction",
                        _ => "VerifiedMerchantClose"
                    },
                    RawPacketBodyRetained: false);
            })
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (interactions.Length != expectedOutbound.Count)
        {
            throw new InvalidDataException($"Expected {expectedOutbound.Count} M4 target C2S frames, found {interactions.Length}.");
        }

        var incoming = ReassembleTransportFrames(frames, ServerToClientDirection)
            .Select(frame => new DecodedTimedFrame(
                frame.Sequence,
                frame.WallUnixMs,
                DecodeTransportFrameForEvidence(frame.Payload, cipher.KeyTable, cipher.InitialPreviousByte)))
            .ToArray();
        var dialogResponses = ReadDialogResponses(incoming, interactions);
        if (dialogResponses.Count != 2 ||
            dialogResponses.Any(response => response.Messages.Count != 2 || !response.ChecksumValid) ||
            dialogResponses.Any(response =>
                response.PairingStatus != "VerifiedRepeatedTimingIdentityAndApplicationHash" ||
                response.LatencyMilliseconds != ExpectedDialogPairingLatencyMilliseconds) ||
            dialogResponses.Select(response => response.ApplicationSha256).Distinct(StringComparer.Ordinal).Single() != ExpectedDialogApplicationSha256)
        {
            throw new InvalidDataException("M4 no longer contains the two exact, checksum-valid S2C 0x7A dialog responses.");
        }

        var merchantResponses = ReadMerchantStateResponses(incoming, interactions);
        if (merchantResponses.Count != 1 || merchantResponses[0].PriorTransactionSequence != 40)
        {
            throw new InvalidDataException("M4 no longer contains the S2C 0x41 response paired to C2S sequence 40.");
        }

        var exhaustion = M4ExistingCaptureExhaustionRecovery.Analyze(workspaceRoot);
        var merchantCloseEvidence = exhaustion.CloseCandidates
            .Where(candidate => candidate.Opcode == "0x39")
            .ToArray();
        var quest086Evidence = exhaustion.CloseCandidates
            .Where(candidate => candidate.Opcode == "0x86")
            .ToArray();
        if (merchantCloseEvidence.Length != 2 ||
            merchantCloseEvidence.Any(candidate =>
                candidate.NpcHandleCandidate != ExpectedNpcHandle ||
                candidate.SecondaryU16Candidate != 0) ||
            quest086Evidence.Length != 2 ||
            quest086Evidence.Any(candidate =>
                candidate.NpcHandleCandidate != 83 ||
                candidate.SecondaryU16Candidate != 4609 ||
                candidate.ActionLabel != "QuestScriptedTransfer"))
        {
            throw new InvalidDataException("M4 existing-capture close-family identities no longer match the independently decoded evidence.");
        }

        var functions = new[]
        {
            Function("Captured C2S 0x37 sender stores the selected handle and enqueues four payload bytes", runtimeCode, 0x000B0800, 0x68, "VerifiedCaptured"),
            Function("Captured C2S 0x38 sender enqueues NPC handle plus selected item/service identity", runtimeCode, 0x000B0730, 0x70, "VerifiedCaptured"),
            Function("Captured C2S 0x39 sender enqueues Merchant NPC handle plus zero close state", runtimeCode, 0x000B07D0, 0x30, "VerifiedCapturedMerchantClose"),
            Function("World application enqueue appends opcode plus caller-supplied payload", runtimeCode, 0x0007FC10, 0x83, "Verified"),
            Function("Outbound transport applies the evidence-pinned cipher and checksum", runtimeCode, 0x00078F40, 0x9C, "Verified"),
            Function("Inbound transport reverses the evidence-pinned cipher and validates checksum", runtimeCode, 0x00078D70, 0x130, "Verified"),
            Function("S2C 0x7A dispatch calls the result-record consumer", runtimeCode, 0x0009068E, 0x19, "VerifiedCaptured"),
            Function("S2C 0x7A result consumer reaches the dialog UI initializer", runtimeCode, 0x000F60E0, 0x230, "VerifiedCaptured"),
            Function("Dialog UI initializer sets the feature-specific active state", runtimeCode, 0x000F5F80, 0xB0, "Verified"),
            Function("S2C 0x41 handler reaches the merchant-session inventory state consumer", runtimeCode, 0x0008FBC0, 0x180, "VerifiedCaptured"),
            Function("S2C 0x41 consumer mutates inventory slot/state structures", runtimeCode, 0x000C5B40, 0x360, "VerifiedCaptured"),
            Function("Dialog close candidate branch sends C2S 0x86 and clears the active UI state", runtimeCode, 0x000F4D50, 0xCA, "VerifiedStaticOnlyNotPaired")
        };

        var sources = new[]
        {
            Source(workspaceRoot, "ReadOnlyRuntimeCode", runtimeCodePath, runtimeCode),
            Source(workspaceRoot, "RestrictedExistingCapture", tracePath, traceBytes),
            Source(workspaceRoot, "SafeInstrumentationMetadata", generalLogPath, generalLogBytes),
            Source(workspaceRoot, "ExistingTransactionMetadata", transactionPath, transactionBytes)
        };

        return new M4NpcInteractionEvidenceSnapshot(
            SchemaVersion,
            Decision: "MERCHANT_FRONT_DOOR_OPEN_CLOSE_IMPLEMENTABLE_STANDALONE_DIALOG_BLOCKED",
            M2WorldContentEvidenceAnalyzer.OfficialClientBuildId,
            M2WorldContentEvidenceAnalyzer.OfficialClientSha256,
            M2WorldContentEvidenceAnalyzer.RuntimeCodeSha256,
            sources,
            functions,
            new M4TransportCipherEvidence(
                $"0x{cipher.InitialPreviousByte:X2}",
                Hash(cipher.KeyTable),
                cipher.KeyTable.Length,
                "0x00078F40",
                "0x00078D70",
                "VerifiedExactBuildAndExistingSession",
                interactions.Length + dialogResponses.Count + merchantResponses.Count,
                interactions.Count(item => item.ChecksumValid) + dialogResponses.Count(item => item.ChecksumValid) + merchantResponses.Count(item => item.ChecksumValid),
                KeyTableRetainedInArtifact: false),
            interactions,
            dialogResponses,
            merchantResponses,
            [
                new("Existing runtime target/eligibility/family router", "PASS", "WorldInteractionRuntime supplies MariaDB-session target resolution, ownership/map/state validation and family routing."),
                new("Official NPC interaction distance policy", "PASS", "Exact-build RVA 0x00089511-0x0008953F takes independent absolute X/Y deltas and suppresses C2S 0x37 when either exceeds three Client grid units."),
                new("C2S NPC interaction application branch", "PASS", "Exact-build RVA 0x000B0800 enqueues opcode 0x37 and four payload bytes after storing the selected handle."),
                new("Existing captured NPC interaction", "PASS", "Existing sequences 5 and 46 decrypt to checksum-valid 0x37 requests for handle 1504."),
                new("Existing-capture transport reconstruction", "PASS", "Exact Client cipher functions, logged initial byte and logged 256-byte table reproduce all eight target checksums; artifact retains only the table hash."),
                new("S2C dialog receive/UI branch", "PASS", "Both 0x37 requests are followed by the same checksum-valid 0x7A application response, and the exact receive branch reaches the dialog-active UI initializer."),
                new("NPC interaction C2S/S2C pairing", "PASS", $"Two independent 0x37(handle 1504) -> identical 0x7A observations pair at {ExpectedDialogPairingLatencyMilliseconds}ms with no competing target interaction."),
                new("NPC dialog-open serializer evidence", "PASS", "The complete 58-byte transport response is reconstructed; both variable records, required fields and zero opaque regions are hash-pinned without retaining packet bodies."),
                new("Merchant service front-door open/close", "PASS", "The existing ShopOpen/ShopSell session proves repeated 0x37(handle 1504) -> 0x7A service-state opens and repeated 0x39(handle 1504,state 0) close transitions. Production treats this as read-only Merchant service open/close; 0x38 -> 0x41 item mutation remains excluded."),
                new("Standalone Dialog close", "BLOCKED", "The static 0x86 state-clear candidate is not paired to the Merchant capture. Exhaustive decoding found 0x86 only in the QuestScriptedTransfer session with fields 83/4609 and a different caller; those frames cannot be promoted as an NPC-handle Dialog close contract."),
                new("Production malformed/incomplete/wrong-state/replay acceptance", "SEPARATE_RUNTIME_GATE", "Current-source execution results are recorded separately in Artifacts/RoadMap/M4/test-results.json; this read-only evidence artifact does not infer test outcomes.")
            ],
            FirstBrokenNode: "Standalone Official Dialog Profile / Close Contract",
            FirstBrokenEdge: "Non-Merchant C2S 0x37 -> target-specific S2C 0x7A -> uniquely paired close command",
            ProvenFacts:
            [
                "The prior EvidenceBlocked C2S conclusion was false: the existing session log contains the exact initial byte and 256-byte key table required to invert the captured transport frames.",
                "Existing sequences 5 and 46 are checksum-valid C2S 0x37 requests for Merchant NPC handle 1504; sequences 45 and 70 are checksum-valid C2S 0x39 close requests carrying the same handle and zero state.",
                "Exhaustion of the 15 existing action-bearing captures found two checksum-valid 0x86 frames only in QuestScriptedTransfer; their 83/4609 fields and different caller prohibit reusing them as an NPC-handle Dialog close.",
                "Existing sequence 40 is a checksum-valid C2S 0x38 request for NPC handle 1504 and official item identity 3604; its S2C 0x41 response reaches the inventory-state consumer.",
                $"Each C2S 0x37 request is followed {ExpectedDialogPairingLatencyMilliseconds}ms later by the same checksum-valid 58-byte S2C 0x7A application response.",
                "Each 0x7A response contains two records referencing handle 1504; all remaining bytes are zero opaque regions and are independently hash-pinned.",
                "The exact 0x7A consumer calls RVA 0x000F5F80, whose UI initializer sets the dialog-active state at RVA 0x000F6018.",
                "The exact world-selection branch authorizes C2S 0x37 only when absolute X and Y deltas are each at most three; Production implements that policy without a derived radius.",
                "No new capture, Client write, packet replay, network emission or fake network bytes were used."
            ],
            RemainingBlockers:
            [
                "Recover a target-specific non-Merchant Dialog 0x7A serializer profile and uniquely pair its close branch; do not reuse the QuestScriptedTransfer 0x86 observations.",
                "Promote ordinary walking into authoritative Runtime X/Y only after its separately scoped evidence contract is complete; do not trust connection-local movement candidates for range eligibility.",
                "Run automated official Client feature-specific acceptance for the Merchant service front-door open/close and the later standalone Dialog slice."
            ],
            ClientWriteAccessUsed: false,
            ClientBinaryModified: false,
            NetworkBytesEmitted: false,
            FakeNetworkBytes: false);
    }

    public static M4DecodedTransportFrame DecodeTransportFrameForEvidence(
        ReadOnlySpan<byte> transportFrame,
        ReadOnlySpan<byte> keyTable,
        byte initialPreviousByte)
    {
        if (transportFrame.Length < 4 ||
            BinaryPrimitives.ReadUInt16LittleEndian(transportFrame) != transportFrame.Length)
        {
            throw new InvalidDataException("M4 transport frame length is invalid.");
        }

        if (keyTable.Length != 256)
        {
            throw new InvalidDataException("M4 transport key table must contain exactly 256 bytes.");
        }

        var decoded = transportFrame.ToArray();
        try
        {
            var previous = initialPreviousByte;
            for (var index = 2; index < decoded.Length; index++)
            {
                var feedback = unchecked((byte)(previous - 3));
                var adjusted = unchecked((byte)(transportFrame[index] - feedback));
                // The evidence-pinned Client decoder executes `movzx eax, al` before the
                // key lookup at RVA 0x00078DBA-0x00078DC0. Consequently the loop counter
                // is reduced to its low byte and the 256-byte table repeats for long frames.
                decoded[index] = (byte)(keyTable[(index - 2) & 0xFF] ^ adjusted);
                previous = decoded[index];
            }

            var checksum = 0;
            for (var index = 0; index < decoded.Length - 1; index++)
            {
                checksum = unchecked((byte)(checksum + unchecked((byte)(decoded[index] + 0x3C))));
            }

            var application = decoded.AsSpan(2, decoded.Length - 3);
            return new M4DecodedTransportFrame(
                decoded.Length,
                application[0],
                application[1..].ToArray(),
                Hash(application),
                (byte)checksum == decoded[^1]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static void VerifyStaticContract(RuntimeCodeAnalyzer analyzer, ReadOnlySpan<byte> runtimeCode)
    {
        RequireInstruction(analyzer, 0x000B0844, "push 4");
        RequireInstruction(analyzer, 0x000B0847, "push 37h");
        RequireCall(analyzer, 0x000B084F, RuntimeImageBase + M4NpcInteractionStaticRecovery.OutboundEnqueueRva);
        RequireInstruction(analyzer, 0x000B0794, "push 8");
        RequireInstruction(analyzer, 0x000B0797, "push 38h");
        RequireCall(analyzer, 0x000B0799, RuntimeImageBase + M4NpcInteractionStaticRecovery.OutboundEnqueueRva);
        RequireInstruction(analyzer, 0x000B07EC, "push 4");
        RequireInstruction(analyzer, 0x000B07EF, "push 39h");
        RequireCall(analyzer, 0x000B07F1, RuntimeImageBase + M4NpcInteractionStaticRecovery.OutboundEnqueueRva);
        RequireInstruction(analyzer, 0x00089517, "sub eax,[edi+224h]");
        RequireInstruction(analyzer, 0x00089522, "cmp eax,3");
        RequireInstruction(analyzer, 0x00089531, "sub eax,[edi+228h]");
        RequireInstruction(analyzer, 0x0008953C, "cmp eax,3");
        RequireInstruction(analyzer, 0x00089541, "cmp dword [ebp-128h],37h");
        RequireCall(analyzer, 0x000895BA, RuntimeImageBase + 0x000B0800);
        RequireInstruction(analyzer, 0x0008E536, "jmp dword [esi*4+4911A8h]");
        var dispatchTarget = BinaryPrimitives.ReadUInt32LittleEndian(Slice(runtimeCode, 0x000911A8 + (0x7A * 4), sizeof(uint)));
        if (dispatchTarget != RuntimeImageBase + 0x0009068E)
        {
            throw new InvalidDataException("S2C 0x7A no longer dispatches to the evidence-pinned dialog branch.");
        }

        RequireInstruction(analyzer, 0x0009068E, "mov ax,[edi+9]");
        RequireCall(analyzer, 0x000906A2, RuntimeImageBase + 0x000F60E0);
        RequireCall(analyzer, 0x000F6306, RuntimeImageBase + 0x000F5F80);
        RequireInstruction(analyzer, 0x000F6018, "mov dword [esi+4EF54h],1");
        RequireCall(analyzer, 0x0008FBD9, RuntimeImageBase + 0x000C5B40);
        RequireInstruction(analyzer, 0x000F4D65, "cmp dword [esi+4EF54h],0");
        RequireInstruction(analyzer, 0x000F4D7D, "push 4");
        RequireInstruction(analyzer, 0x000F4D98, "push 86h");
        RequireCall(analyzer, 0x000F4D9D, RuntimeImageBase + M4NpcInteractionStaticRecovery.OutboundEnqueueRva);
        RequireInstruction(analyzer, 0x000F4DA2, "mov dword [esi+4EF54h],0");
    }

    private static CipherMaterial ReadCipherEvidence(byte[] generalLog)
    {
        var text = new UTF8Encoding(false, true).GetString(generalLog);
        var match = CipherEntryRegex().Match(text);
        if (!match.Success)
        {
            throw new InvalidDataException("M4 exact-session cipher material is absent from the existing metadata log.");
        }

        var key = Convert.FromHexString(match.Groups["key"].Value.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (key.Length != 256)
        {
            throw new InvalidDataException($"M4 exact-session key table length is {key.Length}, expected 256.");
        }

        return new CipherMaterial(
            byte.Parse(match.Groups["initial"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            key);
    }

    private static IReadOnlyDictionary<ulong, (string Label, string Confidence)> ReadActionMetadata(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<ulong, (string, string)>();
        foreach (var transaction in document.RootElement.EnumerateArray())
        {
            if (!transaction.GetProperty("sessionId").GetString()!.Equals(MerchantCaptureSession, StringComparison.Ordinal) ||
                !transaction.TryGetProperty("triggerOutbound", out var trigger) ||
                trigger.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var sequence = transaction.GetProperty("startSequence").GetUInt64();
            result[sequence] = (
                transaction.GetProperty("labelCandidate").GetString()!,
                transaction.GetProperty("confidence").GetString()!);
        }

        return result;
    }

    private static IReadOnlyList<M4DialogResponseCorrelation> ReadDialogResponses(
        IReadOnlyList<DecodedTimedFrame> incoming,
        IReadOnlyList<M4CapturedNpcApplicationObservation> interactions)
    {
        var opens = interactions.Where(item => item.RecoveredApplicationOpcode == "0x37").ToArray();
        return incoming
            .Where(frame => frame.Decoded.ApplicationOpcode == 0x7A)
            .Select(frame =>
            {
                var prior = FindPrior(frame.WallUnixMs, opens);
                var decodedFrame = FindAndDecodeOriginalNotRetained(frame, incoming);
                var messages = ParseDialogMessages(decodedFrame);
                return new M4DialogResponseCorrelation(
                    FormatTimestamp(frame.WallUnixMs),
                    frame.Decoded.DeclaredLength,
                    "0x7A",
                    frame.Decoded.ApplicationSha256,
                    frame.Decoded.ChecksumValid,
                    prior.Observation?.Sequence,
                    prior.Latency,
                    messages,
                    ClassifyDialogPairingForEvidence(prior.Observation is not null, prior.Latency),
                    RawPacketBodyRetained: false);
            })
            .ToArray();
    }

    public static string ClassifyDialogPairingForEvidence(bool priorInteractionFound, long? latencyMilliseconds) =>
        priorInteractionFound && latencyMilliseconds == ExpectedDialogPairingLatencyMilliseconds
            ? "VerifiedRepeatedTimingIdentityAndApplicationHash"
            : "Unpaired";

    private static IReadOnlyList<M4DialogMessageObservation> ParseDialogMessages(byte[] decodedApplication)
    {
        var messages = new List<M4DialogMessageObservation>();
        var offset = 0;
        while (offset < decodedApplication.Length)
        {
            if (decodedApplication.Length - offset < 12 || decodedApplication[offset] != 0x7A)
            {
                throw new InvalidDataException("M4 0x7A response contains an invalid variable-record boundary.");
            }

            var declaredLength = decodedApplication[offset + 1];
            if (declaredLength < 12 || offset > decodedApplication.Length - declaredLength)
            {
                throw new InvalidDataException("M4 0x7A response contains a truncated variable record.");
            }

            var record = decodedApplication.AsSpan(offset, declaredLength);
            var opaque = record[12..];
            messages.Add(new M4DialogMessageObservation(
                offset + 2,
                "0x7A",
                declaredLength,
                record[3],
                record[4],
                BinaryPrimitives.ReadUInt16LittleEndian(record[5..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[7..]),
                BinaryPrimitives.ReadUInt16LittleEndian(record[9..]),
                record[11] != 0,
                opaque.Length,
                Hash(opaque),
                opaque.IndexOfAnyExcept((byte)0) < 0,
                "VerifiedCompleteRecord"));
            offset += declaredLength;
        }

        return messages;
    }

    private static IReadOnlyList<M4MerchantStateResponseObservation> ReadMerchantStateResponses(
        IReadOnlyList<DecodedTimedFrame> incoming,
        IReadOnlyList<M4CapturedNpcApplicationObservation> interactions)
    {
        var transactions = interactions.Where(item => item.RecoveredApplicationOpcode == "0x38").ToArray();
        return incoming
            .Where(frame => frame.Decoded.ApplicationOpcode == 0x41)
            .Select(frame =>
            {
                var prior = FindPrior(frame.WallUnixMs, transactions);
                return new M4MerchantStateResponseObservation(
                    FormatTimestamp(frame.WallUnixMs),
                    frame.Decoded.DeclaredLength,
                    "0x41",
                    frame.Decoded.ApplicationSha256,
                    frame.Decoded.ChecksumValid,
                    prior.Observation?.Sequence,
                    prior.Latency,
                    "0x0008FBC0",
                    "0x000C5B40",
                    prior.Observation is not null && prior.Latency is >= 0 and <= 500
                        ? "VerifiedInventoryStateMutationNotMerchantOpen"
                        : "Unpaired",
                    RawPacketBodyRetained: false);
            })
            .Where(item => item.PriorTransactionSequence is not null)
            .ToArray();
    }

    private static byte[] FindAndDecodeOriginalNotRetained(
        DecodedTimedFrame selected,
        IReadOnlyList<DecodedTimedFrame> all)
    {
        _ = all;
        var application = new byte[selected.Decoded.ApplicationPayload.Length + 1];
        application[0] = selected.Decoded.ApplicationOpcode;
        selected.Decoded.ApplicationPayload.CopyTo(application, 1);
        return application;
    }

    private static (M4CapturedNpcApplicationObservation? Observation, long? Latency) FindPrior(
        long timestamp,
        IReadOnlyList<M4CapturedNpcApplicationObservation> candidates)
    {
        var prior = candidates
            .Select(item => (Observation: item, Timestamp: DateTimeOffset.Parse(item.TimestampUtc, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds()))
            .Where(item => item.Timestamp <= timestamp)
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();
        if (prior.Observation is null)
        {
            return (null, null);
        }

        var latency = timestamp - prior.Timestamp;
        return latency > 3_000 ? (null, null) : (prior.Observation, latency);
    }

    private static IReadOnlyList<ReassembledFrame> ReassembleTransportFrames(
        IReadOnlyList<TraceFrame> records,
        ushort direction)
    {
        var pending = new List<byte>();
        var frames = new List<ReassembledFrame>();
        ulong startSequence = 0;
        long startTimestamp = 0;
        foreach (var record in records.Where(item => item.Direction == direction).OrderBy(item => item.Sequence))
        {
            if (pending.Count == 0)
            {
                startSequence = record.Sequence;
                startTimestamp = record.WallUnixMs;
            }

            pending.AddRange(record.Payload);
            while (pending.Count >= 2)
            {
                var declared = pending[0] | (pending[1] << 8);
                if (declared is < 4 or > 4096)
                {
                    throw new InvalidDataException($"M4 S2C stream has invalid frame length {declared} at trace sequence {startSequence}.");
                }

                if (pending.Count < declared)
                {
                    break;
                }

                frames.Add(new ReassembledFrame(startSequence, startTimestamp, pending.Take(declared).ToArray()));
                pending.RemoveRange(0, declared);
                if (pending.Count > 0)
                {
                    startSequence = record.Sequence;
                    startTimestamp = record.WallUnixMs;
                }
            }
        }

        if (pending.Count != 0)
        {
            throw new InvalidDataException($"M4 S2C stream ends with {pending.Count} incomplete bytes.");
        }

        return frames;
    }

    private static IReadOnlyList<TraceFrame> ReadTraceFrames(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < TraceHeaderLength ||
            !bytes[..7].SequenceEqual("G2TRC01"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != 1)
        {
            throw new InvalidDataException("Invalid M4 restricted trace header.");
        }

        var records = new List<TraceFrame>();
        var offset = TraceHeaderLength;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < TraceRecordHeaderLength)
            {
                throw new InvalidDataException("Truncated M4 trace record header.");
            }

            var header = bytes.Slice(offset, TraceRecordHeaderLength);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != TraceRecordMagic ||
                BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != TraceRecordHeaderLength)
            {
                throw new InvalidDataException("Invalid M4 trace record identity.");
            }

            var capturedLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[64..]));
            if (capturedLength is < 0 or > 4096 || bytes.Length - offset - TraceRecordHeaderLength < capturedLength)
            {
                throw new InvalidDataException("Invalid M4 trace payload length.");
            }

            records.Add(new TraceFrame(
                BinaryPrimitives.ReadUInt64LittleEndian(header[8..]),
                BinaryPrimitives.ReadInt64LittleEndian(header[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[42..]),
                bytes.Slice(offset + TraceRecordHeaderLength, capturedLength).ToArray()));
            offset += TraceRecordHeaderLength + capturedLength;
        }

        return records;
    }

    private static void RequireInstruction(RuntimeCodeAnalyzer analyzer, uint rva, string expected)
    {
        var instruction = analyzer.DecodeRange(rva, 16).FirstOrDefault(item => item.Rva == rva);
        if (instruction is null || !instruction.Text.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"M4 Client instruction 0x{rva:X8} no longer matches '{expected}'.");
        }
    }

    private static void RequireCall(RuntimeCodeAnalyzer analyzer, uint rva, uint target)
    {
        var instruction = analyzer.DecodeRange(rva, 8).FirstOrDefault(item => item.Rva == rva);
        if (instruction is null || instruction.DirectTarget != target)
        {
            throw new InvalidDataException($"M4 Client call 0x{rva:X8} no longer targets 0x{target:X8}.");
        }
    }

    private static M4ClientFunctionEvidence Function(
        string purpose,
        ReadOnlySpan<byte> runtimeCode,
        int rva,
        int length,
        string status) =>
        new(purpose, $"0x{rva:X8}", Hash(Slice(runtimeCode, rva, length)), status);

    private static M4EvidenceSource Source(string root, string kind, string path, ReadOnlySpan<byte> bytes) =>
        new(kind, Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(bytes), bytes.Length);

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> runtimeCode, int rva, int length)
    {
        var offset = checked(rva - (int)RuntimeSnapshotBaseRva);
        if (offset < 0 || length < 0 || offset > runtimeCode.Length - length)
        {
            throw new InvalidDataException($"M4 evidence window 0x{rva:X8} is outside the runtime snapshot.");
        }

        return runtimeCode.Slice(offset, length);
    }

    private static void RequireHash(ReadOnlySpan<byte> bytes, string expected, string purpose)
    {
        var actual = Hash(bytes);
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{purpose} SHA-256 {actual} does not match the evidence pin.");
        }
    }

    private static string FormatTimestamp(long unixMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [GeneratedRegex(
        "packet-decode entry sequence=1 .*?initialKey=0x(?<initial>[0-9A-F]{2}).*?key256=\\\"(?<key>(?:[0-9A-F]{2} ?){256})\\\"",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CipherEntryRegex();

    private sealed record CipherMaterial(byte InitialPreviousByte, byte[] KeyTable);
    private sealed record TraceFrame(ulong Sequence, long WallUnixMs, ushort Direction, byte[] Payload);
    private sealed record ReassembledFrame(ulong Sequence, long WallUnixMs, byte[] Payload);
    private sealed record DecodedTimedFrame(ulong Sequence, long WallUnixMs, M4DecodedTransportFrame Decoded);
}
