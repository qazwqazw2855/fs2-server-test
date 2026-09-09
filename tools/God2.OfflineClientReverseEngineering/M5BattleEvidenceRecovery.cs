using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace God2.OfflineClientReverseEngineering;

public sealed record M5BattleOpcodeShape(
    string Direction,
    string Opcode,
    int Count,
    IReadOnlyList<int> TransportLengths,
    IReadOnlyList<int> ApplicationPayloadLengths,
    int UniqueApplicationHashes,
    bool AllChecksumsValid,
    bool RawPacketBodiesRetained);

public sealed record M5BattleActionWindow(
    string Label,
    string Confidence,
    ulong StartSequence,
    ulong EndSequence,
    string? ClientOpcode,
    int? ClientTransportLength,
    int? ClientApplicationPayloadLength,
    string? ClientApplicationSha256,
    string? CallerRva,
    M5BattleCommand35Candidate? Command35,
    IReadOnlyList<string> ServerOpcodeSequence,
    IReadOnlyList<string> ServerApplicationHashes,
    bool RawPacketBodiesRetained);

public sealed record M5BattleCommand35Candidate(
    int PositionIndexCandidate,
    int ActionCodeCandidate,
    bool Continuation,
    int SideFlagCandidate,
    int ReservedByteCandidate,
    ushort TargetMaskGroup0,
    ushort TargetMaskGroup1,
    ushort TargetMaskGroup2,
    ushort BattleContextCandidate,
    uint ActionParameterCandidate,
    string StaticBuilderEvidence,
    string SemanticStatus);

public sealed record M5BattleCaptureSession(
    string SessionId,
    string Alias,
    string CaptureBuildIdentityStatus,
    string TraceSha256,
    string GeneralLogSha256,
    string CipherTableSha256,
    int CipherEpochCount,
    int ClientToServerFrames,
    int ServerToClientFrames,
    int ChecksumFailures,
    IReadOnlyList<M5BattleOpcodeShape> OpcodeShapes,
    IReadOnlyList<M5BattleActionWindow> ActionWindows,
    bool CipherMaterialRetained,
    bool RawPacketBodiesRetained);

public sealed record M5BattleEvidenceSnapshot(
    string SchemaVersion,
    string Status,
    string OfficialClientBuildId,
    string OfficialClientSha256,
    bool CaptureTimeBuildAttested,
    IReadOnlyList<M5BattleCaptureSession> Sessions,
    IReadOnlyDictionary<string, int> ClientOpcodeCounts,
    IReadOnlyDictionary<string, int> ServerOpcodeCounts,
    int ActionWindowCount,
    int ChecksumFailureCount,
    string FirstBrokenNode,
    string FirstBrokenEdge,
    IReadOnlyList<string> ProvenFacts,
    IReadOnlyList<string> RemainingBlockers,
    bool CipherMaterialRetained,
    bool RawPacketBodiesRetained,
    bool ClientWriteAccessUsed,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

/// <summary>
/// Replays the already-present labelled Battle captures through their exact-session world
/// transport decoder. Output is deliberately limited to opcode/length/hash/timing metadata;
/// cipher material and unknown packet bodies are cleared and never serialized.
/// </summary>
public static class M5BattleEvidenceRecovery
{
    public const string OfficialClientBuildId = "god2-opt-6b127086e0c0";

    private const int TraceHeaderLength = 24;
    private const int TraceRecordHeaderLength = 188;
    private const uint TraceRecordMagic = 0x31523247;
    private const ushort ClientToServerDirection = 1;
    private const ushort ServerToClientDirection = 2;

    private static readonly IReadOnlyDictionary<string, string> BattleAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["戰鬥普通攻擊+對局完角色升級"] = "battle-basic-attack-level-up",
            ["任務戰鬥+謀士技能擺陣+換位+防禦+被敵方技能攻擊"] = "quest-battle-strategist-formation-defend",
            ["野外踩明雷觸發戰鬥+普通攻擊+戰鬥後結算獲得經驗+道具"] = "wild-encounter-basic-attack-settlement-rewards",
            ["劍客戰鬥施放單體技能+戰鬥結束結算經驗獲得物品+角色升級"] = "swordsman-skill-settlement-level-up",
            ["仙道職業戰鬥施放技能"] = "taoist-battle-skill",
            ["戰鬥逃跑成功+戰透逃跑失敗+戰鬥中死亡+坐騎忠誠降低+坐騎忠誠太低無法乘坐"] = "battle-flee-death-mount-loyalty"
        };

    public static M5BattleEvidenceSnapshot Analyze(string workspaceRoot, string officialClientSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(officialClientSha256);
        M5BattleStaticRecovery.ValidateBuildIdentity(
            M5BattleStaticRecovery.ExpectedRuntimeCodeSha256,
            officialClientSha256);
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        var captureRoot = Path.Combine(workspaceRoot, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost");
        var actionPath = Path.Combine(
            workspaceRoot,
            "protocol",
            "evidence",
            "current-build",
            "chinese-labeled-captures",
            "action-transactions.json");
        var actions = ReadActions(actionPath);
        var sessions = new List<M5BattleCaptureSession>();
        var clientCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var serverCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var pair in BattleAliases)
        {
            var sessionDirectory = Directory.EnumerateDirectories(captureRoot, $"host-run-*-{pair.Key}", SearchOption.TopDirectoryOnly)
                .SingleOrDefault() ?? throw new InvalidDataException($"M5 labelled capture is missing: {pair.Value}.");
            var attempts = Directory.EnumerateDirectories(sessionDirectory, "attempt-*-trace", SearchOption.TopDirectoryOnly)
                .Where(path => File.Exists(Path.Combine(path, "sensitive", "trace.bin")) && File.Exists(Path.Combine(path, "general.log")))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (attempts.Length != 1)
            {
                throw new InvalidDataException($"M5 capture {pair.Value} has {attempts.Length} usable attempts; one is required.");
            }

            var attempt = attempts[0];
            var sessionId = Path.GetFileName(sessionDirectory);
            var trace = BoundedFile.ReadAllBytes(Path.Combine(attempt, "sensitive", "trace.bin"), 16L * 1024 * 1024, "M5 battle trace");
            var log = BoundedFile.ReadAllBytes(Path.Combine(attempt, "general.log"), 16L * 1024 * 1024, "M5 battle log");
            CipherMaterial? cipher = null;
            IReadOnlyList<TraceRecord> records = [];
            IReadOnlyList<TimedDecodedFrame> c2s = [];
            IReadOnlyList<TimedDecodedFrame> s2c = [];
            try
            {
                cipher = ReadCipher(log, pair.Value);
                records = ReadTraceRecords(trace);
                c2s = Decode(records, ClientToServerDirection, cipher);
                s2c = Decode(records, ServerToClientDirection, cipher);
                Count(c2s, clientCounts);
                Count(s2c, serverCounts);
                var shapes = BuildShapes(c2s, s2c);
                var windows = BuildActionWindows(actions.GetValueOrDefault(sessionId) ?? [], c2s, s2c);
                var failures = c2s.Count(frame => !frame.Decoded.ChecksumValid) + s2c.Count(frame => !frame.Decoded.ChecksumValid);
                sessions.Add(new M5BattleCaptureSession(
                    sessionId,
                    pair.Value,
                    "CaptureTimeExecutableHashNotAttested",
                    Hash(trace),
                    Hash(log),
                    Hash(cipher.KeyTable),
                    cipher.EpochCount,
                    c2s.Count,
                    s2c.Count,
                    failures,
                    shapes,
                    windows,
                    CipherMaterialRetained: false,
                    RawPacketBodiesRetained: false));
            }
            finally
            {
                foreach (var frame in c2s.Concat(s2c))
                {
                    CryptographicOperations.ZeroMemory(frame.Decoded.ApplicationPayload);
                }

                foreach (var record in records)
                {
                    CryptographicOperations.ZeroMemory(record.Payload);
                }

                if (cipher is not null)
                {
                    CryptographicOperations.ZeroMemory(cipher.KeyTable);
                }

                CryptographicOperations.ZeroMemory(trace);
                CryptographicOperations.ZeroMemory(log);
            }
        }

        var checksumFailures = sessions.Sum(session => session.ChecksumFailures);
        var actionWindowCount = sessions.Sum(session => session.ActionWindows.Count);
        var basicAttackOpcodes = sessions.SelectMany(session => session.ActionWindows)
            .Where(window => window.Label == "BattleBasicAttack" && window.ClientOpcode is not null)
            .Select(window => window.ClientOpcode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var skillOpcodes = sessions.SelectMany(session => session.ActionWindows)
            .Where(window => window.Label is "SwordsmanSingleTargetSkill" or "TaoistBattleSkill" && window.ClientOpcode is not null)
            .Select(window => window.ClientOpcode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new M5BattleEvidenceSnapshot(
            "god2-roadmap-m5-existing-battle-evidence-v2",
            checksumFailures == 0
                ? "PASS_EXISTING_CAPTURE_TRANSPORT_EXHAUSTED_BUILD_IDENTITY_DERIVED"
                : "BLOCKED_BY_CHECKSUM_FAILURE",
            OfficialClientBuildId,
            officialClientSha256,
            CaptureTimeBuildAttested: false,
            sessions,
            clientCounts,
            serverCounts,
            actionWindowCount,
            checksumFailures,
            "Battle/Skill required-field semantic mapping",
            "checksum-valid application families -> actor/action/target/skill/result field layout",
            [
                $"Six already-present labelled Battle sessions decode with {checksumFailures} checksum failures.",
                $"BattleBasicAttack windows correlate to application opcodes: {FormatSet(basicAttackOpcodes)}.",
                $"Swordsman/Taoist skill windows correlate to application opcodes: {FormatSet(skillOpcodes)}.",
                "Every artifact observation retains only build identity, opcode, length, application hash, timing and supervised action metadata."
            ],
            [
                "Static send-branch reconstruction must uniquely map actor, action, target, skill, round and command-window fields before production mutation is enabled.",
                "S2C battle bootstrap/effect/result field layouts and ordered Client state transitions must be uniquely reconstructed before a serializer is enabled.",
                "A unique MariaDB Monster spawn with evidence-backed HP/MP/stat/AI/formation/reward policy is still required for the first production encounter.",
                "The existing Battle captures have module/RVA provenance but no capture-time executable SHA-256 attestation; their build association remains Derived."
            ],
            CipherMaterialRetained: false,
            RawPacketBodiesRetained: false,
            ClientWriteAccessUsed: false,
            NetworkBytesEmitted: false,
            FakeNetworkBytes: false);
    }

    private static IReadOnlyList<M5BattleActionWindow> BuildActionWindows(
        IReadOnlyList<ActionMetadata> actions,
        IReadOnlyList<TimedDecodedFrame> c2s,
        IReadOnlyList<TimedDecodedFrame> s2c) =>
        actions.Select(action =>
        {
            var outbound = c2s.FirstOrDefault(frame => frame.Sequence == action.StartSequence);
            var inbound = s2c
                .Where(frame => frame.Sequence >= action.StartSequence && frame.Sequence <= action.EndSequence && frame.Decoded.ChecksumValid)
                .OrderBy(frame => frame.WallUnixMs)
                .ThenBy(frame => frame.Sequence)
                .ToArray();
            return new M5BattleActionWindow(
                action.Label,
                action.Confidence,
                action.StartSequence,
                action.EndSequence,
                outbound is null ? null : Opcode(outbound),
                outbound?.Decoded.DeclaredLength,
                outbound?.Decoded.ApplicationPayload.Length,
                outbound?.Decoded.ApplicationSha256,
                action.CallerRva,
                ParseCommand35ForEvidence(outbound?.Decoded.ApplicationOpcode, outbound?.Decoded.ApplicationPayload),
                inbound.Select(Opcode).ToArray(),
                inbound.Select(frame => frame.Decoded.ApplicationSha256).ToArray(),
                RawPacketBodiesRetained: false);
        }).ToArray();

    public static M5BattleCommand35Candidate? ParseCommand35ForEvidence(
        byte? applicationOpcode,
        byte[]? applicationPayload)
    {
        if (applicationOpcode != 0x35 || applicationPayload is null || applicationPayload.Length != 16)
        {
            return null;
        }

        var payload = applicationPayload;
        return new M5BattleCommand35Candidate(
            payload[0],
            payload[1] & 0x7F,
            (payload[1] & 0x80) != 0,
            payload[2],
            payload[3],
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(10)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(12)),
            "God2_opt+rva-0x0014E7D0..0x0014EB3C",
            "StaticFieldLayoutVerified_ActionMeaningRequiresDifferentialCorrelation");
    }

    private static IReadOnlyList<M5BattleOpcodeShape> BuildShapes(
        IReadOnlyList<TimedDecodedFrame> c2s,
        IReadOnlyList<TimedDecodedFrame> s2c) =>
        c2s.Select(frame => (Direction: "C2S", Frame: frame))
            .Concat(s2c.Select(frame => (Direction: "S2C", Frame: frame)))
            .GroupBy(item => (item.Direction, Opcode: Opcode(item.Frame)))
            .OrderBy(group => group.Key.Direction, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Opcode, StringComparer.Ordinal)
            .Select(group => new M5BattleOpcodeShape(
                group.Key.Direction,
                group.Key.Opcode,
                group.Count(),
                group.Select(item => item.Frame.Decoded.DeclaredLength).Distinct().Order().ToArray(),
                group.Select(item => item.Frame.Decoded.ApplicationPayload.Length).Distinct().Order().ToArray(),
                group.Select(item => item.Frame.Decoded.ApplicationSha256).Distinct(StringComparer.Ordinal).Count(),
                group.All(item => item.Frame.Decoded.ChecksumValid),
                RawPacketBodiesRetained: false))
            .ToArray();

    private static IReadOnlyDictionary<string, IReadOnlyList<ActionMetadata>> ReadActions(string path)
    {
        using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 16L * 1024 * 1024, "M5 action metadata"));
        return document.RootElement.EnumerateArray()
            .Where(item => BattleAliases.Keys.Any(label => item.GetProperty("sessionId").GetString()!.EndsWith(label, StringComparison.Ordinal)))
            .Select(item => new ActionMetadata(
                item.GetProperty("sessionId").GetString()!,
                item.GetProperty("labelCandidate").GetString()!,
                item.GetProperty("confidence").GetString()!,
                item.GetProperty("startSequence").GetUInt64(),
                item.GetProperty("endSequence").GetUInt64(),
                item.TryGetProperty("triggerOutbound", out var trigger) && trigger.ValueKind == JsonValueKind.Object &&
                trigger.TryGetProperty("callerRva", out var caller)
                    ? caller.GetString()
                    : null))
            .GroupBy(item => item.SessionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ActionMetadata>)group.OrderBy(item => item.StartSequence).ToArray(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<TimedDecodedFrame> Decode(
        IReadOnlyList<TraceRecord> records,
        ushort direction,
        CipherMaterial cipher)
    {
        var frames = Reassemble(records, direction);
        var decodedFrames = new List<TimedDecodedFrame>(frames.Count);
        try
        {
            foreach (var frame in frames)
            {
                decodedFrames.Add(new TimedDecodedFrame(
                    frame.Sequence,
                    frame.WallUnixMs,
                    M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(
                        frame.Payload,
                        cipher.KeyTable,
                        cipher.InitialPreviousByte)));
            }

            return decodedFrames;
        }
        catch
        {
            foreach (var decodedFrame in decodedFrames)
            {
                CryptographicOperations.ZeroMemory(decodedFrame.Decoded.ApplicationPayload);
            }

            throw;
        }
        finally
        {
            foreach (var frame in frames)
            {
                CryptographicOperations.ZeroMemory(frame.Payload);
            }
        }
    }

    private static void Count(IEnumerable<TimedDecodedFrame> frames, IDictionary<string, int> counts)
    {
        foreach (var frame in frames.Where(frame => frame.Decoded.ChecksumValid))
        {
            var opcode = Opcode(frame);
            counts.TryGetValue(opcode, out var count);
            counts[opcode] = count + 1;
        }
    }

    private static CipherMaterial ReadCipher(ReadOnlySpan<byte> log, string sourceIdentity)
    {
        byte[]? selectedKey = null;
        byte selectedInitial = 0;
        var epochs = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        try
        {
            while (offset < log.Length)
            {
                var remaining = log[offset..];
                var newline = remaining.IndexOf((byte)'\n');
                var lineLength = newline < 0 ? remaining.Length : newline;
                var line = remaining[..lineLength].TrimEnd((byte)'\r');
                if (TryReadCipherLine(line, out var initial, out var key, out var isExactSessionEntry))
                {
                    try
                    {
                        epochs.Add($"{initial:X2}:{Hash(key)}");
                        if (isExactSessionEntry)
                        {
                            if (selectedKey is null)
                            {
                                selectedInitial = initial;
                                selectedKey = key;
                                key = null;
                            }
                            else if (selectedInitial != initial ||
                                     !CryptographicOperations.FixedTimeEquals(selectedKey, key))
                            {
                                throw new InvalidDataException(
                                    $"M5 exact-session cipher material is ambiguous for {sourceIdentity}.");
                            }
                        }
                    }
                    finally
                    {
                        if (key is not null)
                        {
                            CryptographicOperations.ZeroMemory(key);
                        }
                    }
                }

                offset += lineLength + (newline < 0 ? 0 : 1);
            }

            if (selectedKey is null)
            {
                throw new InvalidDataException($"M5 exact-session cipher material is absent for {sourceIdentity}.");
            }

            var result = new CipherMaterial(selectedInitial, selectedKey, Math.Max(1, epochs.Count));
            selectedKey = null;
            return result;
        }
        finally
        {
            if (selectedKey is not null)
            {
                CryptographicOperations.ZeroMemory(selectedKey);
            }
        }
    }

    private static bool TryReadCipherLine(
        ReadOnlySpan<byte> line,
        out byte initial,
        out byte[]? key,
        out bool isExactSessionEntry)
    {
        initial = 0;
        key = null;
        isExactSessionEntry = false;
        var entryOffset = line.IndexOf("packet-decode entry"u8);
        if (entryOffset < 0)
        {
            return false;
        }

        var entry = line[entryOffset..];
        var sequenceOffset = entry.IndexOf("sequence="u8);
        isExactSessionEntry = sequenceOffset < 0 ||
                              IsSequenceOne(entry[(sequenceOffset + "sequence="u8.Length)..]);
        var initialOffset = entry.IndexOf("initialKey=0x"u8);
        var keyOffset = entry.IndexOf("key256=\""u8);
        if (initialOffset < 0 || keyOffset < 0)
        {
            throw new InvalidDataException("M5 packet-decode entry is missing cipher fields.");
        }

        initialOffset += "initialKey=0x"u8.Length;
        if (!TryReadHexByte(entry[initialOffset..], out initial))
        {
            throw new InvalidDataException("M5 packet-decode initial key is malformed.");
        }

        var encodedKey = entry[(keyOffset + "key256=\""u8.Length)..];
        key = new byte[256];
        var cursor = 0;
        try
        {
            for (var index = 0; index < key.Length; index++)
            {
                while (cursor < encodedKey.Length && encodedKey[cursor] is (byte)' ' or (byte)'\t')
                {
                    cursor++;
                }

                if (!TryReadHexByte(encodedKey[cursor..], out key[index]))
                {
                    throw new InvalidDataException("M5 packet-decode cipher table is malformed.");
                }

                cursor += 2;
            }

            while (cursor < encodedKey.Length && encodedKey[cursor] is (byte)' ' or (byte)'\t')
            {
                cursor++;
            }

            if (cursor >= encodedKey.Length || encodedKey[cursor] != (byte)'\"')
            {
                throw new InvalidDataException("M5 packet-decode cipher table is not exactly 256 bytes.");
            }

            return true;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            key = null;
            throw;
        }
    }

    private static bool IsSequenceOne(ReadOnlySpan<byte> value) =>
        value.Length > 0 && value[0] == (byte)'1' &&
        (value.Length == 1 || value[1] is < (byte)'0' or > (byte)'9');

    private static bool TryReadHexByte(ReadOnlySpan<byte> value, out byte parsed)
    {
        parsed = 0;
        if (value.Length < 2 ||
            !TryReadHexNibble(value[0], out var high) ||
            !TryReadHexNibble(value[1], out var low))
        {
            return false;
        }

        parsed = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryReadHexNibble(byte value, out int parsed)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            parsed = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            parsed = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            parsed = value - (byte)'a' + 10;
            return true;
        }

        parsed = 0;
        return false;
    }

    private static IReadOnlyList<ReassembledFrame> Reassemble(IReadOnlyList<TraceRecord> records, ushort direction)
    {
        var pending = new byte[8192];
        var pendingCount = 0;
        var result = new List<ReassembledFrame>();
        ulong sequence = 0;
        long timestamp = 0;
        try
        {
            foreach (var record in records.Where(record => record.Direction == direction).OrderBy(record => record.Sequence))
            {
                if (pendingCount == 0)
                {
                    sequence = record.Sequence;
                    timestamp = record.WallUnixMs;
                }

                if (record.Payload.Length > pending.Length - pendingCount)
                {
                    throw new InvalidDataException("M5 transport reassembly buffer exceeded its bounded capacity.");
                }

                record.Payload.CopyTo(pending, pendingCount);
                pendingCount += record.Payload.Length;
                while (pendingCount >= 2)
                {
                    var length = pending[0] | (pending[1] << 8);
                    if (length is < 4 or > 4096)
                    {
                        throw new InvalidDataException($"M5 transport frame length {length} is invalid at sequence {sequence}.");
                    }

                    if (pendingCount < length)
                    {
                        break;
                    }

                    result.Add(new ReassembledFrame(sequence, timestamp, pending.AsSpan(0, length).ToArray()));
                    var remaining = pendingCount - length;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(pending, length, pending, 0, remaining);
                    }

                    CryptographicOperations.ZeroMemory(pending.AsSpan(remaining, pendingCount - remaining));
                    pendingCount = remaining;
                    if (pendingCount > 0)
                    {
                        sequence = record.Sequence;
                        timestamp = record.WallUnixMs;
                    }
                }
            }

            if (pendingCount != 0)
            {
                throw new InvalidDataException($"M5 transport stream ends with {pendingCount} incomplete bytes.");
            }

            return result;
        }
        catch
        {
            foreach (var frame in result)
            {
                CryptographicOperations.ZeroMemory(frame.Payload);
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pending);
        }
    }

    private static IReadOnlyList<TraceRecord> ReadTraceRecords(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < TraceHeaderLength || !bytes[..7].SequenceEqual("G2TRC01"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != 1)
        {
            throw new InvalidDataException("M5 restricted trace header is invalid.");
        }

        var records = new List<TraceRecord>();
        try
        {
            var offset = TraceHeaderLength;
            while (offset < bytes.Length)
            {
                if (bytes.Length - offset < TraceRecordHeaderLength)
                {
                    throw new InvalidDataException("M5 restricted trace record header is truncated.");
                }

                var header = bytes.Slice(offset, TraceRecordHeaderLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != TraceRecordMagic ||
                    BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 1 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != TraceRecordHeaderLength)
                {
                    throw new InvalidDataException("M5 restricted trace record identity is invalid.");
                }

                var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[64..]));
                if (length is < 0 or > 4096 || bytes.Length - offset - TraceRecordHeaderLength < length)
                {
                    throw new InvalidDataException("M5 restricted trace payload length is invalid.");
                }

                records.Add(new TraceRecord(
                    BinaryPrimitives.ReadUInt64LittleEndian(header[8..]),
                    BinaryPrimitives.ReadInt64LittleEndian(header[16..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[42..]),
                    bytes.Slice(offset + TraceRecordHeaderLength, length).ToArray()));
                offset += TraceRecordHeaderLength + length;
            }

            return records;
        }
        catch
        {
            foreach (var record in records)
            {
                CryptographicOperations.ZeroMemory(record.Payload);
            }

            throw;
        }
    }

    private static string Opcode(TimedDecodedFrame frame) => $"0x{frame.Decoded.ApplicationOpcode:X2}";
    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));
    private static string FormatSet(IReadOnlyList<string> values) => values.Count == 0 ? "NONE" : string.Join(", ", values);

    private sealed record CipherMaterial(byte InitialPreviousByte, byte[] KeyTable, int EpochCount);
    private sealed record TraceRecord(ulong Sequence, long WallUnixMs, ushort Direction, byte[] Payload);
    private sealed record ReassembledFrame(ulong Sequence, long WallUnixMs, byte[] Payload);
    private sealed record TimedDecodedFrame(ulong Sequence, long WallUnixMs, M4DecodedTransportFrame Decoded);
    private sealed record ActionMetadata(string SessionId, string Label, string Confidence, ulong StartSequence, ulong EndSequence, string? CallerRva);
}
