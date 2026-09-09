using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.ClassicServer.Protocol;

public enum BattleCaptureDirection
{
    Unknown,
    ClientToServer,
    ServerToClient,
    Internal
}

public enum BattleCaptureStage
{
    SocketRaw,
    FramedEncrypted,
    FramedDecrypted,
    PayloadCompressed,
    PayloadDecompressed,
    OpcodeDispatched,
    SemanticMapped,
    SerializerOutput,
    Unknown
}

public enum BattleCaptureRedactionStatus
{
    NotRequired,
    Pending,
    Redacted,
    Restricted,
    Failed
}

public enum BattleCaptureDropPolicy
{
    DropNewestAndCount
}

public sealed record BattleCaptureRecord(
    string CaptureRecordId,
    string CaptureSessionId,
    BattleCaptureDirection Direction,
    string ConnectionSafeReference,
    long Sequence,
    TimeSpan TimestampDelta,
    BattleCaptureStage CaptureStage,
    int RawFrameLength,
    string RawFrameHash,
    string? OpcodeCandidate,
    int PayloadLength,
    string? DecryptedPayloadHash,
    string? DecompressedPayloadHash,
    OfficialBattleProtocolState StatePhase,
    string ClientBuildId,
    string CorrelationId,
    BattleCaptureRedactionStatus RedactionStatus,
    string? RawDataReference,
    string Notes)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CaptureRecordId) ||
            string.IsNullOrWhiteSpace(CaptureSessionId) ||
            string.IsNullOrWhiteSpace(ConnectionSafeReference) ||
            string.IsNullOrWhiteSpace(ClientBuildId))
        {
            errors.Add("battle.capture.identity_missing");
        }

        if (Sequence <= 0 || TimestampDelta < TimeSpan.Zero)
        {
            errors.Add("battle.capture.sequence_or_time_invalid");
        }

        if (RawFrameLength < 0 || PayloadLength < 0 || PayloadLength > RawFrameLength)
        {
            errors.Add("battle.capture.length_invalid");
        }

        if (!ClientBuildIdentity.IsSha256(RawFrameHash) ||
            (DecryptedPayloadHash is not null && !ClientBuildIdentity.IsSha256(DecryptedPayloadHash)) ||
            (DecompressedPayloadHash is not null && !ClientBuildIdentity.IsSha256(DecompressedPayloadHash)))
        {
            errors.Add("battle.capture.hash_invalid");
        }

        if (RawDataReference is not null && !ClientBuildIdentity.IsSafeRelativePath(RawDataReference))
        {
            errors.Add("battle.capture.raw_reference_not_safe_relative");
        }

        return errors.AsReadOnly();
    }
}

public sealed class BattleCaptureSession
{
    private long _sequence;

    public BattleCaptureSession(
        string captureSessionId,
        string clientBuildId,
        string automationRunId,
        DateTimeOffset startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(captureSessionId) ||
            string.IsNullOrWhiteSpace(clientBuildId) ||
            startedAtUtc == default)
        {
            throw new ArgumentException("Capture identity, client build, and start time are required.");
        }

        CaptureSessionId = captureSessionId;
        ClientBuildId = clientBuildId;
        AutomationRunId = automationRunId;
        StartedAtUtc = startedAtUtc;
    }

    public string CaptureSessionId { get; }

    public string ClientBuildId { get; }

    public string AutomationRunId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public long LastSequence => Interlocked.Read(ref _sequence);

    public long NextSequence() => Interlocked.Increment(ref _sequence);
}

public sealed record BattleCaptureWriteResult(
    bool Accepted,
    long DroppedRecordCount,
    string FailureCode);

public interface IBattleProtocolCaptureSink
{
    int Capacity { get; }

    long DroppedRecordCount { get; }

    BattleCaptureDropPolicy DropPolicy { get; }

    BattleCaptureWriteResult TryCapture(BattleCaptureRecord record);

    IReadOnlyList<BattleCaptureRecord> Snapshot();
}

public sealed class BoundedBattleProtocolCaptureSink : IBattleProtocolCaptureSink
{
    private readonly ConcurrentQueue<BattleCaptureRecord> _records = [];
    private int _count;
    private long _dropped;

    public BoundedBattleProtocolCaptureSink(int capacity)
    {
        if (capacity is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public long DroppedRecordCount => Interlocked.Read(ref _dropped);

    public BattleCaptureDropPolicy DropPolicy => BattleCaptureDropPolicy.DropNewestAndCount;

    public BattleCaptureWriteResult TryCapture(BattleCaptureRecord record)
    {
        if (record.Validate().Count != 0)
        {
            return new BattleCaptureWriteResult(false, DroppedRecordCount, "battle.capture.record_invalid");
        }

        var count = Interlocked.Increment(ref _count);
        if (count > Capacity)
        {
            Interlocked.Decrement(ref _count);
            var dropped = Interlocked.Increment(ref _dropped);
            return new BattleCaptureWriteResult(false, dropped, "battle.capture.queue_full");
        }

        _records.Enqueue(record);
        return new BattleCaptureWriteResult(true, DroppedRecordCount, string.Empty);
    }

    public IReadOnlyList<BattleCaptureRecord> Snapshot() =>
        _records.ToArray().OrderBy(record => record.Sequence).ToArray();
}

public sealed record BattleCaptureRedactionResult(
    bool Succeeded,
    ReadOnlyMemory<byte> RedactedBytes,
    BattleCaptureRedactionStatus Status,
    int RedactedMatchCount,
    string Sha256,
    string FailureCode);

public sealed class BattleCaptureRedactor
{
    public BattleCaptureRedactionResult Redact(
        ReadOnlySpan<byte> source,
        IEnumerable<ReadOnlyMemory<byte>> protectedValues)
    {
        var output = source.ToArray();
        var matchCount = 0;
        foreach (var secret in protectedValues)
        {
            if (secret.IsEmpty)
            {
                continue;
            }

            var offset = 0;
            while (offset <= output.Length - secret.Length)
            {
                var found = output.AsSpan(offset).IndexOf(secret.Span);
                if (found < 0)
                {
                    break;
                }

                var absolute = offset + found;
                output.AsSpan(absolute, secret.Length).Fill(0x2A);
                matchCount++;
                offset = absolute + secret.Length;
            }
        }

        return new BattleCaptureRedactionResult(
            true,
            output,
            matchCount == 0 ? BattleCaptureRedactionStatus.NotRequired : BattleCaptureRedactionStatus.Redacted,
            matchCount,
            Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant(),
            string.Empty);
    }
}

public sealed class BattleCaptureCanonicalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public byte[] Canonicalize(BattleCaptureRecord record)
    {
        var canonical = new
        {
            record.CaptureRecordId,
            record.CaptureSessionId,
            direction = record.Direction.ToString(),
            record.ConnectionSafeReference,
            record.Sequence,
            timestampDeltaTicks = record.TimestampDelta.Ticks,
            captureStage = record.CaptureStage.ToString(),
            record.RawFrameLength,
            rawFrameHash = record.RawFrameHash.ToLowerInvariant(),
            record.OpcodeCandidate,
            record.PayloadLength,
            decryptedPayloadHash = record.DecryptedPayloadHash?.ToLowerInvariant(),
            decompressedPayloadHash = record.DecompressedPayloadHash?.ToLowerInvariant(),
            statePhase = record.StatePhase.ToString(),
            record.ClientBuildId,
            record.CorrelationId,
            redactionStatus = record.RedactionStatus.ToString(),
            record.RawDataReference,
            record.Notes
        };
        return JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
    }

    public string Hash(BattleCaptureRecord record) =>
        Convert.ToHexString(SHA256.HashData(Canonicalize(record))).ToLowerInvariant();
}

public sealed record BattleCaptureReplayDecoderResult(
    bool Succeeded,
    string SemanticHash,
    string FailureCode);

public interface IBattleCaptureReplayDecoder
{
    BattleCaptureReplayDecoderResult Decode(BattleCaptureRecord record, ReadOnlyMemory<byte> rawData);
}

public sealed record BattleCaptureReplayItem(
    BattleCaptureRecord Record,
    ReadOnlyMemory<byte> RawData,
    string ExpectedSemanticHash);

public sealed record BattleCaptureReplayResult(
    bool Succeeded,
    int ReplayedCount,
    long? FirstDivergenceSequence,
    string FailureCode,
    bool NetworkSideEffect,
    bool DatabaseSideEffect,
    bool RewardSideEffect,
    bool QuestSideEffect);

public sealed class BattleCaptureReplayRunner
{
    public BattleCaptureReplayResult Replay(
        string expectedClientBuildId,
        IEnumerable<BattleCaptureReplayItem> source,
        IBattleCaptureReplayDecoder decoder)
    {
        var records = source.OrderBy(item => item.Record.Sequence).ToArray();
        long previous = 0;
        foreach (var item in records)
        {
            if (!string.Equals(item.Record.ClientBuildId, expectedClientBuildId, StringComparison.Ordinal))
            {
                return Failed(item.Record.Sequence, "battle.capture.replay_client_build_mismatch");
            }

            if (item.Record.Sequence <= previous)
            {
                return Failed(item.Record.Sequence, "battle.capture.replay_sequence_invalid");
            }

            if (item.Record.RawFrameLength != item.RawData.Length ||
                !string.Equals(
                    item.Record.RawFrameHash,
                    Convert.ToHexString(SHA256.HashData(item.RawData.Span)).ToLowerInvariant(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failed(item.Record.Sequence, "battle.capture.replay_raw_hash_mismatch");
            }

            var decoded = decoder.Decode(item.Record, item.RawData);
            if (!decoded.Succeeded ||
                !string.Equals(decoded.SemanticHash, item.ExpectedSemanticHash, StringComparison.Ordinal))
            {
                return Failed(
                    item.Record.Sequence,
                    decoded.Succeeded ? "battle.capture.replay_semantic_divergence" : decoded.FailureCode);
            }

            previous = item.Record.Sequence;
        }

        return new BattleCaptureReplayResult(
            true,
            records.Length,
            null,
            string.Empty,
            NetworkSideEffect: false,
            DatabaseSideEffect: false,
            RewardSideEffect: false,
            QuestSideEffect: false);
    }

    private static BattleCaptureReplayResult Failed(long sequence, string failureCode) =>
        new(
            false,
            0,
            sequence,
            failureCode,
            NetworkSideEffect: false,
            DatabaseSideEffect: false,
            RewardSideEffect: false,
            QuestSideEffect: false);
}
