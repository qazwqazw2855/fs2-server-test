using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.OfflineClientReverseEngineering;

public sealed record M4ExhaustionFrame(
    string Direction,
    ulong Sequence,
    string TimestampUtc,
    int TransportLength,
    string Opcode,
    int ApplicationPayloadLength,
    bool ChecksumValid,
    string ApplicationSha256,
    int? NpcHandleCandidate,
    int? SecondaryU16Candidate,
    string? ActionLabel,
    string? ActionConfidence,
    bool RawPacketBodyRetained);

public sealed record M4CloseCandidateCorrelation(
    string SessionId,
    string Opcode,
    ulong Sequence,
    int PayloadLength,
    string ApplicationSha256,
    int? NpcHandleCandidate,
    int? SecondaryU16Candidate,
    long? MillisecondsSincePriorDialog,
    string? NextServerOpcode,
    long? NextServerLatencyMilliseconds,
    string? ActionLabel,
    string? ActionConfidence);

public sealed record M4CaptureSessionExhaustion(
    string SessionId,
    string TraceSha256,
    string GeneralLogSha256,
    string CipherTableSha256,
    string InitialPreviousByte,
    int CipherEpochCount,
    int ClientToServerFrames,
    int ServerToClientFrames,
    int ChecksumFailures,
    IReadOnlyList<M4ExhaustionFrame> RelevantFrames,
    bool CipherMaterialRetained,
    bool RawPacketBodyRetained);

public sealed record M4ExistingCaptureExhaustionSnapshot(
    string SchemaVersion,
    string Status,
    IReadOnlyList<M4CaptureSessionExhaustion> Sessions,
    IReadOnlyList<M4CloseCandidateCorrelation> CloseCandidates,
    IReadOnlyDictionary<string, int> ClientOpcodeCounts,
    IReadOnlyDictionary<string, int> ServerOpcodeCounts,
    int Captured086Count,
    int Captured039Count,
    int Captured037Count,
    int Captured038Count,
    bool CipherMaterialRetained,
    bool RawPacketBodiesRetained,
    bool ClientWriteAccessUsed,
    bool NetworkBytesEmitted,
    bool FakeNetworkBytes);

/// <summary>
/// Exhausts all already-present automation traces for M4 application opcodes. The scanner
/// retains only identities, timings and hashes; session keys and packet bodies never enter
/// the generated artifact.
/// </summary>
public static class M4ExistingCaptureExhaustionRecovery
{
    private const int TraceHeaderLength = 24;
    private const int TraceRecordHeaderLength = 188;
    private const uint TraceRecordMagic = 0x31523247;
    private const ushort ClientToServerDirection = 1;
    private const ushort ServerToClientDirection = 2;

    private static readonly Regex CipherEntryRegex = new(
        "packet-decode entry(?: sequence=1)? .*?initialKey=0x(?<initial>[0-9A-F]{2}).*?key256=\\\"(?<key>(?:[0-9A-F]{2} ?){256})\\\"",
        RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnyCipherEntryRegex = new(
        "packet-decode entry(?: sequence=\\d+)? .*?initialKey=0x(?<initial>[0-9A-F]{2}).*?key256=\\\"(?<key>(?:[0-9A-F]{2} ?){256})\\\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<byte> RelevantOpcodes =
    [
        0x37, 0x38, 0x39, 0x41, 0x7A, 0x86
    ];

    public static M4ExistingCaptureExhaustionSnapshot Analyze(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        var captureRoot = Path.Combine(
            workspaceRoot,
            "Artifacts",
            "ClientInstrumentation",
            "ElevatedAutomationHost");
        var transactions = ReadActionMetadata(Path.Combine(
            workspaceRoot,
            "protocol",
            "evidence",
            "current-build",
            "chinese-labeled-captures",
            "action-transactions.json"));
        var captureSessionIds = transactions.Keys
            .Select(key => key.SessionId)
            .ToHashSet(StringComparer.Ordinal);
        var sessions = new List<M4CaptureSessionExhaustion>();
        var closeCandidates = new List<M4CloseCandidateCorrelation>();
        var clientOpcodeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var serverOpcodeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var sessionDirectory in Directory.EnumerateDirectories(captureRoot, "host-run-*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            var actionSessionId = Path.GetFileName(sessionDirectory);
            if (!captureSessionIds.Contains(actionSessionId))
            {
                continue;
            }

            var attempts = Directory.EnumerateDirectories(sessionDirectory, "attempt-*-trace", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    File.Exists(Path.Combine(path, "sensitive", "trace.bin")) &&
                    File.Exists(Path.Combine(path, "general.log")) &&
                    new FileInfo(Path.Combine(path, "sensitive", "trace.bin")).Length > TraceHeaderLength)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (attempts.Length == 0)
            {
                continue;
            }

            foreach (var attempt in attempts)
            {
                var sessionId = attempts.Length == 1
                    ? actionSessionId
                    : $"{actionSessionId}/{Path.GetFileName(attempt)}";
                var tracePath = Path.Combine(attempt, "sensitive", "trace.bin");
                var logPath = Path.Combine(attempt, "general.log");
                var trace = BoundedFile.ReadAllBytes(tracePath, 16L * 1024 * 1024, "M4 exhaustion trace");
                var log = BoundedFile.ReadAllBytes(logPath, 16L * 1024 * 1024, "M4 exhaustion general log");
                var cipher = ReadCipher(log, logPath);
                var records = ReadTraceRecords(trace);
                var c2s = DecodeDirection(records, ClientToServerDirection, cipher);
                var s2c = DecodeDirection(records, ServerToClientDirection, cipher);
                CountOpcodes(c2s.Where(frame => frame.Decoded.ChecksumValid), clientOpcodeCounts);
                CountOpcodes(s2c.Where(frame => frame.Decoded.ChecksumValid), serverOpcodeCounts);

                var relevant = c2s.Concat(s2c)
                    .Where(frame => frame.Decoded.ChecksumValid && RelevantOpcodes.Contains(frame.Decoded.ApplicationOpcode))
                    .OrderBy(frame => frame.WallUnixMs)
                    .ThenBy(frame => frame.Sequence)
                    .Select(frame => ToEvidenceFrame(
                        sessionId,
                        frame,
                        transactions.GetValueOrDefault((actionSessionId, frame.Sequence))))
                    .ToArray();
                foreach (var candidate in c2s.Where(frame =>
                             frame.Decoded.ChecksumValid &&
                             frame.Decoded.ApplicationOpcode is 0x39 or 0x86))
                {
                    var priorDialog = s2c
                        .Where(frame => frame.Decoded.ApplicationOpcode == 0x7A && frame.WallUnixMs <= candidate.WallUnixMs)
                        .OrderByDescending(frame => frame.WallUnixMs)
                        .FirstOrDefault();
                    var nextServer = s2c
                        .Where(frame => frame.WallUnixMs >= candidate.WallUnixMs)
                        .OrderBy(frame => frame.WallUnixMs)
                        .FirstOrDefault();
                    var metadata = transactions.GetValueOrDefault((actionSessionId, candidate.Sequence));
                    closeCandidates.Add(new M4CloseCandidateCorrelation(
                        sessionId,
                        $"0x{candidate.Decoded.ApplicationOpcode:X2}",
                        candidate.Sequence,
                        candidate.Decoded.ApplicationPayload.Length,
                        candidate.Decoded.ApplicationSha256,
                        candidate.Decoded.ApplicationPayload.Length >= 2
                            ? BinaryPrimitives.ReadUInt16LittleEndian(candidate.Decoded.ApplicationPayload)
                            : null,
                        candidate.Decoded.ApplicationPayload.Length >= 4
                            ? BinaryPrimitives.ReadUInt16LittleEndian(candidate.Decoded.ApplicationPayload.AsSpan(2))
                            : null,
                        priorDialog is null ? null : candidate.WallUnixMs - priorDialog.WallUnixMs,
                        nextServer is null ? null : $"0x{nextServer.Decoded.ApplicationOpcode:X2}",
                        nextServer is null ? null : nextServer.WallUnixMs - candidate.WallUnixMs,
                        metadata?.Label,
                        metadata?.Confidence));
                }

                sessions.Add(new M4CaptureSessionExhaustion(
                    sessionId,
                    Hash(trace),
                    Hash(log),
                    Hash(cipher.KeyTable),
                    $"0x{cipher.InitialPreviousByte:X2}",
                    cipher.EpochCount,
                    c2s.Count,
                    s2c.Count,
                    c2s.Count(frame => !frame.Decoded.ChecksumValid) + s2c.Count(frame => !frame.Decoded.ChecksumValid),
                    relevant,
                    CipherMaterialRetained: false,
                    RawPacketBodyRetained: false));
                Array.Clear(cipher.KeyTable);
                Array.Clear(trace);
                Array.Clear(log);
            }
        }

        if (sessions.Count == 0)
        {
            throw new InvalidDataException("No existing instrumented Client sessions were available for M4 exhaustion.");
        }

        var allRelevant = sessions.SelectMany(session => session.RelevantFrames).ToArray();
        var unresolvedSingleEpochChecksumFailure = sessions.Any(session =>
            session.ChecksumFailures != 0 && session.CipherEpochCount == 1);
        return new M4ExistingCaptureExhaustionSnapshot(
            "god2-roadmap-m4-existing-capture-exhaustion-v1",
            unresolvedSingleEpochChecksumFailure
                ? "BLOCKED_BY_CHECKSUM_FAILURE"
                : sessions.All(session => session.ChecksumFailures == 0)
                    ? "PASS"
                    : "PASS_WITH_MIXED_CIPHER_EPOCH_FRAMES_EXCLUDED",
            sessions,
            closeCandidates,
            clientOpcodeCounts,
            serverOpcodeCounts,
            allRelevant.Count(frame => frame.Direction == "C2S" && frame.Opcode == "0x86"),
            allRelevant.Count(frame => frame.Direction == "C2S" && frame.Opcode == "0x39"),
            allRelevant.Count(frame => frame.Direction == "C2S" && frame.Opcode == "0x37"),
            allRelevant.Count(frame => frame.Direction == "C2S" && frame.Opcode == "0x38"),
            CipherMaterialRetained: false,
            RawPacketBodiesRetained: false,
            ClientWriteAccessUsed: false,
            NetworkBytesEmitted: false,
            FakeNetworkBytes: false);
    }

    private static M4ExhaustionFrame ToEvidenceFrame(
        string sessionId,
        TimedDecodedFrame frame,
        ActionMetadata? metadata)
    {
        var payload = frame.Decoded.ApplicationPayload;
        var hasNpcIdentity = frame.Decoded.ApplicationOpcode is 0x37 or 0x38 or 0x39 or 0x86;
        return new M4ExhaustionFrame(
            frame.Direction == ClientToServerDirection ? "C2S" : "S2C",
            frame.Sequence,
            FormatTimestamp(frame.WallUnixMs),
            frame.Decoded.DeclaredLength,
            $"0x{frame.Decoded.ApplicationOpcode:X2}",
            payload.Length,
            frame.Decoded.ChecksumValid,
            frame.Decoded.ApplicationSha256,
            hasNpcIdentity && payload.Length >= 2
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload)
                : null,
            hasNpcIdentity && payload.Length >= 4
                ? BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2))
                : null,
            metadata?.Label,
            metadata?.Confidence,
            RawPacketBodyRetained: false);
    }

    private static IReadOnlyList<TimedDecodedFrame> DecodeDirection(
        IReadOnlyList<TraceRecord> records,
        ushort direction,
        CipherMaterial cipher)
    {
        var frames = Reassemble(records, direction);
        return frames.Select(frame => new TimedDecodedFrame(
                direction,
                frame.Sequence,
                frame.WallUnixMs,
                M4NpcInteractionEvidenceRecovery.DecodeTransportFrameForEvidence(
                    frame.Payload,
                    cipher.KeyTable,
                    cipher.InitialPreviousByte)))
            .ToArray();
    }

    private static void CountOpcodes(
        IEnumerable<TimedDecodedFrame> frames,
        IDictionary<string, int> counts)
    {
        foreach (var frame in frames)
        {
            var opcode = $"0x{frame.Decoded.ApplicationOpcode:X2}";
            counts.TryGetValue(opcode, out var current);
            counts[opcode] = current + 1;
        }
    }

    private static CipherMaterial ReadCipher(ReadOnlySpan<byte> generalLog, string sourceIdentity)
    {
        var text = new UTF8Encoding(false, true).GetString(generalLog);
        var match = CipherEntryRegex.Match(text);
        if (!match.Success)
        {
            throw new InvalidDataException($"Exact-session cipher material is absent from existing metadata log {sourceIdentity}.");
        }

        var key = Convert.FromHexString(match.Groups["key"].Value.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (key.Length != 256)
        {
            throw new InvalidDataException($"Exact-session key table length is {key.Length}, expected 256.");
        }

        var epochs = AnyCipherEntryRegex.Matches(text)
            .Select(value => $"{value.Groups["initial"].Value}:{Hash(Convert.FromHexString(
                value.Groups["key"].Value.Replace(" ", string.Empty, StringComparison.Ordinal)))}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new CipherMaterial(
            byte.Parse(match.Groups["initial"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            key,
            Math.Max(1, epochs));
    }

    private static IReadOnlyDictionary<(string SessionId, ulong Sequence), ActionMetadata> ReadActionMetadata(string path)
    {
        using var document = JsonDocument.Parse(BoundedFile.ReadAllBytes(path, 16L * 1024 * 1024, "M4 action metadata"));
        var result = new Dictionary<(string, ulong), ActionMetadata>();
        foreach (var transaction in document.RootElement.EnumerateArray())
        {
            if (!transaction.TryGetProperty("triggerOutbound", out var trigger) || trigger.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var sessionId = transaction.GetProperty("sessionId").GetString()!;
            var sequence = transaction.GetProperty("startSequence").GetUInt64();
            result[(sessionId, sequence)] = new ActionMetadata(
                transaction.GetProperty("labelCandidate").GetString()!,
                transaction.GetProperty("confidence").GetString()!);
        }

        return result;
    }

    private static IReadOnlyList<ReassembledFrame> Reassemble(IReadOnlyList<TraceRecord> records, ushort direction)
    {
        var pending = new List<byte>();
        var result = new List<ReassembledFrame>();
        ulong startSequence = 0;
        long startTimestamp = 0;
        foreach (var record in records.Where(record => record.Direction == direction).OrderBy(record => record.Sequence))
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
                    throw new InvalidDataException($"M4 transport stream has invalid frame length {declared} at trace sequence {startSequence}.");
                }

                if (pending.Count < declared)
                {
                    break;
                }

                result.Add(new ReassembledFrame(startSequence, startTimestamp, pending.Take(declared).ToArray()));
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
            throw new InvalidDataException($"M4 transport stream ends with {pending.Count} incomplete bytes.");
        }

        return result;
    }

    private static IReadOnlyList<TraceRecord> ReadTraceRecords(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < TraceHeaderLength ||
            !bytes[..7].SequenceEqual("G2TRC01"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != 1)
        {
            throw new InvalidDataException("Invalid M4 restricted trace header.");
        }

        var records = new List<TraceRecord>();
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

            records.Add(new TraceRecord(
                BinaryPrimitives.ReadUInt64LittleEndian(header[8..]),
                BinaryPrimitives.ReadInt64LittleEndian(header[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[42..]),
                bytes.Slice(offset + TraceRecordHeaderLength, capturedLength).ToArray()));
            offset += TraceRecordHeaderLength + capturedLength;
        }

        return records;
    }

    private static string FormatTimestamp(long unixMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));

    private sealed record CipherMaterial(byte InitialPreviousByte, byte[] KeyTable, int EpochCount);
    private sealed record TraceRecord(ulong Sequence, long WallUnixMs, ushort Direction, byte[] Payload);
    private sealed record ReassembledFrame(ulong Sequence, long WallUnixMs, byte[] Payload);
    private sealed record TimedDecodedFrame(ushort Direction, ulong Sequence, long WallUnixMs, M4DecodedTransportFrame Decoded);
    private sealed record ActionMetadata(string Label, string Confidence);
}
