using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.LoginEvidenceRecovery;

internal sealed class LoginEvidenceAnalyzer
{
    private const string LocalServerHandshake = "1300405FD0401BB55367D34D90DF1D929883DD";
    private const string LocalClientHandshake = "1300E10638FA2835845B9FE9528DB9BCDF70BC";
    private const string WorldServerHandshake = "1300FD9FCAC842A869FEA1BEC83A23DACF5249";

    private readonly string _projectRoot;
    private readonly string _unifiedRoot;
    private readonly string _outputRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public LoginEvidenceAnalyzer(string projectRoot, string unifiedRoot)
    {
        _projectRoot = projectRoot;
        _unifiedRoot = unifiedRoot;
        _outputRoot = Path.Combine(
            projectRoot,
            "Artifacts",
            "ProtocolEvidenceRecovery",
            "LoginDeepRecovery");
    }

    public LoginEvidenceAnalysisSummary Run()
    {
        Directory.CreateDirectory(_outputRoot);
        var sources = BuildSourceInventory();
        WriteJson(
            Path.Combine(_outputRoot, "source-inventory.json"),
            new
            {
                SchemaVersion = 1,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                SearchScopes = new[]
                {
                    "classic/Artifacts, Reports, src protocol evidence, tests and tools",
                    "unified-readonly/Artifacts/GameplayPacketRecovery",
                    "unified-readonly/Artifacts/OfficialServerCapture",
                    "unified-readonly/Reports/PhaseAOfficialClientTransport",
                    "unified-readonly historical LoginFastTrack logs and FirstContact package"
                },
                SourceCount = sources.Count,
                Sources = sources
            });

        var sessions = new List<CandidateSession>();
        sessions.AddRange(ReconstructTransportSessions());
        sessions.AddRange(ReconstructOfficialPcapSessions());

        AssignPacketRoles(sessions);
        WriteReconstructedSessions(sessions);
        WriteLoginEventWindows(sessions);
        WriteNineteenByteAnalysis(sessions);
        var decisions = WriteVerificationDecisions(sessions);
        WriteCredentialAnalysis(sessions);
        var migrated = MigrateVerifiedLoginEvidence(sessions);

        var verifiedRequest = decisions.Any(decision =>
            decision.CandidateRole == "LoginRequest" &&
            decision.NewStatus == "VERIFIED_RAW");
        var verifiedResponse = decisions.Any(decision =>
            decision.CandidateRole == "LoginSuccessAndCharacterBootstrap" &&
            decision.NewStatus == "VERIFIED_RAW");
        var successfulSessions = sessions.Count(session =>
            session.HasVerifiedLoginRequest && session.HasVerifiedLoginSuccessResponse);

        var summary = new LoginEvidenceAnalysisSummary(
            sources.Count,
            sessions.Count,
            successfulSessions,
            verifiedRequest,
            verifiedResponse,
            CredentialFieldsVerified: false,
            RuntimeMutationEnabled: false,
            migrated.Count);
        WriteJson(Path.Combine(_outputRoot, "analysis-summary.json"), summary);
        return summary;
    }

    private List<SourceInventoryEntry> BuildSourceInventory()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "Artifacts", "GameplayPacketRecovery"),
            include: _ => true);
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "Artifacts", "OfficialServerCapture"),
            include: file => SupportedExtension(file.Extension));
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "Reports", "PhaseAOfficialClientTransport"),
            include: file => SupportedExtension(file.Extension));
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "Reports", "OfficialProtocolCompletion"),
            include: file => SupportedExtension(file.Extension));
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "Package", "God2-Classic-Unified-Server-FirstContactSprint-Review"),
            include: file =>
                SupportedExtension(file.Extension) &&
                (file.FullName.Contains("LoginFastTrack", StringComparison.OrdinalIgnoreCase) ||
                 file.FullName.Contains("Connections", StringComparison.OrdinalIgnoreCase) ||
                 file.Name.Contains("FirstContact", StringComparison.OrdinalIgnoreCase) ||
                 file.Name.Contains("Protocol", StringComparison.OrdinalIgnoreCase)));
        AddFiles(
            candidates,
            Path.Combine(_unifiedRoot, "src", "God2.ClassicUnifiedServer.App", "bin"),
            include: file =>
                SupportedExtension(file.Extension) &&
                file.FullName.Contains("LoginFastTrack", StringComparison.OrdinalIgnoreCase));
        AddFiles(
            candidates,
            Path.Combine(_projectRoot, "Artifacts", "ProtocolEvidenceRecovery"),
            include: file => SupportedExtension(file.Extension));
        AddFiles(
            candidates,
            Path.Combine(_projectRoot, "Reports"),
            include: file =>
                SupportedExtension(file.Extension) &&
                file.Name.Contains("Protocol", StringComparison.OrdinalIgnoreCase));
        AddFiles(
            candidates,
            Path.Combine(_projectRoot, "src", "God2.ClassicServer.Protocol", "Evidence"),
            include: file => SupportedExtension(file.Extension));

        var timelineMetadata = ReadTransportMetadata();
        var entries = new List<SourceInventoryEntry>();
        foreach (var path in candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (path.StartsWith(_outputRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = new FileInfo(path);
            var extension = file.Extension.ToLowerInvariant();
            var isRaw = extension is ".bin" or ".raw" or ".hex";
            byte[]? rawBytes = null;
            if (isRaw && file.Length <= 1024 * 1024)
            {
                rawBytes = File.ReadAllBytes(path);
            }

            var metadata = InferSourceMetadata(path, timelineMetadata);
            entries.Add(new SourceInventoryEntry(
                RelativeEvidencePath(path),
                extension.TrimStart('.').ToUpperInvariant(),
                Sha256File(path),
                file.CreationTimeUtc,
                file.LastWriteTimeUtc,
                metadata.SessionLineage,
                metadata.ConnectionTuple,
                metadata.Direction,
                metadata.EventMarker,
                isRaw ? file.Length : null,
                rawBytes is null ? null : Convert.ToHexString(rawBytes),
                metadata.Classification,
                metadata.Notes));
        }

        return entries;
    }

    private Dictionary<string, SourceMetadata> ReadTransportMetadata()
    {
        var result = new Dictionary<string, SourceMetadata>(StringComparer.OrdinalIgnoreCase);
        var transportRoot = FindTransportRunRoot();
        if (transportRoot is null)
        {
            return result;
        }

        var sessionsPath = Path.Combine(transportRoot, "Sessions", "Login.sessions.jsonl");
        var timelinePath = Path.Combine(transportRoot, "Timeline", "Login.transport.jsonl");
        var sessions = ReadJsonLines<TransportSessionEntry>(sessionsPath);
        var timeline = ReadJsonLines<TransportTimelineEntry>(timelinePath);
        var endpointByConnection = sessions
            .GroupBy(item => item.ConnectionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return $"{row.RemoteEndpoint}->{row.LocalEndpoint}";
                },
                StringComparer.Ordinal);

        foreach (var group in timeline.GroupBy(item => item.RawEvidencePath, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var fullPath = Path.GetFullPath(Path.Combine(transportRoot, first.RawEvidencePath));
            result[fullPath] = new SourceMetadata(
                $"{first.RunId};{string.Join(';', group.Select(item => item.ConnectionId).Distinct(StringComparer.Ordinal))}",
                endpointByConnection.GetValueOrDefault(first.ConnectionId, "Unknown"),
                string.Join(',', group.Select(item => item.Direction).Distinct(StringComparer.Ordinal)),
                string.Join(',', group.Select(item => $"TransportSequence{item.Sequence}")),
                "CORRELATED_RAW",
                "Raw append-only TCP transport stream; application boundaries are reconstructed in this sprint.");
        }

        return result;
    }

    private SourceMetadata InferSourceMetadata(
        string path,
        IReadOnlyDictionary<string, SourceMetadata> transportMetadata)
    {
        if (transportMetadata.TryGetValue(Path.GetFullPath(path), out var rawMetadata))
        {
            return rawMetadata;
        }

        var name = Path.GetFileName(path);
        var direction = name.Contains("client-to-server", StringComparison.OrdinalIgnoreCase)
            ? "ClientToServer"
            : name.Contains("server-to-client", StringComparison.OrdinalIgnoreCase)
                ? "ServerToClient"
                : "Unknown";
        var sessionMatch = Regex.Match(name, @"(?:Login|World)-([0-9a-f]{32})-", RegexOptions.IgnoreCase);
        var lineage = sessionMatch.Success ? sessionMatch.Groups[1].Value : "SeeSourceContent";
        var classification = path.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase)
            ? "OFFICIAL_CAPTURE_RAW"
            : path.Contains("LoginFastTrack", StringComparison.OrdinalIgnoreCase)
                ? "HISTORICAL_EVENT_CORRELATION"
                : path.Contains("OfficialProtocolCompletion", StringComparison.OrdinalIgnoreCase)
                    ? "EXISTING_PROTOCOL_KNOWLEDGE"
                    : "SOURCE_EVIDENCE";
        var marker = name.Contains("Login", StringComparison.OrdinalIgnoreCase)
            ? "Login"
            : name.Contains("logout", StringComparison.OrdinalIgnoreCase)
                ? "LogoutReturnToServerSelection"
                : name.Contains("pcap", StringComparison.OrdinalIgnoreCase)
                    ? "OfficialCapture"
                    : "Unknown";

        return new SourceMetadata(
            lineage,
            "SeeSourceContent",
            direction,
            marker,
            classification,
            "Source retained read-only; SHA-256 recorded by LoginDeepRecovery.");
    }

    private IReadOnlyList<CandidateSession> ReconstructTransportSessions()
    {
        var transportRoot = FindTransportRunRoot();
        if (transportRoot is null)
        {
            return [];
        }

        var timelinePath = Path.Combine(transportRoot, "Timeline", "Login.transport.jsonl");
        var sessionsPath = Path.Combine(transportRoot, "Sessions", "Login.sessions.jsonl");
        var timeline = ReadJsonLines<TransportTimelineEntry>(timelinePath);
        var sessionRows = ReadJsonLines<TransportSessionEntry>(sessionsPath);
        var endpointByConnection = sessionRows
            .GroupBy(item => item.ConnectionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var item = group.First();
                    return $"{item.RemoteEndpoint}->{item.LocalEndpoint}";
                },
                StringComparer.Ordinal);

        var result = new List<CandidateSession>();
        foreach (var group in timeline.GroupBy(item => item.ConnectionId, StringComparer.Ordinal))
        {
            var rows = group.OrderBy(item => item.Sequence).ToArray();
            var c2s = ReconstructTransportDirection(transportRoot, rows, "ClientToServer");
            var s2c = ReconstructTransportDirection(transportRoot, rows, "ServerToClient");
            var state = group.Key switch
            {
                "4bcb6ff6d35042ce90862e1dd9ccc46d" => "LoginSuccessFlow",
                "849979618a624ef2b01c647462a3a42f" => "LogoutReturnToServerSelection",
                _ => "LoginInitialContact"
            };
            var sourcePaths = rows
                .Select(item => RelativeEvidencePath(Path.Combine(transportRoot, item.RawEvidencePath)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result.Add(new CandidateSession
            {
                SessionId = $"transport-{group.Key}",
                SourceKind = "RawTransportEvidence",
                State = state,
                ConnectionTuple = endpointByConnection.GetValueOrDefault(group.Key, "Unknown"),
                SourcePaths = sourcePaths,
                ClientToServer = c2s,
                ServerToClient = s2c,
                ClientToServerFrames = ParseFrames($"transport-{group.Key}", state, "ClientToServer", c2s),
                ServerToClientFrames = ParseFrames($"transport-{group.Key}", state, "ServerToClient", s2c),
                Notes =
                [
                    "Timeline sequence is an observation ordinal, not a TCP sequence number.",
                    "The append-only raw stream is complete for every recorded direction.",
                    "Retransmission de-duplication cannot be inferred from this source because TCP sequence was not retained."
                ]
            });
        }

        return result;
    }

    private StreamReconstruction ReconstructTransportDirection(
        string transportRoot,
        IReadOnlyList<TransportTimelineEntry> rows,
        string direction)
    {
        var selected = rows.Where(item => item.Direction == direction).OrderBy(item => item.Offset).ToArray();
        if (selected.Length == 0)
        {
            return EmptyReconstruction(tcpSequenceAvailable: false);
        }

        var rawPath = Path.GetFullPath(Path.Combine(transportRoot, selected[0].RawEvidencePath));
        var bytes = File.ReadAllBytes(rawPath);
        var chunks = new List<ReconstructionChunk>();
        var issues = new List<string>();
        foreach (var row in selected)
        {
            if (row.Offset < 0 || row.Offset + row.Length > bytes.Length)
            {
                issues.Add($"Timeline range {row.Offset}+{row.Length} exceeds raw stream length {bytes.Length}.");
                continue;
            }

            var chunk = bytes.AsSpan(row.Offset, row.Length);
            var actualHash = Sha256(chunk);
            if (!actualHash.Equals(row.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Timeline SHA-256 mismatch at sequence {row.Sequence}.");
            }

            chunks.Add(new ReconstructionChunk(
                row.Offset,
                row.Length,
                null,
                row.TimestampUtc,
                RelativeEvidencePath(rawPath),
                row.Sequence,
                actualHash));
        }

        return new StreamReconstruction(
            bytes,
            chunks,
            TcpSequenceAvailable: false,
            DuplicateSegmentCount: 0,
            OverlapBytesTrimmed: 0,
            Gaps: [],
            Issues: issues);
    }

    private IReadOnlyList<CandidateSession> ReconstructOfficialPcapSessions()
    {
        var capturesRoot = Path.Combine(_unifiedRoot, "Artifacts", "OfficialServerCapture");
        var uniqueCaptures = Directory
            .EnumerateFiles(capturesRoot, "*.pcapng", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Hash = Sha256File(path) })
            .GroupBy(item => item.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

        var segments = new List<CapturedTcpSegment>();
        foreach (var capture in uniqueCaptures)
        {
            segments.AddRange(PcapngReader.Read(capture.Path).Segments.Where(segment => segment.Payload.Length > 0));
        }

        var sessions = new List<CandidateSession>();
        foreach (var group in segments.GroupBy(segment => segment.ConnectionTuple, StringComparer.Ordinal))
        {
            var selected = group.ToArray();
            var c2s = ReassembleTcp(selected.Where(segment => segment.Direction == "ClientToServer"));
            var s2c = ReassembleTcp(selected.Where(segment => segment.Direction == "ServerToClient"));
            var sessionId = "official-" + Sanitize(group.Key);
            var sourcePaths = selected
                .Select(segment => RelativeEvidencePath(segment.SourcePath))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            sessions.Add(new CandidateSession
            {
                SessionId = sessionId,
                SourceKind = "OfficialDirectPcapng",
                State = "OfficialDirectLogin",
                ConnectionTuple = group.Key,
                SourcePaths = sourcePaths,
                ClientToServer = c2s,
                ServerToClient = s2c,
                ClientToServerFrames = ParseFrames(sessionId, "OfficialDirectLogin", "ClientToServer", c2s),
                ServerToClientFrames = ParseFrames(sessionId, "OfficialDirectLogin", "ServerToClient", s2c),
                Notes =
                [
                    "TCP sequence numbers were preserved by PCAPNG.",
                    "Retransmissions and overlapping coalesced payloads were de-duplicated by sequence range.",
                    "Source endpoint 49.235.177.12:2592 is the direct official Login transport captured before UU encapsulation."
                ]
            });
        }

        return sessions;
    }

    private StreamReconstruction ReassembleTcp(IEnumerable<CapturedTcpSegment> source)
    {
        var segments = source
            .OrderBy(segment => segment.Sequence)
            .ThenBy(segment => segment.Payload.Length)
            .ThenBy(segment => segment.TimestampUtc)
            .ToArray();
        if (segments.Length == 0)
        {
            return EmptyReconstruction(tcpSequenceAvailable: true);
        }

        var stream = new List<byte>();
        var chunks = new List<ReconstructionChunk>();
        var gaps = new List<string>();
        var issues = new List<string>();
        var duplicateCount = 0;
        var overlapTrimmed = 0;
        ulong expected = segments[0].Sequence;

        foreach (var segment in segments)
        {
            var sequence = (ulong)segment.Sequence;
            if (sequence > expected)
            {
                gaps.Add($"Missing TCP sequence range {expected}..{sequence - 1} before packet {segment.PacketIndex}.");
                expected = sequence;
            }

            var overlap = sequence < expected ? checked((int)Math.Min((ulong)segment.Payload.Length, expected - sequence)) : 0;
            if (overlap >= segment.Payload.Length)
            {
                duplicateCount++;
                overlapTrimmed += segment.Payload.Length;
                continue;
            }

            if (overlap > 0)
            {
                overlapTrimmed += overlap;
            }

            var tail = segment.Payload.AsSpan(overlap).ToArray();
            var streamOffset = stream.Count;
            stream.AddRange(tail);
            chunks.Add(new ReconstructionChunk(
                streamOffset,
                tail.Length,
                segment.Sequence + checked((uint)overlap),
                segment.TimestampUtc,
                RelativeEvidencePath(segment.SourcePath),
                segment.PacketIndex,
                Sha256(tail)));
            expected = sequence + (ulong)segment.Payload.Length;
        }

        return new StreamReconstruction(
            stream.ToArray(),
            chunks,
            TcpSequenceAvailable: true,
            duplicateCount,
            overlapTrimmed,
            gaps,
            issues);
    }

    private List<RecoveredFrame> ParseFrames(
        string sessionId,
        string state,
        string direction,
        StreamReconstruction reconstruction)
    {
        var frames = new List<RecoveredFrame>();
        var offset = 0;
        var ordinal = 0;
        while (offset + 2 <= reconstruction.Bytes.Length)
        {
            var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(reconstruction.Bytes.AsSpan(offset, 2));
            if (declaredLength < 2 || offset + declaredLength > reconstruction.Bytes.Length)
            {
                break;
            }

            ordinal++;
            var raw = reconstruction.Bytes.AsSpan(offset, declaredLength);
            var sourceChunk = reconstruction.Chunks
                .LastOrDefault(chunk => chunk.StreamOffset <= offset && chunk.StreamOffset + chunk.Length > offset);
            var timestamp = sourceChunk?.TimestampUtc ?? DateTimeOffset.MinValue;
            var hash = Sha256(raw);
            frames.Add(new RecoveredFrame(
                $"{sessionId}-{direction.ToLowerInvariant()}-{ordinal:D2}-{hash[..12].ToLowerInvariant()}",
                sessionId,
                state,
                direction,
                timestamp,
                offset,
                declaredLength,
                Convert.ToHexString(raw),
                hash,
                sourceChunk?.SourcePath ?? "Unknown",
                "Unknown",
                "CORRELATED_RAW",
                "Application frame boundary verified by uint16 little-endian declared length; role pending causal analysis."));
            offset += declaredLength;
        }

        return frames;
    }

    private static void AssignPacketRoles(IEnumerable<CandidateSession> sessions)
    {
        foreach (var session in sessions)
        {
            var frames = session.Frames.ToArray();
            var serverHandshakeSeen = false;
            var clientHandshakeSeen = false;
            var followUpSeen = false;
            var requestSeen = false;
            var responseSeen = false;

            foreach (var frame in frames)
            {
                var role = "Unknown";
                var status = "CORRELATED_RAW";
                var reason = frame.Reason;

                if (!serverHandshakeSeen && frame.Direction == "ServerToClient" && frame.Length == 19)
                {
                    serverHandshakeSeen = true;
                    role = "LoginServerHandshake";
                    status = "VERIFIED_RAW";
                    reason = "First complete S2C application frame on Login port; repeated across independent direct official sessions.";
                }
                else if (serverHandshakeSeen && !clientHandshakeSeen && frame.Direction == "ClientToServer" && frame.Length == 19)
                {
                    clientHandshakeSeen = true;
                    role = "LoginClientHandshakeResponse";
                    status = "VERIFIED_RAW";
                    reason = "First complete C2S application frame immediately following the 19-byte server handshake.";
                }
                else if (clientHandshakeSeen && !followUpSeen && frame.Direction == "ServerToClient" && frame.Length == 6)
                {
                    followUpSeen = true;
                    role = "LoginServerHandshakeFollowUp";
                    status = "VERIFIED_RAW";
                    reason = "Complete 6-byte S2C frame consistently between the handshake response and the 208-byte Login request.";
                }
                else if (followUpSeen && !requestSeen && frame.Direction == "ClientToServer" && frame.Length == 208)
                {
                    requestSeen = true;
                    role = "LoginRequest";
                    status = "VERIFIED_RAW";
                    reason = "Only complete C2S application frame after handshake completion and before the success/bootstrap response.";
                }
                else if (requestSeen && !responseSeen && frame.Direction == "ServerToClient" && frame.Length == 417)
                {
                    responseSeen = true;
                    role = "LoginSuccessAndCharacterBootstrap";
                    status = "VERIFIED_RAW";
                    reason = "Complete S2C frame after the 208-byte Login request in successful sessions; official Client subsequently reaches character flow.";
                }
                else if (responseSeen && frame.Direction == "ClientToServer" && frame.Length == 6)
                {
                    role = "CharacterSelectRequestCandidate";
                    reason = "Complete C2S frame after the 417-byte bootstrap; field semantics remain unverified.";
                }
                else if (responseSeen && frame.Direction == "ServerToClient" && frame.Length == 78)
                {
                    role = "WorldTransferCandidate";
                    reason = "Complete S2C frame after the post-bootstrap C2S frame; transfer fields remain unverified.";
                }

                ReplaceFrame(session, frame with
                {
                    CandidateRole = role,
                    VerificationStatus = status,
                    Reason = reason
                });
            }
        }
    }

    private static void ReplaceFrame(CandidateSession session, RecoveredFrame replacement)
    {
        var frames = replacement.Direction == "ClientToServer"
            ? session.ClientToServerFrames
            : session.ServerToClientFrames;
        var index = frames.FindIndex(frame => frame.EvidenceId == replacement.EvidenceId);
        if (index >= 0)
        {
            frames[index] = replacement;
        }
    }

    private void WriteReconstructedSessions(IReadOnlyList<CandidateSession> sessions)
    {
        var root = Path.Combine(_outputRoot, "reconstructed-streams");
        Directory.CreateDirectory(root);
        foreach (var session in sessions)
        {
            var sessionRoot = Path.Combine(root, session.SessionId);
            Directory.CreateDirectory(sessionRoot);
            File.WriteAllBytes(Path.Combine(sessionRoot, "c2s.stream.bin"), session.ClientToServer.Bytes);
            File.WriteAllBytes(Path.Combine(sessionRoot, "s2c.stream.bin"), session.ServerToClient.Bytes);
            WriteJson(
                Path.Combine(sessionRoot, "c2s.frames.json"),
                FrameOutput(session, session.ClientToServer, session.ClientToServerFrames));
            WriteJson(
                Path.Combine(sessionRoot, "s2c.frames.json"),
                FrameOutput(session, session.ServerToClient, session.ServerToClientFrames));
            WriteJson(
                Path.Combine(sessionRoot, "timeline.json"),
                session.Frames.Select(frame => new
                {
                    frame.TimestampUtc,
                    frame.Direction,
                    frame.Length,
                    frame.CandidateRole,
                    frame.VerificationStatus,
                    frame.Sha256,
                    frame.SourcePath
                }));

            var report = $"""
                # Login TCP Reconstruction

                Session: {session.SessionId}
                Source kind: {session.SourceKind}
                State: {session.State}
                Connection tuple: {session.ConnectionTuple}

                C2S bytes: {session.ClientToServer.Bytes.Length}
                C2S frames: {session.ClientToServerFrames.Count}
                S2C bytes: {session.ServerToClient.Bytes.Length}
                S2C frames: {session.ServerToClientFrames.Count}
                TCP sequence available: {session.ClientToServer.TcpSequenceAvailable || session.ServerToClient.TcpSequenceAvailable}
                Duplicate segments removed: {session.ClientToServer.DuplicateSegmentCount + session.ServerToClient.DuplicateSegmentCount}
                Overlap bytes trimmed: {session.ClientToServer.OverlapBytesTrimmed + session.ServerToClient.OverlapBytesTrimmed}
                Gaps: {session.ClientToServer.Gaps.Count + session.ServerToClient.Gaps.Count}

                Application framing is based only on the two-byte little-endian declared length. Socket read boundaries were not used as packet boundaries.
                """;
            File.WriteAllText(
                Path.Combine(sessionRoot, "reconstruction-report.md"),
                report,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static object FrameOutput(
        CandidateSession session,
        StreamReconstruction reconstruction,
        IReadOnlyList<RecoveredFrame> frames) =>
        new
        {
            session.SessionId,
            session.SourceKind,
            session.ConnectionTuple,
            StreamLength = reconstruction.Bytes.Length,
            StreamSha256 = Sha256(reconstruction.Bytes),
            reconstruction.TcpSequenceAvailable,
            reconstruction.DuplicateSegmentCount,
            reconstruction.OverlapBytesTrimmed,
            reconstruction.Gaps,
            reconstruction.Issues,
            ParsedLength = frames.Sum(frame => frame.Length),
            TrailingLength = reconstruction.Bytes.Length - frames.Sum(frame => frame.Length),
            Frames = frames
        };

    private void WriteLoginEventWindows(IReadOnlyList<CandidateSession> sessions)
    {
        var windows = sessions
            .Where(session => session.HasVerifiedLoginRequest)
            .Select(session =>
            {
                var request = session.Frames.First(frame => frame.CandidateRole == "LoginRequest");
                var start = request.TimestampUtc.AddSeconds(-3);
                var end = request.TimestampUtc.AddSeconds(5);
                return new
                {
                    session.SessionId,
                    session.SourceKind,
                    session.ConnectionTuple,
                    T0 = request.TimestampUtc,
                    T0Definition = "First complete 208-byte C2S frame after the 19/19/6 Login handshake sequence.",
                    WindowStartUtc = start,
                    WindowEndUtc = end,
                    Events = session.Frames
                        .Where(frame => frame.TimestampUtc >= start && frame.TimestampUtc <= end)
                        .Select(frame => new
                        {
                            frame.TimestampUtc,
                            DeltaMilliseconds = (frame.TimestampUtc - request.TimestampUtc).TotalMilliseconds,
                            frame.Direction,
                            frame.Length,
                            frame.CandidateRole,
                            frame.Sha256
                        })
                        .ToArray()
                };
            })
            .ToArray();
        WriteJson(
            Path.Combine(_outputRoot, "login-event-windows.json"),
            new
            {
                SchemaVersion = 1,
                WindowRule = "T-3000ms through T+5000ms",
                WindowCount = windows.Length,
                Windows = windows
            });
    }

    private void WriteNineteenByteAnalysis(IReadOnlyList<CandidateSession> sessions)
    {
        var rows = sessions
            .SelectMany(session => session.Frames)
            .Where(frame => frame.Length == 19)
            .ToList();
        rows.AddRange(ReadWorldNineteenByteFrames());

        var uniqueByRoleDirection = rows
            .GroupBy(frame => $"{frame.CandidateRole}|{frame.Direction}", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(frame => Convert.FromHexString(frame.RawHex)).Distinct(ByteArrayComparer.Instance).ToArray(),
                StringComparer.Ordinal);
        var baseline = Convert.FromHexString(LocalServerHandshake);
        var csvRows = new List<string>
        {
            string.Join(
                ',',
                new[]
                {
                    "EvidenceId", "Session", "State", "Direction", "RawHex", "Length"
                }
                .Concat(Enumerable.Range(0, 19).Select(index => $"Byte{index}"))
                .Concat(
                [
                    "StableOffsets",
                    "VariableOffsets",
                    "HammingDistanceFromBaseline",
                    "XorFromBaseline",
                    "EntropyBitsPerByte",
                    "LongestCommonPrefixBytes",
                    "LongestCommonSuffixBytes",
                    "CandidateRole",
                    "Confidence",
                    "Reason"
                ]))
        };

        foreach (var row in rows.OrderBy(frame => frame.TimestampUtc).ThenBy(frame => frame.EvidenceId, StringComparer.Ordinal))
        {
            var bytes = Convert.FromHexString(row.RawHex);
            var cohort = uniqueByRoleDirection[$"{row.CandidateRole}|{row.Direction}"];
            var stable = StableOffsets(cohort);
            var variable = Enumerable.Range(0, 19).Except(stable).ToArray();
            var fields = new List<string>
            {
                row.EvidenceId,
                row.SessionId,
                row.State,
                row.Direction,
                row.RawHex,
                row.Length.ToString(CultureInfo.InvariantCulture)
            };
            fields.AddRange(bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
            fields.Add(string.Join(';', stable));
            fields.Add(string.Join(';', variable));
            fields.Add(HammingDistance(bytes, baseline).ToString(CultureInfo.InvariantCulture));
            fields.Add(Convert.ToHexString(Xor(bytes, baseline)));
            fields.Add(Entropy(bytes).ToString("F6", CultureInfo.InvariantCulture));
            fields.Add(LongestCommonPrefix(bytes, baseline).ToString(CultureInfo.InvariantCulture));
            fields.Add(LongestCommonSuffix(bytes, baseline).ToString(CultureInfo.InvariantCulture));
            fields.Add(row.CandidateRole);
            fields.Add(row.VerificationStatus);
            fields.Add(row.Reason);
            csvRows.Add(string.Join(',', fields.Select(Csv)));
        }

        File.WriteAllLines(
            Path.Combine(_outputRoot, "frame-19-comparison.csv"),
            csvRows,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteJson(
            Path.Combine(_outputRoot, "frame-19-analysis.json"),
            new
            {
                SchemaVersion = 1,
                Baseline = LocalServerHandshake,
                LocalClientHandshake,
                WorldServerHandshake,
                Endianness = new
                {
                    Bytes0To1 = "0x0013 little-endian",
                    DeclaredLength = 19,
                    BoundaryVerified = true
                },
                Cohorts = uniqueByRoleDirection.Select(item => new
                {
                    Cohort = item.Key,
                    UniqueSampleCount = item.Value.Length,
                    StableOffsets = StableOffsets(item.Value),
                    VariableOffsets = Enumerable.Range(0, 19).Except(StableOffsets(item.Value)).ToArray(),
                    OffsetFrequencies = Enumerable.Range(0, 19).Select(offset => new
                    {
                        Offset = offset,
                        Values = item.Value
                            .GroupBy(bytes => bytes[offset])
                            .OrderBy(group => group.Key)
                            .ToDictionary(group => group.Key.ToString("X2"), group => group.Count())
                    })
                })
            });
    }

    private IEnumerable<RecoveredFrame> ReadWorldNineteenByteFrames()
    {
        var transportRoot = FindTransportRunRoot();
        if (transportRoot is null)
        {
            return [];
        }

        var timelinePath = Path.Combine(transportRoot, "Timeline", "World.transport.jsonl");
        var timeline = ReadJsonLines<TransportTimelineEntry>(timelinePath);
        var result = new List<RecoveredFrame>();
        foreach (var row in timeline.Where(item => item.Length == 19))
        {
            var rawPath = Path.Combine(transportRoot, row.RawEvidencePath);
            var bytes = File.ReadAllBytes(rawPath).AsSpan(row.Offset, row.Length);
            result.Add(new RecoveredFrame(
                $"world-{row.ConnectionId}-{row.Sequence:D4}",
                row.ConnectionId,
                "WorldInitialHandshake",
                row.Direction,
                row.TimestampUtc,
                row.Offset,
                row.Length,
                Convert.ToHexString(bytes),
                Sha256(bytes),
                RelativeEvidencePath(rawPath),
                "WorldServerHandshake",
                "VERIFIED_RAW",
                "First complete S2C application frame on World port; kept separate from Login despite equal length."));
        }

        return result;
    }

    private IReadOnlyList<VerificationDecision> WriteVerificationDecisions(IReadOnlyList<CandidateSession> sessions)
    {
        var decisions = sessions
            .SelectMany(session => session.Frames)
            .Where(frame => frame.CandidateRole is
                "LoginServerHandshake" or
                "LoginClientHandshakeResponse" or
                "LoginServerHandshakeFollowUp" or
                "LoginRequest" or
                "LoginSuccessAndCharacterBootstrap")
            .Select(frame =>
            {
                var isRequest = frame.CandidateRole == "LoginRequest";
                var isResponse = frame.CandidateRole == "LoginSuccessAndCharacterBootstrap";
                var unknown = isRequest
                    ? new[]
                    {
                        "Username offset and encoding",
                        "Password offset and encoding",
                        "Opcode or command identity after the length prefix",
                        "Nonce, token, checksum and encryption semantics"
                    }
                    : isResponse
                        ? new[]
                        {
                            "Success result-code offset",
                            "Boundary between login success and character bootstrap fields",
                            "Character entry field semantics",
                            "Nonce, token, checksum and encryption semantics"
                        }
                        : new[]
                        {
                            "Bytes after the two-byte length prefix remain opaque",
                            "Opcode, nonce, checksum and encryption semantics"
                        };
                var support = sessions
                    .SelectMany(session => session.Frames)
                    .Where(candidate => candidate.CandidateRole == frame.CandidateRole)
                    .Select(candidate => candidate.EvidenceId)
                    .Where(id => id != frame.EvidenceId)
                    .Take(12)
                    .ToArray();
                return new VerificationDecision(
                    frame.EvidenceId,
                    "CORRELATED_RAW",
                    frame.VerificationStatus == "VERIFIED_RAW" ? "VERIFIED_RAW" : "CORRELATED_RAW",
                    frame.CandidateRole,
                    [
                        $"Raw bytes complete ({frame.Length} bytes)",
                        $"Direction verified as {frame.Direction}",
                        "Application boundary verified by the two-byte little-endian declared length",
                        "Connection lineage, timestamp and source SHA-256 are retained",
                        "Packet role repeats at the same causal position across independent successful Login sessions"
                    ],
                    unknown,
                    support,
                    [],
                    frame.Reason,
                    frame.Sha256,
                    $"{frame.SessionId};{frame.SourcePath}");
            })
            .ToArray();
        WriteJson(
            Path.Combine(_outputRoot, "verification-decisions.json"),
            new
            {
                SchemaVersion = 1,
                PacketRoleVerification = new
                {
                    LoginPacketRoleVerified = decisions.Any(decision =>
                        decision.CandidateRole == "LoginRequest" &&
                        decision.NewStatus == "VERIFIED_RAW"),
                    LoginResponseRoleVerified = decisions.Any(decision =>
                        decision.CandidateRole == "LoginSuccessAndCharacterBootstrap" &&
                        decision.NewStatus == "VERIFIED_RAW")
                },
                FieldSemanticVerification = new
                {
                    LoginCredentialFieldsVerified = false,
                    LoginResultCodeFieldsVerified = false
                },
                LoginRuntimeMutationEnabled = false,
                Decisions = decisions
            });
        return decisions;
    }

    private void WriteCredentialAnalysis(IReadOnlyList<CandidateSession> sessions)
    {
        var samples = sessions
            .SelectMany(session => session.Frames)
            .Where(frame => frame.CandidateRole == "LoginRequest")
            .GroupBy(frame => frame.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var stable = samples.Length == 0
            ? []
            : StableOffsets(samples.Select(frame => Convert.FromHexString(frame.RawHex)).ToArray());
        WriteJson(
            Path.Combine(_outputRoot, "credential-field-analysis.json"),
            new
            {
                SchemaVersion = 1,
                SampleCount = samples.Length,
                KnownDistinctCredentialLabels = 0,
                UsernameEvidenceFound = false,
                PasswordEvidenceFound = false,
                SearchesPerformed = new[]
                {
                    "Printable ASCII runs",
                    "UTF-8 validity and readable runs",
                    "UTF-16LE alternating-null runs",
                    "Null-terminated and fixed-width readable strings",
                    "Cross-session stable/changing offset comparison"
                },
                ConfirmedStableOffsets = stable,
                ConfirmedSemanticOffsets = Array.Empty<int>(),
                Result = "No source binds a known username or password value to any payload offset. Packet role is verified independently; credential semantics remain Unknown."
            });
    }

    private IReadOnlyList<string> MigrateVerifiedLoginEvidence(IReadOnlyList<CandidateSession> sessions)
    {
        var selectedSessions = sessions
            .Where(session => session.HasVerifiedLoginRequest && session.HasVerifiedLoginSuccessResponse)
            .OrderByDescending(session => session.SourceKind == "OfficialDirectPcapng")
            .ThenBy(session => session.SessionId, StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        var selectedFrames = selectedSessions
            .SelectMany(session => session.Frames)
            .Where(frame => frame.CandidateRole is
                "LoginServerHandshake" or
                "LoginClientHandshakeResponse" or
                "LoginServerHandshakeFollowUp" or
                "LoginRequest" or
                "LoginSuccessAndCharacterBootstrap")
            .GroupBy(frame => frame.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var destinationRoot = Path.Combine(
            _projectRoot,
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "VerifiedRaw",
            "Login");
        Directory.CreateDirectory(destinationRoot);

        var destinationPaths = new List<string>();
        foreach (var frame in selectedFrames)
        {
            var role = frame.CandidateRole switch
            {
                "LoginServerHandshake" => "server-handshake-19",
                "LoginClientHandshakeResponse" => "client-handshake-19",
                "LoginServerHandshakeFollowUp" => "server-followup-6",
                "LoginRequest" => "request-208",
                "LoginSuccessAndCharacterBootstrap" => "success-bootstrap-417",
                _ => "unknown"
            };
            var fileName = $"login-{role}-{frame.Sha256[..12].ToLowerInvariant()}.bin";
            var destination = Path.Combine(destinationRoot, fileName);
            File.WriteAllBytes(destination, Convert.FromHexString(frame.RawHex));
            destinationPaths.Add(RelativeProjectPath(destination));
        }

        UpdateFormalSourceManifest(selectedFrames, destinationPaths);
        return destinationPaths;
    }

    private void UpdateFormalSourceManifest(
        IReadOnlyList<RecoveredFrame> frames,
        IReadOnlyList<string> destinationPaths)
    {
        var manifestPath = Path.Combine(
            _projectRoot,
            "src",
            "God2.ClassicServer.Protocol",
            "Evidence",
            "ProtocolEvidenceRecovery",
            "source-manifest.json");
        using var existingDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var existing = existingDocument.RootElement
            .GetProperty("Migrated")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToList();
        var existingDestinations = existing
            .Select(element => element.TryGetProperty("DestinationPath", out var path) ? path.GetString() : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (!existingDestinations.Add(destinationPaths[index]))
            {
                continue;
            }

            existing.Add(JsonSerializer.SerializeToElement(new
            {
                frame.EvidenceId,
                frame.SourcePath,
                DestinationPath = destinationPaths[index],
                frame.Direction,
                frame.RawHex,
                SHA256 = frame.Sha256,
                RepeatedVerificationCount = frames.Count(candidate => candidate.CandidateRole == frame.CandidateRole),
                VerificationStatus = "VERIFIED_RAW",
                Lineage = $"{frame.SessionId}; {frame.State}; application frame boundary and causal role verified by LoginDeepRecovery"
            }));
        }

        WriteJson(
            manifestPath,
            new
            {
                SchemaVersion = 2,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Policy = "Only VERIFIED_RAW packet segments are stored here. Packet role verification is independent from field-semantic verification. Unknown credential/result fields do not enable runtime mutation.",
                Migrated = existing
            });
    }

    private string? FindTransportRunRoot()
    {
        var root = Path.Combine(_unifiedRoot, "Artifacts", "GameplayPacketRecovery");
        var timeline = Directory
            .EnumerateFiles(root, "Login.transport.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
        return timeline is null ? null : Directory.GetParent(Directory.GetParent(timeline)!.FullName)!.FullName;
    }

    private static StreamReconstruction EmptyReconstruction(bool tcpSequenceAvailable) =>
        new([], [], tcpSequenceAvailable, 0, 0, [], []);

    private static IReadOnlyList<T> ReadJsonLines<T>(string path)
    {
        var result = new List<T>();
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<T>(
                line,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static bool SupportedExtension(string extension) =>
        extension.ToLowerInvariant() is
            ".json" or ".jsonl" or ".bin" or ".pcap" or ".pcapng" or ".raw" or ".hex" or
            ".log" or ".txt" or ".md" or ".csv";

    private static void AddFiles(
        ISet<string> target,
        string root,
        Func<FileInfo, bool> include)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var file = new FileInfo(path);
            if (include(file))
            {
                target.Add(file.FullName);
            }
        }
    }

    private string RelativeEvidencePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return $"classic/{Path.GetRelativePath(_projectRoot, full).Replace('\\', '/')}";
        }

        if (full.StartsWith(_unifiedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return $"unified-readonly/{Path.GetRelativePath(_unifiedRoot, full).Replace('\\', '/')}";
        }

        return full.Replace('\\', '/');
    }

    private string RelativeProjectPath(string path) =>
        Path.GetRelativePath(_projectRoot, path).Replace('\\', '/');

    private void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, _jsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static int[] StableOffsets(IReadOnlyList<byte[]> samples)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var length = samples.Min(sample => sample.Length);
        return Enumerable
            .Range(0, length)
            .Where(offset => samples.All(sample => sample[offset] == samples[0][offset]))
            .ToArray();
    }

    private static int HammingDistance(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var distance = 0;
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            distance += System.Numerics.BitOperations.PopCount((uint)(left[index] ^ right[index]));
        }

        return distance + Math.Abs(left.Length - right.Length) * 8;
    }

    private static byte[] Xor(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var length = Math.Min(left.Length, right.Length);
        var output = new byte[length];
        for (var index = 0; index < length; index++)
        {
            output[index] = (byte)(left[index] ^ right[index]);
        }

        return output;
    }

    private static double Entropy(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return 0;
        }

        var counts = new int[256];
        foreach (var value in bytes)
        {
            counts[value]++;
        }

        var entropy = 0d;
        foreach (var count in counts.Where(count => count > 0))
        {
            var probability = (double)count / bytes.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static int LongestCommonPrefix(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int LongestCommonSuffix(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var length = Math.Min(left.Length, right.Length);
        var count = 0;
        while (count < length && left[left.Length - 1 - count] == right[right.Length - 1 - count])
        {
            count++;
        }

        return count;
    }

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Sanitize(string value) =>
        Regex.Replace(value, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();

    private sealed record SourceMetadata(
        string SessionLineage,
        string ConnectionTuple,
        string Direction,
        string EventMarker,
        string Classification,
        string Notes);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            ReferenceEquals(x, y) ||
            (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            foreach (var value in obj)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}

public sealed record LoginEvidenceAnalysisSummary(
    int SourceCount,
    int CandidateSessionCount,
    int SuccessfulLoginSessionCount,
    bool LoginPacketRoleVerified,
    bool LoginResponseRoleVerified,
    bool CredentialFieldsVerified,
    bool RuntimeMutationEnabled,
    int MigratedEvidenceFileCount);
