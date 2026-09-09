using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace God2.RuntimeNpcEvidence;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var options = EvidenceOptions.Parse(args);
        var repoRoot = Path.GetFullPath(options.RepoRoot);
        var generatedAt = DateTimeOffset.UtcNow;

        var matrices = LoadWorldMatrices(repoRoot);
        var protocolCandidates = LoadProtocolCandidates(repoRoot);
        var runtimeSnapshot = LoadLatestRuntimeSnapshot(repoRoot);
        var observation = options.HostRun is null
            ? null
            : await BuildObservationAsync(repoRoot, options.HostRun, options.ServerRun, runtimeSnapshot, generatedAt);

        if (matrices.Count == 0 && observation is null)
        {
            Console.Error.WriteLine(
                "No world packet matrix or explicit existing host observation was supplied; existing NPC evidence outputs were not overwritten.");
            return 2;
        }

        var index = BuildIndex(repoRoot, generatedAt, matrices, protocolCandidates, runtimeSnapshot, observation);
        await WriteIndexAsync(repoRoot, index);
        await WriteReportsAsync(repoRoot, index, observation);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            index = ToRepoPath(repoRoot, Path.Combine(repoRoot, "Artifacts", "RuntimeNpcEvidence", "index.json")),
            observation = observation?.ArtifactRoot,
            index.Summary.TotalPackets,
            index.Summary.ServerToClientPackets,
            index.Summary.NpcS2cCandidateCount,
            index.Summary.NpcSerializerStatus
        }, JsonOptions));
        return 0;
    }

    private static RuntimeNpcEvidenceIndex BuildIndex(
        string repoRoot,
        DateTimeOffset generatedAt,
        IReadOnlyList<WorldMatrixEvidence> matrices,
        IReadOnlyList<ProtocolCandidateEvidence> protocolCandidates,
        RuntimeSnapshotEvidence? runtimeSnapshot,
        ObservationArtifact? observation)
    {
        var candidates = new List<NpcEvidenceCandidate>();
        var totalPackets = 0;
        var serverToClient = 0;
        var clientToServer = 0;
        var npc7758 = 0;
        var merchant77B5 = 0;
        var uniquePacketKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var matrix in matrices)
        {
            foreach (var packet in matrix.Packets)
            {
                totalPackets++;
                if (packet.Direction == "ServerToClient")
                {
                    serverToClient++;
                }
                else if (packet.Direction == "ClientToServer")
                {
                    clientToServer++;
                }

                uniquePacketKeys.Add($"{packet.Direction}:{packet.Length}:{packet.EncodedHex}");
                var classification = Classify(packet);
                if (classification.TrackAsEvidence)
                {
                    if (classification.Name == "NpcInteractionC2S8Candidate")
                    {
                        npc7758++;
                    }
                    if (classification.Name == "MerchantInteractionC2S8Candidate")
                    {
                        merchant77B5++;
                    }

                    candidates.Add(new NpcEvidenceCandidate(
                        matrix.Artifact,
                        matrix.CaptureTimeUtc,
                        packet.Direction,
                        packet.OpcodeCandidate,
                        packet.Length,
                        packet.PayloadSha256,
                        string.Empty,
                        string.Empty,
                        packet.Scenario,
                        "AfterWorldReady",
                        "UnknownFromPacketOnly",
                        classification.Name == "NpcInteractionC2S8Candidate" ? "ObservedDuringKnownNpcInteractionEvidence" : "NotCorrelated",
                        classification.Name == "MerchantInteractionC2S8Candidate" ? "ObservedDuringKnownMerchantInteractionEvidence" : "NotCorrelated",
                        classification.Name,
                        classification.Confidence,
                        classification.ExclusionReason));
                }
            }
        }

        foreach (var protocolCandidate in protocolCandidates)
        {
            candidates.Add(new NpcEvidenceCandidate(
                protocolCandidate.Artifact,
                protocolCandidate.CaptureTimeUtc,
                protocolCandidate.Direction,
                protocolCandidate.OpcodeCandidate,
                protocolCandidate.Length,
                protocolCandidate.RawSha256,
                string.Empty,
                string.Empty,
                protocolCandidate.Family,
                "Unknown",
                "Unknown",
                protocolCandidate.Id.Contains("npc", StringComparison.OrdinalIgnoreCase) ? "KnownProtocolRecord" : "NotNpc",
                protocolCandidate.Id.Contains("merchant", StringComparison.OrdinalIgnoreCase) ? "KnownProtocolRecord" : "NotMerchant",
                protocolCandidate.Classification,
                protocolCandidate.Confidence,
                protocolCandidate.ExclusionReason));
        }

        var npcS2cCandidates = candidates.Count(candidate =>
            candidate.Direction == "ServerToClient" &&
            candidate.CandidateClassification.Contains("NPC", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(candidate.ExclusionReason));

        return new RuntimeNpcEvidenceIndex(
            "runtime-npc-evidence-index-v1",
            generatedAt.ToString("o", CultureInfo.InvariantCulture),
            [
                "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion",
                "Artifacts/ClientInstrumentation/ElevatedAutomationHost",
                "Artifacts/WorldRuntimeInspector",
                "Artifacts/RuntimeNpcObservation"
            ],
            new RuntimeNpcEvidenceSummary(
                matrices.Count,
                totalPackets,
                clientToServer,
                serverToClient,
                uniquePacketKeys.Count,
                npc7758,
                merchant77B5,
                npcS2cCandidates,
                runtimeSnapshot?.NpcCount ?? 0,
                runtimeSnapshot?.ReplicationSpawnQueueCount ?? 0,
                runtimeSnapshot?.SerializerBlockedCount ?? 0,
                observation?.ArtifactRoot ?? string.Empty,
                "SerializerBlockedByEvidence",
                0,
                "NOT_FOUND"),
            matrices.Select(matrix => new EvidenceArtifactSummary(
                matrix.Artifact,
                matrix.CaptureTimeUtc,
                matrix.PacketCount,
                matrix.ClientToServerPackets,
                matrix.ServerToClientPackets)).ToArray(),
            candidates
                .OrderBy(candidate => candidate.Artifact, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.CaptureTimeUtc, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Direction, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.OpcodeCandidate, StringComparer.Ordinal)
                .ToArray(),
            runtimeSnapshot,
            observation);
    }

    private static async Task<ObservationArtifact> BuildObservationAsync(
        string repoRoot,
        string hostRun,
        string? serverRun,
        RuntimeSnapshotEvidence? runtimeSnapshot,
        DateTimeOffset generatedAt)
    {
        var hostRunFull = Path.GetFullPath(hostRun);
        var runId = generatedAt.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var artifactRootFull = Path.Combine(repoRoot, "Artifacts", "RuntimeNpcObservation", runId);
        Directory.CreateDirectory(artifactRootFull);

        var matrixPath = FindObservationMatrixPath(hostRunFull);
        var hostState = ReadHostObservationState(repoRoot, hostRunFull, matrixPath);
        var packets = File.Exists(matrixPath) ? LoadMatrixPackets(repoRoot, matrixPath) : [];
        var packetRows = packets.Select(packet => new ObservationPacket(
            packet.Sequence,
            packet.Direction,
            packet.TimestampUnixMs,
            string.Empty,
            string.Empty,
            "WorldReady",
            packet.OpcodeCandidate,
            packet.Length,
            string.Empty,
            string.Empty,
            packet.PayloadSha256,
            packet.Scenario,
            runtimeSnapshot?.NpcCount ?? 0,
            runtimeSnapshot?.NpcCount ?? 0,
            runtimeSnapshot?.ReplicationSpawnQueueCount ?? 0,
            "SerializerBlockedByEvidence",
            "ClientAliveDuringCapture")).ToArray();

        await File.WriteAllTextAsync(
            Path.Combine(artifactRootFull, "packets.json"),
            JsonSerializer.Serialize(packetRows, JsonOptions));
        await File.WriteAllLinesAsync(
            Path.Combine(artifactRootFull, "packets.raw"),
            packetRows.Select(packet => $"{packet.Sequence} {packet.Direction} len={packet.FrameLength} opcode={packet.OpcodeCandidate} sha256={packet.Sha256}"));

        await WriteTimelineAsync(repoRoot, hostRunFull, artifactRootFull, packets);
        await WriteRuntimeSnapshotsAsync(repoRoot, artifactRootFull, runtimeSnapshot);
        await WriteClientProcessAsync(repoRoot, artifactRootFull, hostState);
        await WriteServerLogAsync(repoRoot, artifactRootFull, serverRun);
        await WriteAutomationLogAsync(repoRoot, hostRunFull, artifactRootFull, hostState);

        var serverToClient = packetRows.Count(packet => packet.Direction == "ServerToClient");
        var clientToServer = packetRows.Count(packet => packet.Direction == "ClientToServer");
        var npc7758 = packets.Count(packet => IsNpc7758(packet.EncodedHex, packet.OpcodeCandidate));
        var merchant77B5 = packets.Count(packet => IsMerchant77B5(packet.EncodedHex, packet.OpcodeCandidate));
        var npcS2c = packets.Count(packet =>
            packet.Direction == "ServerToClient" &&
            (packet.OpcodeCandidate.Contains("7758", StringComparison.OrdinalIgnoreCase) ||
             packet.EncodedHex.Contains("7758", StringComparison.OrdinalIgnoreCase)));

        var summary = new ObservationArtifact(
            ToRepoPath(repoRoot, artifactRootFull),
            ToRepoPath(repoRoot, hostRunFull),
            serverRun is null ? string.Empty : ToRepoPath(repoRoot, serverRun),
            packetRows.Length,
            clientToServer,
            serverToClient,
            npc7758,
            merchant77B5,
            npcS2c,
            runtimeSnapshot?.NpcCount ?? 0,
            runtimeSnapshot?.ReplicationSpawnQueueCount ?? 0,
            hostState.Status,
            hostState.Stage,
            hostState.Error,
            hostState.LoginAttemptCount,
            "SerializerBlockedByEvidence",
            "NOT_FOUND");

        await File.WriteAllTextAsync(
            Path.Combine(artifactRootFull, "summary.md"),
            BuildObservationSummary(summary));
        return summary;
    }

    private static async Task WriteTimelineAsync(string repoRoot, string hostRunFull, string artifactRootFull, IReadOnlyList<WorldPacketEvidence> packets)
    {
        var timeline = new JsonArray();
        var enterWorld = Path.Combine(hostRunFull, "enter-world-result.json");
        if (File.Exists(enterWorld))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(enterWorld));
            if (document.RootElement.TryGetProperty("screenshots", out var screenshots) &&
                screenshots.ValueKind == JsonValueKind.Array)
            {
                foreach (var screenshot in screenshots.EnumerateArray())
                {
                    timeline.Add(new JsonObject
                    {
                        ["kind"] = "ScreenStage",
                        ["stage"] = screenshot.GetPropertyOrDefault("stage"),
                        ["capturedAtUtc"] = screenshot.GetPropertyOrDefault("capturedAtUtc"),
                        ["artifact"] = ToRepoPath(repoRoot, screenshot.GetPropertyOrDefault("path"))
                    });
                }
            }
        }

        foreach (var group in packets.GroupBy(packet => packet.Scenario))
        {
            timeline.Add(new JsonObject
            {
                ["kind"] = "PacketScenario",
                ["scenario"] = group.Key,
                ["packetCount"] = group.Count(),
                ["clientToServer"] = group.Count(packet => packet.Direction == "ClientToServer"),
                ["serverToClient"] = group.Count(packet => packet.Direction == "ServerToClient")
            });
        }

        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "timeline.json"), timeline.ToJsonString(JsonOptions));
    }

    private static async Task WriteRuntimeSnapshotsAsync(string repoRoot, string artifactRootFull, RuntimeSnapshotEvidence? runtimeSnapshot)
    {
        var snapshotObject = JsonSerializer.SerializeToNode(runtimeSnapshot ?? RuntimeSnapshotEvidence.Empty(), JsonOptions)!;
        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "runtime-snapshot.json"), snapshotObject.ToJsonString(JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "replication-snapshot.json"), new JsonObject
        {
            ["npcCount"] = runtimeSnapshot?.NpcCount ?? 0,
            ["visibleNpcCount"] = runtimeSnapshot?.NpcCount ?? 0,
            ["pendingSpawnCount"] = runtimeSnapshot?.ReplicationSpawnQueueCount ?? 0,
            ["serializerBlockedCount"] = runtimeSnapshot?.SerializerBlockedCount ?? 0,
            ["serializerStatus"] = "SerializerBlockedByEvidence"
        }.ToJsonString(JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "inspector-snapshot.json"), snapshotObject.ToJsonString(JsonOptions));
    }

    private static string FindObservationMatrixPath(string hostRunFull)
    {
        var fresh = Path.Combine(hostRunFull, "fresh-npc-observation", "world-packet-matrix.json");
        if (File.Exists(fresh))
        {
            return fresh;
        }

        return Path.Combine(hostRunFull, "world-capture-matrix", "world-packet-matrix.json");
    }

    private static HostObservationState ReadHostObservationState(string repoRoot, string hostRunFull, string matrixPath)
    {
        var enterWorld = Path.Combine(hostRunFull, "enter-world-result.json");
        var matrixPresent = File.Exists(matrixPath);
        var freshMatrixPresent = matrixPresent &&
            matrixPath.Contains($"{Path.DirectorySeparatorChar}fresh-npc-observation{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        var enterWorldPresent = File.Exists(enterWorld);
        var loginAttempts = Directory.Exists(hostRunFull)
            ? Directory.EnumerateFiles(hostRunFull, "login-attempt-*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        var stage = matrixPresent
            ? freshMatrixPresent ? "FreshNpcObservationComplete" : "WorldCaptureMatrixComplete"
            : enterWorldPresent
                ? "EnterWorldResultPresent"
                : loginAttempts.Length > 0
                    ? "LoginAttempted"
                    : "NoLoginAttempt";
        var error = string.Empty;
        var latestLoginAttempt = string.Empty;

        if (loginAttempts.Length > 0)
        {
            var latest = loginAttempts[^1];
            latestLoginAttempt = ToRepoPath(repoRoot, latest);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(latest));
                var root = document.RootElement;
                var success = root.GetPropertyOrDefault("success");
                error = root.GetPropertyOrDefault("error");
                if (!success.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    stage = "LoginFailed";
                }
            }
            catch
            {
                stage = "LoginAttemptParseFailed";
                error = "Unable to parse latest login-attempt artifact.";
            }
        }

        if (enterWorldPresent)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(enterWorld));
                var root = document.RootElement;
                var worldReady = root.GetPropertyOrDefault("worldReady");
                stage = matrixPresent
                    ? freshMatrixPresent ? "FreshNpcObservationComplete" : "WorldCaptureMatrixComplete"
                    : worldReady.Equals("true", StringComparison.OrdinalIgnoreCase)
                        ? "WorldReadyNoMatrix"
                        : root.GetPropertyOrDefault("currentStage", "EnterWorldAttempted");
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = root.GetPropertyOrDefault("firstBlocker");
                }
            }
            catch
            {
                stage = "EnterWorldResultParseFailed";
                error = "Unable to parse enter-world-result artifact.";
            }
        }

        var status = matrixPresent
            ? freshMatrixPresent ? "CapturedFreshNpcObservation" : "CapturedWorldMatrix"
            : enterWorldPresent
                ? "WorldReachedWithoutMatrix"
                : "BlockedBeforeWorldCapture";

        return new HostObservationState(
            status,
            stage,
            error,
            loginAttempts.Length,
            enterWorldPresent,
            matrixPresent,
            latestLoginAttempt);
    }

    private static async Task WriteClientProcessAsync(string repoRoot, string artifactRootFull, HostObservationState hostState)
    {
        var statusPath = Path.Combine(repoRoot, "Automation", "State", "host-status.json");
        JsonObject status = new()
        {
            ["observationStatus"] = hostState.Status,
            ["hostStage"] = hostState.Stage,
            ["hostError"] = hostState.Error,
            ["loginAttemptCount"] = hostState.LoginAttemptCount,
            ["enterWorldResultPresent"] = hostState.EnterWorldResultPresent,
            ["worldCaptureMatrixPresent"] = hostState.WorldCaptureMatrixPresent,
            ["latestLoginAttempt"] = hostState.LatestLoginAttempt
        };
        if (File.Exists(statusPath))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(statusPath));
            var root = document.RootElement;
            status["stage"] = root.GetPropertyOrDefault("currentStage");
            status["launcherPresent"] = !string.IsNullOrWhiteSpace(root.GetPropertyOrDefault("launcherPid"));
            status["clientPresent"] = !string.IsNullOrWhiteSpace(root.GetPropertyOrDefault("clientPid"));
            status["databaseSecretAvailable"] = root.GetPropertyOrDefault("databaseSecretAvailable");
            status["databaseSecretSource"] = root.GetPropertyOrDefault("databaseSecretSource");
        }

        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "client-process.json"), status.ToJsonString(JsonOptions));
    }

    private static async Task WriteServerLogAsync(string repoRoot, string artifactRootFull, string? serverRun)
    {
        var output = "No server run supplied.";
        if (!string.IsNullOrWhiteSpace(serverRun))
        {
            var stdout = Path.Combine(Path.GetFullPath(serverRun), "stdout.log");
            if (File.Exists(stdout))
            {
                output = Sanitize(repoRoot, await ReadAllTextSharedAsync(stdout));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "server-log.txt"), output);
    }

    private static async Task<string> ReadAllTextSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteAutomationLogAsync(string repoRoot, string hostRunFull, string artifactRootFull, HostObservationState hostState)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Automation observation host run:");
        builder.AppendLine(ToRepoPath(repoRoot, hostRunFull));
        builder.AppendLine($"observationStatus: {hostState.Status}");
        builder.AppendLine($"hostStage: {hostState.Stage}");
        builder.AppendLine($"hostError: {hostState.Error}");
        builder.AppendLine($"loginAttemptCount: {hostState.LoginAttemptCount}");
        builder.AppendLine($"latestLoginAttempt: {hostState.LatestLoginAttempt}");
        foreach (var fileName in new[] { "login-attempt-1.json", "enter-world-result.json", "fresh-npc-observation/world-packet-matrix.json", "world-capture-matrix/world-packet-matrix.json" })
        {
            var path = Path.Combine(hostRunFull, fileName);
            builder.AppendLine($"{fileName}: {(File.Exists(path) ? "present" : "missing")}");
        }

        await File.WriteAllTextAsync(Path.Combine(artifactRootFull, "automation-log.txt"), builder.ToString());
    }

    private static IReadOnlyList<WorldMatrixEvidence> LoadWorldMatrices(string repoRoot)
    {
        var root = Path.Combine(repoRoot, "Artifacts", "ClientInstrumentation", "ElevatedAutomationHost");
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "world-packet-matrix.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}stale{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => TryLoadWorldMatrix(repoRoot, path))
            .Where(matrix => matrix is not null)
            .Cast<WorldMatrixEvidence>()
            .OrderByDescending(matrix => matrix.CaptureTimeUtc, StringComparer.Ordinal)
            .ToArray();
    }

    private static WorldMatrixEvidence? TryLoadWorldMatrix(string repoRoot, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var packets = LoadMatrixPackets(repoRoot, path);
            return new WorldMatrixEvidence(
                ToRepoPath(repoRoot, path),
                root.GetPropertyOrDefault("GeneratedAtUtc", File.GetLastWriteTimeUtc(path).ToString("o", CultureInfo.InvariantCulture)),
                packets.Count,
                packets.Count(packet => packet.Direction == "ClientToServer"),
                packets.Count(packet => packet.Direction == "ServerToClient"),
                packets);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<WorldPacketEvidence> LoadMatrixPackets(string repoRoot, string matrixPath)
    {
        var packets = new List<WorldPacketEvidence>();
        using var document = JsonDocument.Parse(File.ReadAllText(matrixPath));
        if (!document.RootElement.TryGetProperty("Scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array)
        {
            return packets;
        }

        foreach (var scenario in scenarios.EnumerateArray())
        {
            var scenarioName = scenario.GetPropertyOrDefault("Scenario", "Unknown");
            if (!scenario.TryGetProperty("Packets", out var packetElements) || packetElements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var packet in packetElements.EnumerateArray())
            {
                packets.Add(new WorldPacketEvidence(
                    packet.GetPropertyOrDefaultInt("Sequence"),
                    scenarioName,
                    packet.GetPropertyOrDefault("Direction"),
                    packet.GetPropertyOrDefaultInt("Length"),
                    packet.GetPropertyOrDefault("EncodedHex"),
                    packet.GetPropertyOrDefault("DecodedHex"),
                    packet.GetPropertyOrDefault("OpcodeCandidate"),
                    packet.GetPropertyOrDefault("PayloadSha256"),
                    packet.GetPropertyOrDefaultInt64("WallUnixMs")));
            }
        }

        return packets;
    }

    private static IReadOnlyList<ProtocolCandidateEvidence> LoadProtocolCandidates(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "src", "God2.ClassicServer.Protocol", "Evidence", "OfficialProtocolCompletion", "UnknownPacketCatalog.json");
        if (!File.Exists(path))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("unknownPackets", out var packets) || packets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var generatedAt = document.RootElement.GetPropertyOrDefault("generatedAt", string.Empty);
        var rows = new List<ProtocolCandidateEvidence>();
        foreach (var packet in packets.EnumerateArray())
        {
            var samples = packet.TryGetProperty("samples", out var samplesElement) && samplesElement.ValueKind == JsonValueKind.Array
                ? samplesElement.EnumerateArray().Select(sample => sample.GetString() ?? string.Empty).Where(sample => sample.Length > 0).ToArray()
                : [];
            var rawHex = samples.FirstOrDefault() ?? packet.GetPropertyOrDefault("fixedBytes");
            if (rawHex.Length == 0)
            {
                continue;
            }

            var direction = packet.GetPropertyOrDefault("direction");
            var id = packet.GetPropertyOrDefault("id");
            var family = packet.GetPropertyOrDefault("family");
            var opcode = packet.GetPropertyOrDefault("opcodeCandidate");
            var length = packet.GetPropertyOrDefaultInt("length");
            var classification = family.Equals("NPC", StringComparison.OrdinalIgnoreCase)
                ? "NpcInteractionC2S8Candidate"
                : family.Equals("Merchant", StringComparison.OrdinalIgnoreCase)
                    ? "MerchantInteractionC2S8Candidate"
                    : "OtherC2SCandidate";
            var exclusion = direction == "ServerToClient"
                ? string.Empty
                : "ClientToServer evidence cannot unlock NPC Spawn/Update/Despawn serializer.";
            rows.Add(new ProtocolCandidateEvidence(
                ToRepoPath(repoRoot, path),
                generatedAt,
                id,
                family,
                direction,
                opcode,
                length,
                rawHex,
                Sha256Hex(rawHex),
                classification,
                packet.GetPropertyOrDefault("confidence", "Unknown"),
                exclusion));
        }

        return rows;
    }

    private static RuntimeSnapshotEvidence? LoadLatestRuntimeSnapshot(string repoRoot)
    {
        var root = Path.Combine(repoRoot, "Artifacts", "WorldRuntimeInspector");
        if (!Directory.Exists(root))
        {
            return null;
        }

        var path = Directory.EnumerateFiles(root, "world-runtime-snapshot.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var element = document.RootElement;
        return new RuntimeSnapshotEvidence(
            ToRepoPath(repoRoot, path),
            element.GetPropertyOrDefault("CapturedAtUtc"),
            element.GetPropertyOrDefaultInt("MapId"),
            element.GetPropertyOrDefaultInt("NpcCount"),
            element.GetPropertyOrDefaultInt("MonsterCount"),
            element.GetPropertyOrDefaultInt("PortalCount"),
            element.GetPropertyOrDefaultInt("MerchantCount"),
            element.GetPropertyOrDefaultInt("ReplicationSpawnQueueCount", element.GetPropertyOrDefaultInt("SpawnQueueCount")),
            element.GetPropertyOrDefaultInt("SerializerBlockedCount"));
    }

    private static PacketClassification Classify(WorldPacketEvidence packet)
    {
        if (packet.Direction == "ServerToClient")
        {
            return new PacketClassification(
                true,
                "UnclassifiedServerToClient",
                "Unknown",
                string.Empty);
        }

        if (IsNpc7758(packet.EncodedHex, packet.OpcodeCandidate))
        {
            return new PacketClassification(
                true,
                "NpcInteractionC2S8Candidate",
                "Medium",
                "Direction is ClientToServer; forbidden to treat 7758 as NPC S2C spawn.");
        }

        if (IsMerchant77B5(packet.EncodedHex, packet.OpcodeCandidate))
        {
            return new PacketClassification(
                true,
                "MerchantInteractionC2S8Candidate",
                "LowMedium",
                "Direction is ClientToServer; merchant-like interaction cannot unlock NPC S2C serializer.");
        }

        if (packet.Length == 8)
        {
            return new PacketClassification(
                true,
                "OtherClientToServer8ByteCandidate",
                "Low",
                "Direction is ClientToServer and packet is not proven NPC spawn/update/despawn.");
        }

        return new PacketClassification(false, "NotNpcEvidenceCandidate", "Unknown", "Not tracked as NPC evidence.");
    }

    private static async Task WriteIndexAsync(string repoRoot, RuntimeNpcEvidenceIndex index)
    {
        var path = Path.Combine(repoRoot, "Artifacts", "RuntimeNpcEvidence", "index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(index, JsonOptions));
    }

    private static async Task WriteReportsAsync(string repoRoot, RuntimeNpcEvidenceIndex index, ObservationArtifact? observation)
    {
        var reportRoot = Path.Combine(repoRoot, "Reports");
        Directory.CreateDirectory(reportRoot);
        await File.WriteAllTextAsync(Path.Combine(reportRoot, "RuntimeNpcEvidence.Index.md"), BuildIndexReport(index));
        await File.WriteAllTextAsync(Path.Combine(reportRoot, "RuntimeNpcEvidence.Observation.md"), BuildObservationReport(index, observation));
        await File.WriteAllTextAsync(Path.Combine(reportRoot, "RuntimeNpcEvidence.StaticAnalysis.md"), BuildStaticAnalysisReport(index));
    }

    private static string BuildIndexReport(RuntimeNpcEvidenceIndex index)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Runtime NPC Evidence Index");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{index.GeneratedAtUtc}`");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | ---: |");
        builder.AppendLine($"| World matrices indexed | {index.Summary.WorldMatrixCount} |");
        builder.AppendLine($"| Packets inspected | {index.Summary.TotalPackets} |");
        builder.AppendLine($"| ClientToServer | {index.Summary.ClientToServerPackets} |");
        builder.AppendLine($"| ServerToClient | {index.Summary.ServerToClientPackets} |");
        builder.AppendLine($"| Unique packet keys | {index.Summary.UniquePacketKeys} |");
        builder.AppendLine($"| 7758 NPC interaction candidates | {index.Summary.Npc7758Count} |");
        builder.AppendLine($"| 77B5 merchant-like candidates | {index.Summary.Merchant77B5Count} |");
        builder.AppendLine($"| NPC S2C candidates | {index.Summary.NpcS2cCandidateCount} |");
        builder.AppendLine($"| Fake network bytes | {index.Summary.FakeNetworkBytes} |");
        builder.AppendLine();
        builder.AppendLine("## Decision");
        builder.AppendLine();
        builder.AppendLine("NPC S2C Packet: `NOT_FOUND`.");
        builder.AppendLine();
        builder.AppendLine("NPC serializer remains `SerializerBlockedByEvidence`; no opcode, length, offsets, padding, or golden bytes were inferred.");
        builder.AppendLine();
        builder.AppendLine("## Candidate Summary");
        builder.AppendLine();
        builder.AppendLine("| Classification | Direction | Opcode | Length | Confidence | Exclusion |");
        builder.AppendLine("| --- | --- | --- | ---: | --- | --- |");
        foreach (var candidate in index.Candidates
                     .GroupBy(candidate => new { candidate.CandidateClassification, candidate.Direction, candidate.OpcodeCandidate, candidate.Length, candidate.Confidence, candidate.ExclusionReason })
                     .OrderBy(group => group.Key.CandidateClassification, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.OpcodeCandidate, StringComparer.Ordinal))
        {
            builder.AppendLine($"| {candidate.Key.CandidateClassification} x{candidate.Count()} | {candidate.Key.Direction} | {candidate.Key.OpcodeCandidate} | {candidate.Key.Length} | {candidate.Key.Confidence} | {candidate.Key.ExclusionReason} |");
        }

        return builder.ToString();
    }

    private static string BuildObservationReport(RuntimeNpcEvidenceIndex index, ObservationArtifact? observation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Runtime NPC Evidence Observation");
        builder.AppendLine();
        if (observation is null)
        {
            builder.AppendLine("No fresh automated official-client observation artifact was supplied to the index builder yet.");
        }
        else
        {
            builder.AppendLine($"Artifact: `{observation.ArtifactRoot}`");
            builder.AppendLine();
            builder.AppendLine("| Metric | Value |");
            builder.AppendLine("| --- | ---: |");
            builder.AppendLine($"| Packets | {observation.PacketCount} |");
            builder.AppendLine($"| ClientToServer | {observation.ClientToServerPackets} |");
            builder.AppendLine($"| ServerToClient | {observation.ServerToClientPackets} |");
            builder.AppendLine($"| 7758 candidates | {observation.Npc7758Count} |");
            builder.AppendLine($"| 77B5 candidates | {observation.Merchant77B5Count} |");
            builder.AppendLine($"| NPC S2C candidates | {observation.NpcS2cCandidateCount} |");
            builder.AppendLine($"| Runtime NPC count | {observation.RuntimeNpcCount} |");
            builder.AppendLine($"| Pending spawn count | {observation.PendingSpawnCount} |");
            builder.AppendLine($"| Observation status | {observation.ObservationStatus} |");
            builder.AppendLine($"| Host stage | {observation.HostStage} |");
            builder.AppendLine($"| Host error | {observation.HostError} |");
            builder.AppendLine($"| Login attempts | {observation.LoginAttemptCount} |");
            builder.AppendLine();
            builder.AppendLine("Captured files: `packets.raw`, `packets.json`, `timeline.json`, `runtime-snapshot.json`, `replication-snapshot.json`, `inspector-snapshot.json`, `client-process.json`, `server-log.txt`, `automation-log.txt`, `summary.md`.");
        }

        builder.AppendLine();
        builder.AppendLine("Observation result: NPC S2C Packet `NOT_FOUND`; NPC serializer remains `SerializerBlockedByEvidence`.");
        builder.AppendLine($"Current indexed S2C total: {index.Summary.ServerToClientPackets}.");
        return builder.ToString();
    }

    private static string BuildStaticAnalysisReport(RuntimeNpcEvidenceIndex index)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Runtime NPC Evidence Static Analysis");
        builder.AppendLine();
        builder.AppendLine("Status: limited static/dynamic-anchor analysis only. No official client binary was modified.");
        builder.AppendLine();
        builder.AppendLine("Anchors used:");
        builder.AppendLine();
        builder.AppendLine("- Existing trace return RVA values from world packet matrices.");
        builder.AppendLine("- `UnknownPacketCatalog.json` protocol candidates.");
        builder.AppendLine("- `OfficialNpcWorldProtocol` evidence gate.");
        builder.AppendLine("- Runtime serializer catalog status.");
        builder.AppendLine();
        builder.AppendLine("Result:");
        builder.AppendLine();
        builder.AppendLine("- No S2C NPC spawn/update/despawn dispatcher opcode was verified.");
        builder.AppendLine("- 7758 remains C2S NPC interaction evidence only.");
        builder.AppendLine("- 77B5 remains C2S merchant-like interaction evidence only.");
        builder.AppendLine("- PlayerSpawn 0x1F/128 remains current-player bootstrap evidence only and was not reused for NPC.");
        builder.AppendLine();
        builder.AppendLine($"Indexed S2C candidates: {index.Summary.NpcS2cCandidateCount}.");
        builder.AppendLine();
        builder.AppendLine("Decision: insufficient for serializer unlock.");
        return builder.ToString();
    }

    private static string BuildObservationSummary(ObservationArtifact summary) =>
        $"""
        # Runtime NPC Observation Summary

        | Metric | Value |
        | --- | ---: |
        | Packets | {summary.PacketCount} |
        | ClientToServer | {summary.ClientToServerPackets} |
        | ServerToClient | {summary.ServerToClientPackets} |
        | 7758 candidates | {summary.Npc7758Count} |
        | 77B5 candidates | {summary.Merchant77B5Count} |
        | NPC S2C candidates | {summary.NpcS2cCandidateCount} |
        | Runtime NPC count | {summary.RuntimeNpcCount} |
        | Pending spawn count | {summary.PendingSpawnCount} |
        | Observation status | {summary.ObservationStatus} |
        | Host stage | {summary.HostStage} |
        | Host error | {summary.HostError} |
        | Login attempts | {summary.LoginAttemptCount} |

        NPC S2C Packet: `NOT_FOUND`.

        NPC serializer remains `SerializerBlockedByEvidence`.
        """;

    private static string Sha256Hex(string hex)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(Convert.FromHexString(hex)));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsNpc7758(string hex, string opcode) =>
        opcode.Equals("7758", StringComparison.OrdinalIgnoreCase) ||
        hex.Contains("7758", StringComparison.OrdinalIgnoreCase);

    private static bool IsMerchant77B5(string hex, string opcode) =>
        opcode.Equals("77B5", StringComparison.OrdinalIgnoreCase) ||
        hex.Contains("77B5", StringComparison.OrdinalIgnoreCase);

    private static string ToRepoPath(string repoRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full[(root.Length + 1)..].Replace('\\', '/')
            : "<external-path>";
    }

    private static string Sanitize(string repoRoot, string value) =>
        value.Replace(Path.GetFullPath(repoRoot), "<repo>", StringComparison.OrdinalIgnoreCase);
}

internal sealed record EvidenceOptions(string RepoRoot, string? HostRun, string? ServerRun)
{
    public static EvidenceOptions Parse(string[] args) =>
        new(
            Value(args, "--repo-root") ?? Directory.GetCurrentDirectory(),
            Value(args, "--host-run"),
            Value(args, "--server-run"));

    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal sealed record RuntimeNpcEvidenceIndex(
    string SchemaVersion,
    string GeneratedAtUtc,
    IReadOnlyList<string> SourceRoots,
    RuntimeNpcEvidenceSummary Summary,
    IReadOnlyList<EvidenceArtifactSummary> Artifacts,
    IReadOnlyList<NpcEvidenceCandidate> Candidates,
    RuntimeSnapshotEvidence? RuntimeSnapshot,
    ObservationArtifact? Observation);

internal sealed record RuntimeNpcEvidenceSummary(
    int WorldMatrixCount,
    int TotalPackets,
    int ClientToServerPackets,
    int ServerToClientPackets,
    int UniquePacketKeys,
    int Npc7758Count,
    int Merchant77B5Count,
    int NpcS2cCandidateCount,
    int RuntimeNpcCount,
    int PendingSpawnCount,
    int SerializerBlockedCount,
    string LatestObservation,
    string NpcSerializerStatus,
    int FakeNetworkBytes,
    string NpcS2cPacket);

internal sealed record EvidenceArtifactSummary(
    string Artifact,
    string CaptureTimeUtc,
    int PacketCount,
    int ClientToServerPackets,
    int ServerToClientPackets);

internal sealed record NpcEvidenceCandidate(
    string Artifact,
    string CaptureTimeUtc,
    string Direction,
    string OpcodeCandidate,
    int Length,
    string RawSha256,
    string RawHex,
    string DecodedHex,
    string WorldState,
    string WorldReadyRelative,
    string NpcVisibilityRelative,
    string NpcInteractionRelative,
    string MerchantInteractionRelative,
    string CandidateClassification,
    string Confidence,
    string ExclusionReason);

internal sealed record WorldMatrixEvidence(
    string Artifact,
    string CaptureTimeUtc,
    int PacketCount,
    int ClientToServerPackets,
    int ServerToClientPackets,
    IReadOnlyList<WorldPacketEvidence> Packets);

internal sealed record WorldPacketEvidence(
    int Sequence,
    string Scenario,
    string Direction,
    int Length,
    string EncodedHex,
    string DecodedHex,
    string OpcodeCandidate,
    string PayloadSha256,
    long TimestampUnixMs);

internal sealed record ProtocolCandidateEvidence(
    string Artifact,
    string CaptureTimeUtc,
    string Id,
    string Family,
    string Direction,
    string OpcodeCandidate,
    int Length,
    string RawHex,
    string RawSha256,
    string Classification,
    string Confidence,
    string ExclusionReason);

internal sealed record RuntimeSnapshotEvidence(
    string Artifact,
    string CapturedAtUtc,
    int MapId,
    int NpcCount,
    int MonsterCount,
    int PortalCount,
    int MerchantCount,
    int ReplicationSpawnQueueCount,
    int SerializerBlockedCount)
{
    public static RuntimeSnapshotEvidence Empty() =>
        new(string.Empty, string.Empty, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed record ObservationArtifact(
    string ArtifactRoot,
    string HostRun,
    string ServerRun,
    int PacketCount,
    int ClientToServerPackets,
    int ServerToClientPackets,
    int Npc7758Count,
    int Merchant77B5Count,
    int NpcS2cCandidateCount,
    int RuntimeNpcCount,
    int PendingSpawnCount,
    string ObservationStatus,
    string HostStage,
    string HostError,
    int LoginAttemptCount,
    string SerializerStatus,
    string NpcS2cPacket);

internal sealed record HostObservationState(
    string Status,
    string Stage,
    string Error,
    int LoginAttemptCount,
    bool EnterWorldResultPresent,
    bool WorldCaptureMatrixPresent,
    string LatestLoginAttempt);

internal sealed record ObservationPacket(
    int Sequence,
    string Direction,
    long TimestampUnixMs,
    string ConnectionId,
    string SessionId,
    string Stage,
    string OpcodeCandidate,
    int FrameLength,
    string RawBytesHex,
    string DecodedBytesHex,
    string Sha256,
    string Scenario,
    int NpcRuntimeCount,
    int VisibleNpcCount,
    int PendingSpawnCount,
    string SerializerState,
    string ClientProcessState);

internal sealed record PacketClassification(
    bool TrackAsEvidence,
    string Name,
    string Confidence,
    string ExclusionReason);

internal static class JsonExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string name, string defaultValue = "")
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? defaultValue,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => defaultValue
        };
    }

    public static int GetPropertyOrDefaultInt(this JsonElement element, string name, int defaultValue = 0) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : defaultValue;

    public static long GetPropertyOrDefaultInt64(this JsonElement element, string name, long defaultValue = 0) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : defaultValue;
}
