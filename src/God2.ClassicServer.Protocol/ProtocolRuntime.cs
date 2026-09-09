using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Security.Cryptography;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Protocol;

public enum ProtocolStage
{
    Connected,
    Login,
    Authenticated,
    CharacterList,
    CharacterSelected,
    WorldEntering,
    WorldAuthenticated,
    MapBinding,
    PlayerAttached,
    ContentLoading,
    InWorld,
    Closing,
    Closed
}

public enum FrameReadStatus
{
    FrameReady,
    NeedMoreData,
    InvalidFrame,
    OversizedFrame
}

public enum PacketRouteStatus
{
    Handled,
    CapturedEvidence,
    CapturedUnknown,
    StageRejected,
    HandlerMissing,
    ProtocolError
}

public sealed record ProtocolFrame(ReadOnlyMemory<byte> Bytes)
{
    public void Clear()
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(Bytes, out var segment) &&
            segment.Array is not null)
        {
            segment.Array.AsSpan(segment.Offset, segment.Count).Clear();
        }
    }
}

public sealed record FrameReadIssue(FrameReadStatus Status, string Code, string Message, int DeclaredLength = 0);

public sealed record FrameAccumulatorResult(
    IReadOnlyList<ProtocolFrame> Frames,
    FrameReadIssue? Issue,
    int BufferedByteCount)
{
    public bool Succeeded => Issue is null || Issue.Status == FrameReadStatus.NeedMoreData;
}

public sealed class FrameAccumulator
{
    private byte[] _buffer;
    private int _start;
    private int _end;

    public FrameAccumulator(int maxPacketSize = 4096, int? maxBufferedBytes = null)
    {
        if (maxPacketSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPacketSize), "Packet size limit must allow a length prefix.");
        }

        MaxPacketSize = maxPacketSize;
        MaxBufferedBytes = maxBufferedBytes ?? checked(maxPacketSize * 2);
        if (MaxBufferedBytes < MaxPacketSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes), "Buffered byte limit must allow one maximum-size packet.");
        }

        _buffer = new byte[Math.Min(MaxBufferedBytes, 256)];
    }

    public int MaxPacketSize { get; }

    public int MaxBufferedBytes { get; }

    public int BufferedByteCount => _end - _start;

    public int BufferCapacity => _buffer.Length;

    public FrameAccumulatorResult Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBufferedBytes - BufferedByteCount)
        {
            Clear();
            return new FrameAccumulatorResult(
                [],
                new FrameReadIssue(
                    FrameReadStatus.OversizedFrame,
                    "frame.buffer_oversized",
                    "Buffered transport data exceeds the configured connection limit.",
                    bytes.Length),
                0);
        }

        if (!bytes.IsEmpty)
        {
            EnsureWritable(bytes.Length);
            bytes.CopyTo(_buffer.AsSpan(_end));
            _end += bytes.Length;
        }

        var frames = new List<ProtocolFrame>();

        while (BufferedByteCount >= 2)
        {
            var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_start, 2));
            if (declaredLength < 2)
            {
                ClearFrames(frames);
                Clear();
                return new FrameAccumulatorResult(
                    [],
                    new FrameReadIssue(FrameReadStatus.InvalidFrame, "frame.length_invalid", "Packet length must include the two-byte prefix.", declaredLength),
                    0);
            }

            if (declaredLength > MaxPacketSize)
            {
                ClearFrames(frames);
                Clear();
                return new FrameAccumulatorResult(
                    [],
                    new FrameReadIssue(FrameReadStatus.OversizedFrame, "frame.length_oversized", "Packet length exceeds the configured packet size limit.", declaredLength),
                    0);
            }

            if (BufferedByteCount < declaredLength)
            {
                CompactBufferedBytes();
                return new FrameAccumulatorResult(
                    frames,
                    new FrameReadIssue(FrameReadStatus.NeedMoreData, "frame.partial", "Partial packet buffered.", declaredLength),
                    BufferedByteCount);
            }

            var frame = _buffer.AsSpan(_start, declaredLength).ToArray();
            _start += declaredLength;
            frames.Add(new ProtocolFrame(frame));
        }

        CompactBufferedBytes();

        return new FrameAccumulatorResult(frames, null, BufferedByteCount);
    }

    private void EnsureWritable(int additionalBytes)
    {
        var required = BufferedByteCount + additionalBytes;
        if (_buffer.Length - _end >= additionalBytes)
        {
            return;
        }

        if (_start > 0)
        {
            CompactBufferedBytes();
            if (_buffer.Length - _end >= additionalBytes)
            {
                return;
            }
        }

        var newSize = Math.Min(MaxBufferedBytes, Math.Max(required, _buffer.Length * 2));
        var previousBuffer = _buffer;
        var replacement = new byte[newSize];
        previousBuffer.AsSpan(_start, BufferedByteCount).CopyTo(replacement);
        CryptographicOperations.ZeroMemory(previousBuffer);
        _buffer = replacement;
        _end = BufferedByteCount;
        _start = 0;
    }

    private void CompactBufferedBytes()
    {
        if (_start == 0)
        {
            return;
        }

        if (_start == _end)
        {
            Clear();
            return;
        }

        var previousEnd = _end;
        var remaining = BufferedByteCount;
        _buffer.AsSpan(_start, remaining).CopyTo(_buffer);
        _buffer.AsSpan(remaining, previousEnd - remaining).Clear();
        _start = 0;
        _end = remaining;
    }

    public void Clear()
    {
        _buffer.AsSpan(0, _end).Clear();
        _start = 0;
        _end = 0;
    }

    private static void ClearFrames(IEnumerable<ProtocolFrame> frames)
    {
        foreach (var frame in frames)
        {
            frame.Clear();
        }
    }
}

public sealed record TcpPayloadSegment(uint Sequence, ReadOnlyMemory<byte> Payload);

public sealed record TcpSequenceGap(uint Start, uint End);

public sealed record TcpStreamReconstructionResult(
    ReadOnlyMemory<byte> Bytes,
    int DuplicateSegmentCount,
    int OverlapBytesTrimmed,
    IReadOnlyList<TcpSequenceGap> Gaps);

public static class TcpApplicationStreamReconstructor
{
    public static TcpStreamReconstructionResult Reconstruct(IEnumerable<TcpPayloadSegment> source)
    {
        var segments = source
            .Where(segment => segment.Payload.Length > 0)
            .OrderBy(segment => segment.Sequence)
            .ThenBy(segment => segment.Payload.Length)
            .ToArray();
        if (segments.Length == 0)
        {
            return new TcpStreamReconstructionResult(ReadOnlyMemory<byte>.Empty, 0, 0, []);
        }

        var stream = new List<byte>();
        var gaps = new List<TcpSequenceGap>();
        var duplicateCount = 0;
        var overlapTrimmed = 0;
        ulong expected = segments[0].Sequence;

        foreach (var segment in segments)
        {
            var sequence = (ulong)segment.Sequence;
            if (sequence > expected)
            {
                gaps.Add(new TcpSequenceGap((uint)expected, segment.Sequence - 1));
                expected = sequence;
            }

            var overlap = sequence < expected
                ? checked((int)Math.Min((ulong)segment.Payload.Length, expected - sequence))
                : 0;
            if (overlap >= segment.Payload.Length)
            {
                duplicateCount++;
                overlapTrimmed += segment.Payload.Length;
                continue;
            }

            overlapTrimmed += overlap;
            stream.AddRange(segment.Payload.Span[overlap..].ToArray());
            expected = sequence + (uint)segment.Payload.Length;
        }

        return new TcpStreamReconstructionResult(stream.ToArray(), duplicateCount, overlapTrimmed, gaps);
    }
}

public interface IPacketSequenceValidator
{
    OperationResult Validate(PacketEnvelope packet);
}

public sealed class MonotonicPacketSequenceValidator : IPacketSequenceValidator
{
    private uint _lastSequence;

    public OperationResult Validate(PacketEnvelope packet)
    {
        if (packet.Header.Sequence == 0)
        {
            return OperationResult.Success;
        }

        if (packet.Header.Sequence <= _lastSequence)
        {
            return OperationResult.Failure("packet.sequence_invalid", "Packet sequence must increase monotonically.", packet.Header.OpcodeCandidate);
        }

        _lastSequence = packet.Header.Sequence;
        return OperationResult.Success;
    }
}

public interface IPacketChecksumBoundary
{
    OperationResult Validate(PacketEnvelope packet);
}

public sealed class NoRecoveredChecksumBoundary : IPacketChecksumBoundary
{
    public OperationResult Validate(PacketEnvelope packet) => OperationResult.Success;
}

public interface IPacketEncryptionBoundary
{
    OperationResult<PacketEnvelope> Decrypt(PacketEnvelope packet);
}

public sealed class NoRecoveredEncryptionBoundary : IPacketEncryptionBoundary
{
    public OperationResult<PacketEnvelope> Decrypt(PacketEnvelope packet) => OperationResult<PacketEnvelope>.Success(packet);
}

public sealed record UnknownPacketCapture(
    DateTimeOffset Timestamp,
    string ConnectionId,
    string SessionId,
    string Opcode,
    int PayloadLength,
    string RawPayloadSha256,
    string RawEvidencePath,
    ProtocolStage CurrentProtocolStage);

public interface IUnknownPacketCaptureSink
{
    void Capture(UnknownPacketCapture capture);
}

public sealed class InMemoryUnknownPacketCaptureSink : IUnknownPacketCaptureSink
{
    private readonly ConcurrentQueue<UnknownPacketCapture> _captures = [];
    private readonly int _maximumEntries;
    private readonly object _sync = new();
    private int _count;

    public InMemoryUnknownPacketCaptureSink(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public IReadOnlyList<UnknownPacketCapture> Captures
    {
        get
        {
            lock (_sync)
            {
                return _captures.ToArray();
            }
        }
    }

    public void Capture(UnknownPacketCapture capture)
    {
        lock (_sync)
        {
            _captures.Enqueue(capture);
            _count++;
            while (_count > _maximumEntries && _captures.TryDequeue(out _))
            {
                _count--;
            }
        }
    }
}

public sealed record ProtocolAuditEntry(
    DateTimeOffset Timestamp,
    string ConnectionId,
    string SessionId,
    string EventName,
    string Opcode,
    int PayloadLength,
    ProtocolStage Stage,
    string Message);

public interface IProtocolAuditLog
{
    void Write(ProtocolAuditEntry entry);
}

public sealed class InMemoryProtocolAuditLog : IProtocolAuditLog
{
    private readonly ConcurrentQueue<ProtocolAuditEntry> _entries = [];
    private readonly int _maximumEntries;
    private readonly object _sync = new();
    private int _count;

    public InMemoryProtocolAuditLog(int maximumEntries = 10_000)
    {
        _maximumEntries = Math.Max(1, maximumEntries);
    }

    public IReadOnlyList<ProtocolAuditEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Write(ProtocolAuditEntry entry)
    {
        lock (_sync)
        {
            _entries.Enqueue(entry);
            _count++;
            while (_count > _maximumEntries && _entries.TryDequeue(out _))
            {
                _count--;
            }
        }
    }
}

public static class PacketEvidenceHash
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
