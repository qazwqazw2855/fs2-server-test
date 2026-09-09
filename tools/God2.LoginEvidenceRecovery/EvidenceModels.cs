namespace God2.LoginEvidenceRecovery;

internal sealed record TransportTimelineEntry(
    int SchemaVersion,
    string RunId,
    string Component,
    string ConnectionId,
    int Sequence,
    string Direction,
    DateTimeOffset TimestampUtc,
    int Offset,
    int Length,
    string Sha256,
    string RawEvidencePath,
    string BoundaryMeaning);

internal sealed record TransportSessionEntry(
    string RunId,
    string EventType,
    string Component,
    string ConnectionId,
    string RemoteEndpoint,
    string LocalEndpoint,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset TimestampUtc,
    string? DisconnectReason,
    long ReceivedBytes,
    long SentBytes);

internal sealed record ReconstructionChunk(
    int StreamOffset,
    int Length,
    uint? TcpSequence,
    DateTimeOffset TimestampUtc,
    string SourcePath,
    int SourcePacketIndex,
    string Sha256);

internal sealed record StreamReconstruction(
    byte[] Bytes,
    IReadOnlyList<ReconstructionChunk> Chunks,
    bool TcpSequenceAvailable,
    int DuplicateSegmentCount,
    int OverlapBytesTrimmed,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> Issues);

internal sealed record RecoveredFrame(
    string EvidenceId,
    string SessionId,
    string State,
    string Direction,
    DateTimeOffset TimestampUtc,
    int StreamOffset,
    int Length,
    string RawHex,
    string Sha256,
    string SourcePath,
    string CandidateRole,
    string VerificationStatus,
    string Reason);

internal sealed class CandidateSession
{
    public required string SessionId { get; init; }

    public required string SourceKind { get; init; }

    public required string State { get; init; }

    public required string ConnectionTuple { get; init; }

    public required IReadOnlyList<string> SourcePaths { get; init; }

    public required StreamReconstruction ClientToServer { get; init; }

    public required StreamReconstruction ServerToClient { get; init; }

    public required List<RecoveredFrame> ClientToServerFrames { get; init; }

    public required List<RecoveredFrame> ServerToClientFrames { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    public IEnumerable<RecoveredFrame> Frames =>
        ClientToServerFrames.Concat(ServerToClientFrames).OrderBy(frame => frame.TimestampUtc);

    public bool HasVerifiedLoginRequest =>
        Frames.Any(frame =>
            frame.CandidateRole == "LoginRequest" &&
            frame.VerificationStatus == "VERIFIED_RAW");

    public bool HasVerifiedLoginSuccessResponse =>
        Frames.Any(frame =>
            frame.CandidateRole == "LoginSuccessAndCharacterBootstrap" &&
            frame.VerificationStatus == "VERIFIED_RAW");
}

internal sealed record SourceInventoryEntry(
    string SourcePath,
    string FileType,
    string Sha256,
    DateTimeOffset CreatedTimestamp,
    DateTimeOffset ModifiedTimestamp,
    string SessionLineage,
    string ConnectionTuple,
    string Direction,
    string EventMarker,
    long? RawLength,
    string? RawHex,
    string ExistingClassification,
    string ExistingNotes);

internal sealed record VerificationDecision(
    string EvidenceId,
    string PreviousStatus,
    string NewStatus,
    string CandidateRole,
    IReadOnlyList<string> VerifiedFacts,
    IReadOnlyList<string> UnknownFacts,
    IReadOnlyList<string> SupportingEvidenceIds,
    IReadOnlyList<string> ContradictingEvidenceIds,
    string DecisionReason,
    string Sha256,
    string SourceLineage);
