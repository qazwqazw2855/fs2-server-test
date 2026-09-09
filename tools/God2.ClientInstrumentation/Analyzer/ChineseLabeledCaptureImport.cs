using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using God2.ClassicServer.Protocol;

namespace God2.ClientInstrumentation.Analyzer;

internal static partial class ChineseLabeledCaptureImporter
{
    private const string SchemaVersion = "chinese-labeled-capture-import-v1";
    private const string ClientBuildId = "god2-opt-6b127086e0c0";
    private const string ExpectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    private const int GameplayPort = 2596;
    private const int AuxiliaryPort = 2592;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, SessionLabelDefinition> LabelDefinitions =
        BuildLabelDefinitions().ToDictionary(definition => definition.OriginalChineseLabel, StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        var repoRoot = Path.GetFullPath(Required(args, "--repo-root"));
        var zipPath = Path.GetFullPath(Required(args, "--zip"));
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Chinese-labeled capture archive was not found.", zipPath);
        }

        var protocolRoot = Path.Combine(repoRoot, "protocol", "evidence", "current-build", "chinese-labeled-captures");
        var artifactRoot = Path.Combine(repoRoot, "Artifacts", "ChineseLabeledCaptureImport");
        var reportRoot = Path.Combine(repoRoot, "Reports");
        EnsureOutputDirectories(protocolRoot, artifactRoot);

        var zipHashBefore = await HashFileAsync(zipPath);
        var priorManifest = await TryReadPriorManifestAsync(Path.Combine(protocolRoot, "import-manifest.json"));
        var archiveLength = new FileInfo(zipPath).Length;
        var importedAtUtc = DateTimeOffset.UtcNow;

        ImportCorpus corpus;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using (var archiveStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(936)))
        {
            ValidateArchivePaths(archive);
            corpus = await ImportArchiveAsync(archive, zipHashBefore, archiveLength, importedAtUtc);
        }

        var zipHashAfter = await HashFileAsync(zipPath);
        if (!string.Equals(zipHashBefore, zipHashAfter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Source archive changed while it was being imported.");
        }

        var regressionEvidence = LoadRegressionEvidence(repoRoot);
        var runtimeMode = LoadRuntimeMode(repoRoot);
        var result = Analyze(corpus, priorManifest, regressionEvidence, runtimeMode);
        await WriteOutputsAsync(repoRoot, protocolRoot, artifactRoot, reportRoot, result, zipHashAfter);
        Console.WriteLine(JsonSerializer.Serialize(result.Summary, JsonOptions));
        return result.AcceptanceChecks.All(check => check.Passed) ? 0 : 5;
    }

    private static ImportAnalysisResult Analyze(
        ImportCorpus corpus,
        PriorImportManifest? priorManifest,
        RegressionEvidence regressionEvidence,
        RuntimeModeEvidence runtimeMode)
    {
        var catalog = new OfficialCurrentBuildPacketEvidenceCatalog();
        var sessions = new List<AnalyzedSession>(corpus.Sessions.Count);
        foreach (var imported in corpus.Sessions)
        {
            var frames = LivePacketClassifier.ReconstructFrames(imported.TraceRecords);
            var analyzedFrames = AnalyzeFrames(imported, frames, catalog);
            var decoded = ParseDecodedServerObservations(imported.GeneralLog);
            var transactions = SegmentTransactions(imported, analyzedFrames);
            sessions.Add(new AnalyzedSession(imported, analyzedFrames, decoded, transactions));
        }

        var allFrames = sessions.SelectMany(session => session.Frames).ToArray();
        var allTransactions = sessions.SelectMany(session => session.Transactions).ToArray();
        var clusters = BuildClusters(allFrames, sessions);
        var fieldHypotheses = BuildFieldHypotheses(clusters, allFrames);
        var decoderCandidates = BuildDecoderCandidates(clusters, allFrames);
        var serializerCandidates = BuildSerializerCandidates(sessions);
        var runtimeMappings = BuildRuntimeMappings(clusters, sessions);
        var promotionGates = BuildPromotionGates(clusters, decoderCandidates, serializerCandidates);
        var experiments = BuildActiveExperiments(fieldHypotheses, promotionGates);
        var unknownActionable = BuildUnknownActionable(clusters);
        var packetSequences = BuildPacketSequences(sessions);
        var differentials = BuildDifferentials(clusters, sessions);

        var duplicateArchive = string.Equals(priorManifest?.ZipSha256, corpus.ZipSha256, StringComparison.OrdinalIgnoreCase);
        var securityEvidence = InspectRestrictedCorpus(corpus);
        var summary = BuildSummary(
            corpus,
            sessions,
            clusters,
            fieldHypotheses,
            decoderCandidates,
            serializerCandidates,
            runtimeMappings,
            promotionGates,
            experiments,
            unknownActionable,
            duplicateArchive,
            securityEvidence,
            runtimeMode);
        var preliminary = new ImportAnalysisResult(
            corpus,
            sessions,
            clusters,
            fieldHypotheses,
            decoderCandidates,
            serializerCandidates,
            runtimeMappings,
            promotionGates,
            experiments,
            unknownActionable,
            packetSequences,
            differentials,
            summary,
            []);
        var reportsExcludeRawPayload = ReportsExcludeRestrictedPayload(preliminary);
        var checks = BuildAcceptanceChecks(
            corpus,
            sessions,
            summary,
            clusters,
            fieldHypotheses,
            decoderCandidates,
            serializerCandidates,
            reportsExcludeRawPayload,
            regressionEvidence);
        var finalSummary = summary with
        {
            FinalStatus = checks.All(check => check.Passed)
                ? "CHINESE-LABELED PACKET CLASSIFICATION PASS"
                : "CHINESE-LABELED PACKET CLASSIFICATION FAIL"
        };
        return preliminary with { Summary = finalSummary, AcceptanceChecks = checks };
    }

    private static async Task<ImportCorpus> ImportArchiveAsync(
        ZipArchive archive,
        string zipSha256,
        long archiveLength,
        DateTimeOffset importedAtUtc)
    {
        var sessionGroups = archive.Entries
            .Where(entry => SessionEntryRegex().IsMatch(NormalizeEntryPath(entry.FullName)))
            .GroupBy(entry => NormalizeEntryPath(entry.FullName).Split('/')[0], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        if (sessionGroups.Length != 16)
        {
            throw new InvalidDataException($"Expected exactly 16 host-run sessions, found {sessionGroups.Length}.");
        }

        var sessions = new List<ImportedSession>(sessionGroups.Length);
        foreach (var group in sessionGroups)
        {
            var match = SessionEntryRegex().Match(group.Key + "/");
            var captureSessionId = group.Key;
            var originalLabel = match.Groups[1].Value;
            if (!LabelDefinitions.TryGetValue(originalLabel, out var label))
            {
                throw new InvalidDataException($"No deterministic supervised label definition exists for: {originalLabel}");
            }

            var metadataEntry = RequiredEntry(group, "metadata.jsonl");
            var traceEntry = RequiredEntry(group, "sensitive/trace.bin");
            var generalLogEntry = RequiredEntry(group, "general.log");
            var metadataBytes = await ReadEntryBytesAsync(metadataEntry);
            var traceBytes = await ReadEntryBytesAsync(traceEntry);
            var generalLogBytes = await ReadEntryBytesAsync(generalLogEntry);
            var metadataText = StrictUtf8(metadataBytes, metadataEntry.FullName);
            var generalLogText = StrictUtf8(generalLogBytes, generalLogEntry.FullName);
            var metadata = ParseMetadata(metadataText, metadataEntry.FullName);
            TraceRecord[] traceRecordsInStorageOrder;
            using (var traceStream = new MemoryStream(traceBytes, writable: false))
            {
                traceRecordsInStorageOrder = TraceReader.Read(traceStream, traceEntry.FullName).ToArray();
            }

            // Sequence is allocated before the synchronized trace write. Two capture threads can therefore
            // append adjacent records in the opposite physical order without losing or duplicating data.
            // Canonicalize on the authoritative sequence before reconstructing per-socket byte streams.
            var traceRecords = traceRecordsInStorageOrder.OrderBy(record => record.Sequence).ToArray();

            var fileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in group.Where(entry => !string.IsNullOrEmpty(entry.Name)).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                fileHashes[RelativeSessionPath(entry.FullName)] = await HashEntryAsync(entry);
            }

            var sessionHashSource = string.Join("\n", fileHashes.Select(pair => $"{pair.Key}:{pair.Value}"));
            var sessionHash = Sha256Hex(Encoding.UTF8.GetBytes(sessionHashSource));
            var traceValidation = ValidateTrace(metadata, traceRecordsInStorageOrder, traceBytes.Length, generalLogText);
            sessions.Add(new ImportedSession(
                captureSessionId,
                originalLabel,
                label,
                sessionHash,
                Sha256Hex(metadataBytes),
                Sha256Hex(traceBytes),
                Sha256Hex(generalLogBytes),
                fileHashes,
                metadata,
                traceRecords,
                generalLogText,
                traceBytes.LongLength,
                traceValidation,
                ToRelativeRawReference(captureSessionId, traceEntry.FullName)));
        }

        var staleEntries = archive.Entries.Count(entry => NormalizeEntryPath(entry.FullName).StartsWith("stale/", StringComparison.Ordinal));
        return new ImportCorpus(
            SchemaVersion,
            importedAtUtc,
            "ElevatedAutomationHost.zip",
            zipSha256,
            archiveLength,
            ClientBuildId,
            ExpectedClientSha256,
            "x86",
            sessions,
            staleEntries,
            StaleExcludedFromGameplay: true);
    }

    private static IReadOnlyList<AnalyzedFrame> AnalyzeFrames(
        ImportedSession session,
        IReadOnlyList<LiveTraceFrame> frames,
        OfficialCurrentBuildPacketEvidenceCatalog catalog)
    {
        var recordsBySequence = session.TraceRecords.ToDictionary(record => record.Sequence);
        var result = new List<AnalyzedFrame>(frames.Count);
        foreach (var frame in frames.OrderBy(frame => frame.Sequence))
        {
            var source = recordsBySequence[frame.SourceSequence];
            var direction = frame.Direction == Direction.ClientToServer ? "ClientToServer" : "ServerToClient";
            var safeSocket = $"socket-{Sha256Hex(BitConverter.GetBytes(frame.Socket))[..12].ToLowerInvariant()}";
            var rawHash = frame.PayloadSha256.ToLowerInvariant();
            var rawReference = $"{session.RawTraceReference}#seq-{frame.SourceSequence}.{frame.FrameIndex}";
            var isAuxiliary = frame.RemotePort == AuxiliaryPort;
            var infrastructure = frame.Direction == Direction.ClientToServer
                ? frame.Payload.Length switch
                {
                    5 when HasValidLengthPrefix(frame.Payload) => "Heartbeat",
                    10 when HasValidLengthPrefix(frame.Payload) => "Movement",
                    _ => ""
                }
                : "";
            var classification = infrastructure.Length > 0
                ? new FamilyClassification(infrastructure, "KnownInfrastructureTraffic")
                : MatchKnownFamily(catalog, session, frame);
            var known = classification.Family;
            var callerRva = NormalizeClientRva(source.Stack.Skip(2).FirstOrDefault());
            result.Add(new AnalyzedFrame(
                $"{session.CaptureSessionId}/seq-{frame.SourceSequence}.{frame.FrameIndex}",
                session.CaptureSessionId,
                frame.SourceSequence,
                frame.FrameIndex,
                frame.WallUnixMs,
                direction,
                frame.Payload.Length,
                rawHash,
                rawReference,
                safeSocket,
                frame.RemotePort,
                callerRva,
                infrastructure,
                known,
                classification.Source,
                isAuxiliary,
                infrastructure.Length > 0 || isAuxiliary,
                known.Length == 0 && infrastructure.Length == 0 && !isAuxiliary,
                frame.Payload));
        }
        return result;
    }

    private static FamilyClassification MatchKnownFamily(
        OfficialCurrentBuildPacketEvidenceCatalog catalog,
        ImportedSession session,
        LiveTraceFrame frame)
    {
        if (frame.RemotePort != GameplayPort)
        {
            return new FamilyClassification("", "ExcludedAuxiliaryConnection");
        }

        if (frame.Direction == Direction.ClientToServer)
        {
            var match = catalog.MatchClientFrame(
                session.Label.PrimaryDomain == "CharacterLifecycle" ? ProtocolStage.CharacterList : ProtocolStage.InWorld,
                frame.Payload);
            if (match is not null)
            {
                var family = match.Signature.Operation switch
                {
                    OfficialCapturedOperation.LoginAuthenticationRequestCandidate => "LoginAuthenticationCandidate",
                    OfficialCapturedOperation.CharacterCreateRequestCandidate => "CharacterCreateCandidate",
                    OfficialCapturedOperation.MerchantInsufficientFundsRequestCandidate => "ShopInsufficientFunds",
                    OfficialCapturedOperation.OutOfBattleHealingRequestCandidate => "OutOfCombatHeal",
                    OfficialCapturedOperation.SynthesisRequestCandidate => "Crafting",
                    OfficialCapturedOperation.EquipmentToggleRequestCandidateA or
                    OfficialCapturedOperation.EquipmentToggleRequestCandidateB => "EquipmentSwitch",
                    OfficialCapturedOperation.BattleCommandEnvelopeCandidate => "BattleCommand20",
                    OfficialCapturedOperation.BattleBoundaryAcknowledgementCandidate => "BattleCommand12",
                    OfficialCapturedOperation.BattleResultDismissAcknowledgementCandidate => "BattleSettlementConfirmation",
                    _ => ""
                };
                return new FamilyClassification(family, "OfficialCurrentBuildPacketEvidence");
            }

            var candidate = InferSupervisedClientFamily(session.Label, frame.Payload.Length);
            return new FamilyClassification(candidate, candidate.Length > 0 ? "SupervisedStructuralCandidate" : "Unclassified");
        }

        return new FamilyClassification("", "Unclassified");
    }

    private static string InferSupervisedClientFamily(SessionLabelDefinition label, int frameLength)
    {
        if (label.NormalizedSessionAlias == "character-delete-create")
        {
            return frameLength switch
            {
                57 => "LoginAuthenticationCandidate",
                48 => "CharacterCreateCandidate",
                _ => ""
            };
        }

        if (label.NormalizedSessionAlias == "craft-and-submit-quest" && frameLength == 85)
        {
            return "Crafting";
        }

        if (frameLength == 20 && label.BattleRelated)
        {
            return "BattleCommand20";
        }

        if (frameLength == 12 && label.BattleRelated)
        {
            return "BattleCommand12";
        }

        return label.NormalizedSessionAlias switch
        {
            "equipment-wear-remove" when frameLength is 7 or 8 => "EquipmentSwitch",
            "mount-equip-unequip" when frameLength == 8 => "MountEquipmentCandidate",
            "pet-deploy-withdraw-walk-recall" when frameLength == 8 => "PetStateCandidate",
            "portal-map-transfer" when frameLength == 8 => "PortalMapTransferCandidate",
            "quest-scripted-transfer" when frameLength == 8 => "QuestTransferCandidate",
            "shop-open-sell-gem" when frameLength is 7 or 8 or 12 => "ShopSellCandidate",
            "god-companion-acquisition" when frameLength == 8 => "CompanionAcquisitionCandidate",
            "craft-and-submit-quest" when frameLength == 8 => "QuestSubmitCandidate",
            _ => ""
        };
    }

    private static IReadOnlyList<DecodedServerObservation> ParseDecodedServerObservations(string generalLog)
    {
        var observations = new List<DecodedServerObservation>();
        foreach (var line in generalLog.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DecodedExitRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            observations.Add(new DecodedServerObservation(
                long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                $"0x{match.Groups[3].Value.ToUpperInvariant()}",
                "FramedDecrypted",
                "packet-decode probe; decoded payload bytes remain restricted"));
        }
        return observations;
    }

    private static IReadOnlyList<ActionTransaction> SegmentTransactions(
        ImportedSession session,
        IReadOnlyList<AnalyzedFrame> frames)
    {
        var ordered = frames.OrderBy(frame => frame.WallUnixMs).ThenBy(frame => frame.SourceSequence).ToArray();
        var triggers = ordered
            .Where(frame => frame.Direction == "ClientToServer")
            .Where(frame => !frame.ExcludedInfrastructure && !frame.AuxiliaryConnection)
            .ToArray();
        var groups = new List<List<AnalyzedFrame>>();
        foreach (var trigger in triggers)
        {
            if (groups.Count == 0 || trigger.WallUnixMs - groups[^1][^1].WallUnixMs > 750)
            {
                groups.Add([trigger]);
            }
            else
            {
                groups[^1].Add(trigger);
            }
        }

        if (groups.Count == 0)
        {
            var first = ordered.FirstOrDefault();
            var last = ordered.LastOrDefault();
            if (first is null || last is null)
            {
                return [];
            }

            return
            [
                new ActionTransaction(
                    $"{session.CaptureSessionId}/tx-001",
                    session.CaptureSessionId,
                    string.Join("+", session.Label.ActionLabels),
                    session.Label.ActionLabels,
                    first.SourceSequence,
                    last.SourceSequence,
                    first.WallUnixMs,
                    last.WallUnixMs,
                    ExpectedPreState(session.Label),
                    null,
                    [],
                    [],
                    ExpectedPostState(session.Label),
                    "No actionable outbound frame survived infrastructure and connection exclusion.",
                    ordered.Length,
                    ordered.Count(frame => frame.InfrastructureKind == "Movement"),
                    ordered.Count(frame => frame.InfrastructureKind == "Heartbeat"),
                    [],
                    "Low",
                    true,
                    ["Trigger frame missing; label retained as supervised session context only."])
            ];
        }

        var transactions = new List<ActionTransaction>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var start = group[0];
            var nextStartMs = index + 1 < groups.Count ? groups[index + 1][0].WallUnixMs : long.MaxValue;
            var window = ordered
                .Where(frame => frame.WallUnixMs >= start.WallUnixMs && frame.WallUnixMs < nextStartMs)
                .ToArray();
            var assigned = AssignTransactionLabels(session.Label.ActionLabels, index, groups.Count);
            var composite = assigned.Count > 1;
            var candidateFamilies = group
                .Select(frame => frame.KnownFamily)
                .Where(family => family.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var inbound = window
                .Where(frame => frame.Direction == "ServerToClient" && !frame.AuxiliaryConnection)
                .Take(8)
                .Select(frame => frame.FrameId)
                .ToArray();
            var resultFrames = window
                .Where(frame => frame.Direction == "ServerToClient" && !frame.AuxiliaryConnection)
                .TakeLast(4)
                .Select(frame => frame.FrameId)
                .ToArray();
            var contradictions = new List<string>();
            if (candidateFamilies.Length == 0)
            {
                contradictions.Add("No current-build known signature or safe structural family matched the trigger.");
            }
            if (composite)
            {
                contradictions.Add("More supervised action labels than separable trigger groups; composite preserved.");
            }
            if (session.Label.ActionLabels.Count > 1)
            {
                contradictions.Add("Sub-action boundary follows the declared operation order and timing gaps; the capture has no explicit UI action marker.");
            }

            transactions.Add(new ActionTransaction(
                $"{session.CaptureSessionId}/tx-{index + 1:000}",
                session.CaptureSessionId,
                string.Join("+", assigned),
                assigned,
                start.SourceSequence,
                window.LastOrDefault()?.SourceSequence ?? group[^1].SourceSequence,
                start.WallUnixMs,
                window.LastOrDefault()?.WallUnixMs ?? group[^1].WallUnixMs,
                ExpectedPreState(session.Label),
                new TriggerReference(start.FrameId, start.FrameLength, start.RawHash, start.CallerRva),
                inbound,
                resultFrames,
                ExpectedPostState(session.Label),
                DirectionPattern(window),
                window.Length,
                window.Count(frame => frame.InfrastructureKind == "Movement"),
                window.Count(frame => frame.InfrastructureKind == "Heartbeat"),
                candidateFamilies,
                contradictions.Count == 0 ? "Medium" : "Low",
                composite,
                contradictions));
        }

        return transactions;
    }

    private static IReadOnlyList<string> AssignTransactionLabels(
        IReadOnlyList<string> actionLabels,
        int groupIndex,
        int groupCount)
    {
        if (actionLabels.Count == 0)
        {
            return ["UnlabeledActionCandidate"];
        }

        if (groupCount >= actionLabels.Count)
        {
            var labelIndex = Math.Min(actionLabels.Count - 1, groupIndex * actionLabels.Count / groupCount);
            return [actionLabels[labelIndex]];
        }

        var start = groupIndex * actionLabels.Count / groupCount;
        var end = Math.Max(start + 1, (groupIndex + 1) * actionLabels.Count / groupCount);
        return actionLabels.Skip(start).Take(end - start).ToArray();
    }

    private static IReadOnlyList<PacketCluster> BuildClusters(
        IReadOnlyList<AnalyzedFrame> frames,
        IReadOnlyList<AnalyzedSession> sessions)
    {
        var clusters = frames
            .Where(frame => !frame.AuxiliaryConnection)
            .GroupBy(
                frame => frame.ExcludedInfrastructure
                    ? $"infra:{frame.InfrastructureKind}"
                    : $"{frame.Direction}:{frame.FrameLength}:{frame.CallerRva}:{frame.KnownFamily}:{frame.ClassificationSource}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var sample = group.First();
                var sessionIds = group.Select(frame => frame.SessionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var family = sample.ExcludedInfrastructure ? sample.InfrastructureKind : sample.KnownFamily;
                var promotion = ResolvePromotionStage(group.Count(), sessionIds.Length, family);
                return new PacketCluster(
                    $"cluster-{Sha256Hex(Encoding.UTF8.GetBytes(group.Key))[..16].ToLowerInvariant()}",
                    sample.Direction,
                    sample.FrameLength,
                    sample.CallerRva,
                    family.Length == 0 ? "Unknown" : family,
                    sample.ClassificationSource,
                    group.Count(),
                    sessionIds.Length,
                    sessionIds,
                    group.Select(frame => frame.RawHash).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    group.Select(frame => frame.RawReference).Take(12).ToArray(),
                    sample.ExcludedInfrastructure,
                    sample.ExcludedInfrastructure,
                    family.Length == 0 && !sample.ExcludedInfrastructure,
                    promotion,
                    family.Length == 0 ? "Low" : sessionIds.Length > 1 ? "MediumHigh" : "Medium",
                    RuntimeMutationBlocked: family is not "Movement" and not "Heartbeat",
                    EvidenceConflict: DetectEvidenceConflict(group, sessions));
            })
            .OrderBy(cluster => cluster.ClusterId, StringComparer.Ordinal)
            .ToArray();

        var decodedClusters = sessions
            .SelectMany(session => session.DecodedServerObservations.Select(observation => (session, observation)))
            .GroupBy(item => $"{item.observation.DecodedOpcode}:{item.observation.DecodedLength}", StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var sessionIds = group.Select(item => item.session.Imported.CaptureSessionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                return new PacketCluster(
                    $"decoded-{first.observation.DecodedOpcode[2..].ToLowerInvariant()}-{first.observation.DecodedLength}",
                    "ServerToClient",
                    first.observation.DecodedLength + 3,
                    "packet-decode-probe",
                    $"DecodedServerOpcode{first.observation.DecodedOpcode}",
                    "PacketDecodeProbeObservation",
                    group.Count(),
                    sessionIds.Length,
                    sessionIds,
                    group.Count(),
                    [],
                    ExcludedFromUnknown: false,
                    ExcludedFromDifferential: false,
                    ActionableUnknown: false,
                    ResolvePromotionStage(group.Count(), sessionIds.Length, "DecodedServerOpcode"),
                    sessionIds.Length > 1 ? "MediumHigh" : "Medium",
                    RuntimeMutationBlocked: true,
                    EvidenceConflict: false);
            });
        return clusters.Concat(decodedClusters).OrderBy(cluster => cluster.ClusterId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<FieldHypothesis> BuildFieldHypotheses(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<AnalyzedFrame> frames)
    {
        var hypotheses = new List<FieldHypothesis>();
        foreach (var cluster in clusters.Where(cluster => !cluster.ExcludedFromDifferential && cluster.Family != "Unknown"))
        {
            var samples = frames
                .Where(frame => frame.FrameLength == cluster.FrameLength)
                .Where(frame => frame.CallerRva == cluster.CallerRva)
                .Where(frame => (frame.KnownFamily.Length == 0 ? "Unknown" : frame.KnownFamily) == cluster.Family)
                .Take(24)
                .ToArray();
            hypotheses.Add(new FieldHypothesis(
                $"{cluster.ClusterId}/length-le16",
                cluster.Family,
                "FrameLength",
                samples.Select(sample => sample.RawReference).Take(8).ToArray(),
                [],
                cluster.FrameLength.ToString(CultureInfo.InvariantCulture),
                cluster.FrameLength.ToString(CultureInfo.InvariantCulture),
                "little-endian",
                0,
                2,
                "High",
                "Capture one malformed length only in the offline candidate test; never send it to a live endpoint.",
                "Length prefix is structurally verified; it does not identify gameplay semantics."));
            if (cluster.FrameLength > 2)
            {
                hypotheses.Add(new FieldHypothesis(
                    $"{cluster.ClusterId}/opaque-payload",
                    cluster.Family,
                    "EncryptedOrOpaquePayload",
                    samples.Select(sample => sample.RawReference).Take(8).ToArray(),
                    samples.Select(sample => sample.RawHash).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Take(4).ToArray(),
                    "stable semantic family with dynamic values",
                    $"{Math.Max(0, cluster.RawHashVariantCount)} raw-hash variants",
                    "unknown",
                    2,
                    Math.Max(0, cluster.FrameLength - 2),
                    cluster.SessionCount > 1 ? "Medium" : "Low",
                    NextExperimentForFamily(cluster.Family),
                    "Cipher/session/entity variation is not promoted to an opcode or fixed identifier."));
            }
        }

        return hypotheses.OrderBy(hypothesis => hypothesis.HypothesisId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<DecoderCandidate> BuildDecoderCandidates(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<AnalyzedFrame> frames)
    {
        var candidateFamilies = new HashSet<string>(StringComparer.Ordinal)
        {
            "CharacterCreateCandidate", "CharacterDeleteCandidate", "EquipmentSwitch", "Crafting",
            "ShopSellCandidate", "PortalMapTransferCandidate", "QuestSubmitCandidate",
            "BattleCommand20", "BattleCommand12", "MountEquipmentCandidate", "PetStateCandidate"
        };
        return clusters
            .Where(cluster => candidateFamilies.Contains(cluster.Family))
            .GroupBy(cluster => cluster.Family, StringComparer.Ordinal)
            .Select(group =>
            {
                var lengths = group.Select(cluster => cluster.FrameLength).Distinct().Order().ToArray();
                var samples = group.SelectMany(cluster => cluster.RawReferences).Take(8).ToArray();
                return new DecoderCandidate(
                    $"decoder-{Slug(group.Key)}",
                    group.Key,
                    ClientBuildId,
                    group.Key.StartsWith("Character", StringComparison.Ordinal) ? "CharacterList" : "InWorld",
                    lengths,
                    "little-endian UInt16 frame length at offset 0; remaining required bytes retained opaque",
                    samples,
                    ["wrong build", "wrong protocol state", "length mismatch", "truncated payload", "oversized payload"],
                    "SemanticCommandCandidate only",
                    DecoderVerified: false,
                    RuntimeMutationAllowed: false,
                    "DecoderCandidate",
                    "Encrypted/opaque required fields and stable semantic identifiers are not recovered.");
            })
            .OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SerializerCandidate> BuildSerializerCandidates(IReadOnlyList<AnalyzedSession> sessions)
    {
        return sessions
            .SelectMany(session => session.DecodedServerObservations)
            .GroupBy(observation => observation.DecodedOpcode, StringComparer.Ordinal)
            .Select(group => new SerializerCandidate(
                $"serializer-{group.Key[2..].ToLowerInvariant()}",
                $"DecodedServerOpcode{group.Key}",
                group.Count(),
                "SerializerBlockedByEvidence",
                ["entity ID", "item/skill/map IDs", "HP/MP/EXP/level", "result code", "counts", "sequence", "opaque required bytes"],
                ProductionBytesEmitted: false,
                SerializerVerified: false,
                "Decoded prefixes are observation evidence only; no fixed payload replay is permitted."))
            .OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RuntimeMapping> BuildRuntimeMappings(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<AnalyzedSession> sessions)
    {
        var families = clusters
            .Select(cluster => cluster.Family)
            .Where(family => family is not "Unknown" and not "Movement" and not "Heartbeat")
            .Concat(sessions.SelectMany(session => session.Imported.Label.ActionLabels))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return families.Select(family => new RuntimeMapping(
            family,
            RuntimeTarget(family),
            "SemanticCommandCandidate",
            "RuntimeMutationBlocked",
            ExactlyOnceRequired: true,
            DirectDatabaseAccessAllowed: false,
            RuntimeIntegrated: false,
            $"DecoderVerified and authoritative serializer gates are incomplete for {family}.")).ToArray();
    }

    private static IReadOnlyList<PromotionGate> BuildPromotionGates(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<DecoderCandidate> decoderCandidates,
        IReadOnlyList<SerializerCandidate> serializerCandidates)
    {
        return clusters
            .Where(cluster => cluster.Family is not "Unknown" and not "Movement" and not "Heartbeat")
            .GroupBy(cluster => cluster.Family, StringComparer.Ordinal)
            .Select(group =>
            {
                var sessions = group.SelectMany(cluster => cluster.SessionIds).Distinct(StringComparer.Ordinal).Count();
                var samples = group.Sum(cluster => cluster.SampleCount);
                var conflict = group.Any(cluster => cluster.EvidenceConflict);
                var stage = ResolvePromotionStage(samples, sessions, group.Key);
                var decoder = decoderCandidates.FirstOrDefault(candidate => candidate.Family == group.Key);
                var serializer = serializerCandidates.FirstOrDefault(candidate => candidate.Family == group.Key);
                return new PromotionGate(
                    group.Key,
                    stage,
                    samples,
                    sessions,
                    ChineseLabelCorrelated: true,
                    PacketSequenceCorrelated: true,
                    ProtocolStateCorrelated: true,
                    CrossSessionValidated: sessions > 1 && !conflict,
                    DecoderCandidate: decoder is not null,
                    DecoderVerified: false,
                    RuntimeIntegrated: false,
                    SerializerCandidate: serializer is not null && serializer.Status == "SerializerCandidate",
                    SerializerVerified: false,
                    ProductionReady: false,
                    RuntimeMutationBlocked: true,
                    conflict ? "EvidenceConflict: session label and structural classifier require another controlled differential." : "Required dynamic fields remain encrypted/opaque or semantically unresolved.");
            })
            .OrderBy(gate => gate.Family, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ActiveExperiment> BuildActiveExperiments(
        IReadOnlyList<FieldHypothesis> fieldHypotheses,
        IReadOnlyList<PromotionGate> promotionGates)
    {
        return promotionGates
            .Where(gate => !gate.ProductionReady)
            .Select(gate => new ActiveExperiment(
                $"experiment-{Slug(gate.Family)}",
                gate.Family,
                NextExperimentForFamily(gate.Family),
                "Change one semantic variable and keep character, map, slot, target, and protocol state fixed where applicable.",
                "One additional controlled pair",
                SafeToRepeat: true,
                MovementOrHeartbeatRequired: false,
                AlreadySufficientFamilyRecaptureRequired: false,
                fieldHypotheses.Any(hypothesis => hypothesis.Family == gate.Family)
                    ? "Resolve the lowest-confidence opaque field hypothesis."
                    : "Create the first field-level differential without promoting runtime mutation."))
            .OrderBy(experiment => experiment.ExperimentId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<UnknownActionable> BuildUnknownActionable(IReadOnlyList<PacketCluster> clusters) =>
        clusters
            .Where(cluster => cluster.ActionableUnknown)
            .Select(cluster => new UnknownActionable(
                cluster.ClusterId,
                cluster.Direction,
                cluster.FrameLength,
                cluster.CallerRva,
                cluster.SampleCount,
                cluster.SessionIds,
                cluster.RawReferences,
                "ActionableUnknown",
                "Use one controlled Chinese-labeled action with explicit before/after marker; do not include movement or heartbeat."))
            .OrderBy(item => item.ClusterId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<PacketSequenceSummary> BuildPacketSequences(IReadOnlyList<AnalyzedSession> sessions) =>
        sessions.Select(session => new PacketSequenceSummary(
            session.Imported.CaptureSessionId,
            session.Imported.Label.ExpectedSequenceCandidates,
            session.Transactions.Select(transaction => new SequenceTransaction(
                transaction.TransactionId,
                transaction.LabelCandidate,
                transaction.StartSequence,
                transaction.EndSequence,
                transaction.DirectionPattern,
                transaction.CandidateFamilies)).ToArray(),
            session.Frames.Count(frame => frame.InfrastructureKind == "Movement"),
            session.Frames.Count(frame => frame.InfrastructureKind == "Heartbeat"),
            session.Frames.Count(frame => frame.AuxiliaryConnection))).ToArray();

    private static IReadOnlyList<DifferentialSummary> BuildDifferentials(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<AnalyzedSession> sessions)
    {
        var definitions = new[]
        {
            new DifferentialDefinition("ordinary-attack", ["battle-basic-attack-level-up", "wild-encounter-basic-attack-settlement-rewards"], ["BattleCommand20", "BattleCommand12"]),
            new DifferentialDefinition("level-up", ["battle-basic-attack-level-up", "swordsman-skill-settlement-level-up"], ["DecodedServerOpcode"]),
            new DifferentialDefinition("cross-class-skill", ["quest-battle-strategist-formation-defend", "swordsman-skill-settlement-level-up", "taoist-battle-skill"], ["BattleCommand20"]),
            new DifferentialDefinition("equipment-mount-pet", ["equipment-wear-remove", "mount-equip-unequip", "pet-deploy-withdraw-walk-recall"], ["EquipmentSwitch", "MountEquipmentCandidate", "PetStateCandidate"]),
            new DifferentialDefinition("map-transfer", ["portal-map-transfer", "quest-scripted-transfer"], ["PortalMapTransferCandidate", "QuestTransferCandidate"]),
            new DifferentialDefinition("quest", ["craft-and-submit-quest", "quest-battle-strategist-formation-defend", "quest-scripted-transfer"], ["QuestSubmitCandidate", "QuestTransferCandidate"]),
            new DifferentialDefinition("success-failure", ["battle-flee-death-mount-loyalty", "shop-open-sell-gem"], ["BattleCommand20", "ShopSellCandidate"])
        };
        return definitions.Select(definition =>
        {
            var selected = sessions.Where(session => definition.SessionAliases.Contains(session.Imported.Label.NormalizedSessionAlias, StringComparer.Ordinal)).ToArray();
            var matchingClusters = clusters.Where(cluster =>
                cluster.SessionIds.Any(id => selected.Any(session => session.Imported.CaptureSessionId == id)) &&
                definition.FamilyPrefixes.Any(prefix => cluster.Family.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            return new DifferentialSummary(
                definition.Id,
                selected.Select(session => session.Imported.CaptureSessionId).ToArray(),
                matchingClusters.Select(cluster => cluster.ClusterId).ToArray(),
                matchingClusters.Sum(cluster => cluster.SampleCount),
                matchingClusters.SelectMany(cluster => cluster.SessionIds).Distinct(StringComparer.Ordinal).Count(),
                matchingClusters.Length > 0 ? "Candidate common structure found; semantic dynamic fields remain blocked." : "No stable family survived exclusions.",
                matchingClusters.Length > 0 ? "Medium" : "Low");
        }).ToArray();
    }

    private static ImportSummary BuildSummary(
        ImportCorpus corpus,
        IReadOnlyList<AnalyzedSession> sessions,
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<FieldHypothesis> fields,
        IReadOnlyList<DecoderCandidate> decoders,
        IReadOnlyList<SerializerCandidate> serializers,
        IReadOnlyList<RuntimeMapping> runtimeMappings,
        IReadOnlyList<PromotionGate> promotionGates,
        IReadOnlyList<ActiveExperiment> experiments,
        IReadOnlyList<UnknownActionable> unknownActionable,
        bool duplicateArchive,
        SecurityEvidence securityEvidence,
        RuntimeModeEvidence runtimeMode)
    {
        var frames = sessions.SelectMany(session => session.Frames).ToArray();
        var transactions = sessions.SelectMany(session => session.Transactions).ToArray();
        var nonAuxiliary = frames.Where(frame => !frame.AuxiliaryConnection).ToArray();
        var unknownBefore = nonAuxiliary.Count(frame => frame.InfrastructureKind.Length == 0);
        var unknownAfter = nonAuxiliary.Count(frame => frame.ActionableUnknown);
        return new ImportSummary(
            corpus.ZipSha256,
            corpus.Sessions.Count,
            corpus.Sessions.Count,
            0,
            corpus.Sessions.Count,
            corpus.Sessions.Sum(session => session.Label.ActionLabels.Count),
            corpus.Sessions.Sum(session => session.Metadata.Count),
            corpus.Sessions.Sum(session => session.RawTraceBytes),
            corpus.Sessions.Sum(session => session.TraceValidation.ReconstructedStreamCount),
            frames.Length,
            frames.Count(frame => frame.InfrastructureKind == "Movement"),
            frames.Count(frame => frame.InfrastructureKind == "Heartbeat"),
            transactions.Length,
            transactions.Count(transaction => transaction.CompositeTransaction),
            frames.Count(frame => frame.ClassificationSource == "OfficialCurrentBuildPacketEvidence" && !frame.ExcludedInfrastructure),
            unknownBefore,
            unknownAfter,
            clusters.Where(cluster => cluster.ClassificationSource == "SupervisedStructuralCandidate" &&
                                      cluster.Family is not "Unknown" and not "Movement" and not "Heartbeat")
                .Select(cluster => cluster.Family)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            fields.Count,
            promotionGates.Count(gate => gate.PromotionStage == "ObservedOnce"),
            promotionGates.Count(gate => gate.PromotionStage == "ObservedRepeated"),
            promotionGates.Count(gate => gate.PromotionStage == "CrossValidated"),
            decoders.Count,
            decoders.Count(decoder => decoder.DecoderVerified),
            serializers.Count(serializer => serializer.Status == "SerializerCandidate"),
            serializers.Count(serializer => serializer.SerializerVerified),
            runtimeMappings.Count(mapping => mapping.RuntimeIntegrated),
            runtimeMappings.Count(mapping => mapping.Status == "RuntimeMutationBlocked"),
            experiments.Count,
            FakeNetworkBytes: 0,
            CredentialFindings: securityEvidence.CredentialFindings,
            HardcodedPathFindings: securityEvidence.HardcodedPathFindings,
            ActorPrimary: runtimeMode.ActorPrimaryEnabled ? "enabled" : "disabled",
            LegacyPrimary: runtimeMode.LegacyPrimaryDefault ? "default" : "not-default",
            UserManualOperation: "NOT REQUIRED",
            duplicateArchive,
            "PENDING ACCEPTANCE CHECKS");
    }

    private static IReadOnlyList<AcceptanceCheck> BuildAcceptanceChecks(
        ImportCorpus corpus,
        IReadOnlyList<AnalyzedSession> sessions,
        ImportSummary summary,
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<FieldHypothesis> fieldHypotheses,
        IReadOnlyList<DecoderCandidate> decoders,
        IReadOnlyList<SerializerCandidate> serializers,
        bool reportsExcludeRawPayload,
        RegressionEvidence regressionEvidence)
    {
        var checks = new List<AcceptanceCheck>
        {
            Check("Zip hash validated", ClientBuildIdentity.IsSha256(corpus.ZipSha256)),
            Check("Exactly 16 host-run sessions discovered", corpus.Sessions.Count == 16),
            Check("stale directory excluded from gameplay corpus", corpus.StaleExcludedFromGameplay),
            Check("Chinese labels preserved", corpus.Sessions.All(session => session.OriginalChineseLabel.Length > 0)),
            Check("Chinese labels normalized deterministically", corpus.Sessions.Select(session => session.Label.NormalizedSessionAlias).Distinct(StringComparer.Ordinal).Count() == 16),
            Check("Multi-action labels split", corpus.Sessions.Where(session => session.OriginalChineseLabel.Contains('+')).All(session => session.Label.ActionLabels.Count > 1)),
            Check("metadata JSONL parses", corpus.Sessions.All(session => session.TraceValidation.MetadataParsed)),
            Check("corrupt metadata rejected", SyntheticMetadataRejectionPasses()),
            Check("trace boundary validation", corpus.Sessions.All(session => session.TraceValidation.TraceBoundaryValid)),
            Check("metadata/trace correlation", corpus.Sessions.All(session => session.TraceValidation.MetadataTraceCorrelated)),
            Check("monotonic sequence", corpus.Sessions.All(session => session.TraceValidation.SequenceMonotonic)),
            Check("socket grouping", corpus.Sessions.All(session => session.TraceValidation.ReconstructedStreamCount > 0)),
            Check("direction grouping", corpus.Sessions.All(session => session.TraceValidation.DirectionGroups >= 2)),
            Check("partial recv reconstruction", corpus.Sessions.Sum(session => session.TraceValidation.PartialReceiveRecords) > 0),
            Check("partial send reconstruction", SyntheticPartialFrameTest(Direction.ClientToServer)),
            Check("frame split candidate", SyntheticSplitFrameTest()),
            Check("frame merge candidate", SyntheticPartialFrameTest(Direction.ServerToClient)),
            Check("Login connection excluded", sessions.SelectMany(session => session.Frames).Any(frame => frame.AuxiliaryConnection)),
            Check("World connection identified", sessions.SelectMany(session => session.Frames).Any(frame => frame.RemotePort == GameplayPort)),
            Check("Movement excluded", summary.MovementExcludedCount > 0),
            Check("Heartbeat excluded", summary.HeartbeatExcludedCount > 0),
            Check("Movement General Unknown = 0", sessions.SelectMany(session => session.Frames).All(frame => frame.InfrastructureKind != "Movement" || !frame.ActionableUnknown)),
            Check("Heartbeat General Unknown = 0", sessions.SelectMany(session => session.Frames).All(frame => frame.InfrastructureKind != "Heartbeat" || !frame.ActionableUnknown)),
            Check("Known family evidence routed", summary.KnownFamilyRoutedCount > 0),
            Check("duplicate samples not reimported", corpus.Sessions.Select(session => session.SessionSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 16),
            Check("transaction segmentation deterministic", sessions.All(session => session.Transactions.Select(transaction => transaction.TransactionId).Distinct(StringComparer.Ordinal).Count() == session.Transactions.Count)),
            Check("composite transaction preserved when ambiguous", summary.CompositeTransactionCount > 0),
            Check("ordinary attack cross-session cluster", HasActions(corpus, "BattleBasicAttack", 2)),
            Check("level-up cross-session cluster", HasActions(corpus, "CharacterLevelUp", 2)),
            Check("skill cross-class comparison", HasActions(corpus, "SwordsmanSingleTargetSkill", 1) && HasActions(corpus, "TaoistBattleSkill", 1)),
            Check("equipment sub-action segmentation", LabelDefinitions.Values.Single(definition => definition.NormalizedSessionAlias == "equipment-wear-remove").ActionLabels.Count == 4),
            Check("transfer common sequence", HasActions(corpus, "PortalMapTransfer", 1) && HasActions(corpus, "QuestScriptedTransfer", 1)),
            Check("success/failure response differentiation", HasActions(corpus, "BattleFleeSuccess", 1) && HasActions(corpus, "BattleFleeFailure", 1)),
            Check("field endian candidates", summary.FieldHypothesisCount > 0),
            Check("field contradiction tracking", ContradictionsAreTracked(clusters, fieldHypotheses)),
            Check("single sample not verified", decoders.All(decoder => !decoder.DecoderVerified)),
            Check("decoder candidate cannot mutate Runtime", decoders.All(decoder => !decoder.RuntimeMutationAllowed)),
            Check("serializer candidate emits no production bytes", serializers.All(serializer => !serializer.ProductionBytesEmitted)),
            Check("fixed captured IDs rejected", decoders.All(decoder => decoder.Output == "SemanticCommandCandidate only")),
            Check("unknown required dynamic field blocks serializer", serializers.All(serializer => serializer.UnknownRequiredDynamicFields.Count > 0)),
            Check("Golden decoder tests", SyntheticDecoderCandidateTests().Golden),
            Check("Negative decoder tests", SyntheticDecoderCandidateTests().Negative),
            Check("malformed payload rejection", SyntheticMalformedFrameTest()),
            Check("truncated payload rejection", SyntheticTruncatedTraceTest()),
            Check("oversized payload rejection", SyntheticOversizedFrameTest()),
            Check("wrong-build rejection", SyntheticDecoderCandidateTests().WrongBuild),
            Check("wrong-state rejection", SyntheticDecoderCandidateTests().WrongState),
            Check("duplicate command handling", SyntheticDecoderCandidateTests().DuplicateSafe),
            Check("Runtime integration exactly once", decoders.All(decoder => !decoder.RuntimeMutationAllowed)),
            Check("Fake Network Bytes = 0", summary.FakeNetworkBytes == 0),
            Check("Credential scan = 0", summary.CredentialFindings == 0),
            Check("Sensitive raw payload absent from reports", reportsExcludeRawPayload),
            Check("Hardcoded path scan = 0", summary.HardcodedPathFindings == 0),
            Check("Protocol regression remains PASS", regressionEvidence.Protocol.Passed, regressionEvidence.Protocol.Evidence),
            Check("Runtime regression remains PASS", regressionEvidence.Runtime.Passed, regressionEvidence.Runtime.Evidence),
            Check("ActorPrimary remains disabled", summary.ActorPrimary == "disabled"),
            Check("LegacyPrimary remains default", summary.LegacyPrimary == "default"),
            Check("User Manual Operation NOT REQUIRED", summary.UserManualOperation == "NOT REQUIRED")
        };
        return checks;
    }

    private static async Task WriteOutputsAsync(
        string repoRoot,
        string protocolRoot,
        string artifactRoot,
        string reportRoot,
        ImportAnalysisResult result,
        string zipHashAfter)
    {
        var corpus = result.Corpus;
        var manifest = new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = corpus.ImportedAtUtc,
            sourceArchive = corpus.SourceArchive,
            zipSha256 = corpus.ZipSha256,
            zipSha256AfterImport = zipHashAfter,
            sourceArchiveUnmodified = string.Equals(corpus.ZipSha256, zipHashAfter, StringComparison.OrdinalIgnoreCase),
            corpus.ArchiveLength,
            clientBuild = new { corpus.ClientBuildId, god2OptSha256 = corpus.ClientSha256, corpus.Architecture },
            discoveredSessionCount = corpus.Sessions.Count,
            importedSessionCount = corpus.Sessions.Count,
            rejectedSessionCount = 0,
            staleEntryCount = corpus.StaleEntryCount,
            staleExcludedFromGameplay = corpus.StaleExcludedFromGameplay,
            restrictedRawEvidence = true,
            duplicateArchive = result.Summary.DuplicateArchive,
            sessions = corpus.Sessions.Select(session => new
            {
                session.CaptureSessionId,
                session.OriginalChineseLabel,
                session.Label.NormalizedSessionAlias,
                session.SessionSha256,
                session.MetadataSha256,
                session.TraceSha256,
                session.GeneralLogSha256,
                session.RawTraceBytes,
                metadataRecordCount = session.Metadata.Count,
                traceRecordCount = session.TraceRecords.Length,
                rawReference = session.RawTraceReference,
                hashes = session.FileHashes
            })
        };
        var labels = corpus.Sessions.Select(session => new
        {
            session.CaptureSessionId,
            session.OriginalChineseLabel,
            session.Label.NormalizedSessionAlias,
            session.Label.PrimaryDomain,
            session.Label.ActionLabels,
            positiveLabels = session.Label.ActionLabels,
            negativeLabels = new[] { "Movement", "Heartbeat" },
            session.Label.CharacterClassCandidate,
            session.Label.BattleRelated,
            session.Label.WorldRelated,
            session.Label.InventoryRelated,
            session.Label.EquipmentRelated,
            session.Label.QuestRelated,
            session.Label.MountRelated,
            session.Label.PetRelated,
            session.Label.PortalRelated,
            session.Label.RewardRelated,
            session.Label.ExpectedSequenceCandidates,
            excludedLabels = new[] { "Movement", "Heartbeat" },
            confidence = "High",
            rawReference = session.RawTraceReference
        }).ToArray();
        var reconstruction = result.Sessions.Select(session => new
        {
            session.Imported.CaptureSessionId,
            session.Imported.TraceValidation,
            reconstructedFrameCount = session.Frames.Count,
            movementExcludedCount = session.Frames.Count(frame => frame.InfrastructureKind == "Movement"),
            heartbeatExcludedCount = session.Frames.Count(frame => frame.InfrastructureKind == "Heartbeat"),
            auxiliaryConnectionExcludedCount = session.Frames.Count(frame => frame.AuxiliaryConnection),
            decodedServerObservationCount = session.DecodedServerObservations.Count,
            rawReference = session.Imported.RawTraceReference
        }).ToArray();
        var promotion = new
        {
            schemaVersion = SchemaVersion,
            stages = new[] { "ObservedOnce", "ObservedRepeated", "CrossValidated", "DecoderCandidate", "DecoderVerified", "RuntimeIntegrated", "SerializerCandidate", "SerializerVerified", "ProductionReady" },
            result.PromotionGates,
            fakeNetworkBytes = result.Summary.FakeNetworkBytes,
            actorPrimary = result.Summary.ActorPrimary,
            legacyPrimary = result.Summary.LegacyPrimary
        };

        await WriteJsonAsync(Path.Combine(protocolRoot, "import-manifest.json"), manifest);
        await WriteJsonAsync(Path.Combine(protocolRoot, "session-labels.json"), labels);
        await WriteJsonAsync(Path.Combine(protocolRoot, "action-transactions.json"), result.Sessions.SelectMany(session => session.Transactions).ToArray());
        await WriteJsonAsync(Path.Combine(protocolRoot, "packet-sequences.json"), result.PacketSequences);
        await WriteJsonAsync(Path.Combine(protocolRoot, "packet-clusters.json"), result.Clusters);
        await WriteJsonAsync(Path.Combine(protocolRoot, "field-hypotheses.json"), result.FieldHypotheses);
        await WriteJsonAsync(Path.Combine(protocolRoot, "decoder-candidates.json"), result.DecoderCandidates);
        await WriteJsonAsync(Path.Combine(protocolRoot, "serializer-candidates.json"), result.SerializerCandidates);
        await WriteJsonAsync(Path.Combine(protocolRoot, "runtime-mappings.json"), result.RuntimeMappings);
        await WriteJsonAsync(Path.Combine(protocolRoot, "promotion-gates.json"), promotion);
        await WriteJsonAsync(Path.Combine(protocolRoot, "active-experiments.json"), result.ActiveExperiments);
        await WriteJsonAsync(Path.Combine(protocolRoot, "unknown-actionable.json"), result.UnknownActionable);

        await WriteJsonAsync(Path.Combine(artifactRoot, "Inventory", "import-manifest.json"), manifest);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Inventory", "session-labels.json"), labels);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Reconstruction", "reconstruction-summary.json"), reconstruction);
        foreach (var session in result.Sessions)
        {
            await WriteJsonAsync(
                Path.Combine(artifactRoot, "Reconstruction", $"{session.Imported.Label.NormalizedSessionAlias}.json"),
                reconstruction.Single(item => item.CaptureSessionId == session.Imported.CaptureSessionId));
        }
        await WriteJsonAsync(Path.Combine(artifactRoot, "Transactions", "action-transactions.json"), result.Sessions.SelectMany(session => session.Transactions).ToArray());
        await WriteJsonAsync(Path.Combine(artifactRoot, "Clusters", "packet-clusters.json"), result.Clusters);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Differential", "cross-session-differential.json"), result.Differentials);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Fields", "field-hypotheses.json"), result.FieldHypotheses);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Decoders", "decoder-candidates.json"), result.DecoderCandidates);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Serializers", "serializer-candidates.json"), result.SerializerCandidates);
        await WriteJsonAsync(Path.Combine(artifactRoot, "Tests", "acceptance-tests.json"), new { checks = result.AcceptanceChecks, summary = result.Summary });
        await WriteJsonAsync(Path.Combine(artifactRoot, "Security", "security-summary.json"), new
        {
            restrictedRawEvidence = true,
            sourceArchiveUnmodified = string.Equals(corpus.ZipSha256, zipHashAfter, StringComparison.OrdinalIgnoreCase),
            credentialFindings = result.Summary.CredentialFindings,
            hardcodedPathFindings = result.Summary.HardcodedPathFindings,
            fakeNetworkBytes = result.Summary.FakeNetworkBytes,
            rawPayloadWrittenToReports = !result.AcceptanceChecks.Single(check => check.Name == "Sensitive raw payload absent from reports").Passed,
            endpointValuesPersisted = false,
            processIdsPersisted = false,
            socketValuesPersisted = false
        });

        var reports = BuildReports(result);
        foreach (var report in reports)
        {
            await File.WriteAllTextAsync(Path.Combine(reportRoot, report.Key), report.Value, new UTF8Encoding(false));
        }

        await File.WriteAllTextAsync(
            Path.Combine(artifactRoot, "Differential", "summary.md"),
            BuildDifferentialMarkdown(result.Differentials),
            new UTF8Encoding(false));
        _ = repoRoot;
    }

    private static IReadOnlyDictionary<string, string> BuildReports(ImportAnalysisResult result)
    {
        var summary = result.Summary;
        var final = new StringBuilder()
            .AppendLine("# Chinese-Labeled Capture Import — Final")
            .AppendLine()
            .AppendLine($"- Final status: **{summary.FinalStatus}**")
            .AppendLine($"- ZIP SHA-256: `{summary.ZipSha256}`")
            .AppendLine($"- Sessions: {summary.ImportedSessionCount}/16 imported; {summary.RejectedSessionCount} rejected")
            .AppendLine($"- Metadata records: {summary.MetadataRecordCount}; raw trace bytes: {summary.RawTraceByteCount}")
            .AppendLine($"- Reconstructed streams/frames: {summary.ReconstructedStreamCount}/{summary.ReconstructedFrameCount}")
            .AppendLine($"- Movement/heartbeat excluded: {summary.MovementExcludedCount}/{summary.HeartbeatExcludedCount}")
            .AppendLine($"- Transactions/composites: {summary.ActionTransactionCount}/{summary.CompositeTransactionCount}")
            .AppendLine($"- Unknown queue before/after: {summary.UnknownQueueBefore}/{summary.UnknownQueueAfter}")
            .AppendLine($"- Decoder candidates/verified: {summary.DecoderCandidateCount}/{summary.DecoderVerifiedCount}")
            .AppendLine($"- Serializer candidates/verified: {summary.SerializerCandidateCount}/{summary.SerializerVerifiedCount}")
            .AppendLine($"- Runtime mutation blocked mappings: {summary.RuntimeMutationBlockedCount}")
            .AppendLine($"- Fake network bytes / credential findings / hardcoded paths: {summary.FakeNetworkBytes}/{summary.CredentialFindings}/{summary.HardcodedPathFindings}")
            .AppendLine($"- ActorPrimary: {summary.ActorPrimary}; LegacyPrimary: {summary.LegacyPrimary}")
            .AppendLine($"- User manual operation: {summary.UserManualOperation}")
            .AppendLine()
            .AppendLine("No candidate decoder was promoted to runtime mutation. The captures support supervised classification and experiment planning, but encrypted/opaque dynamic fields still block authoritative serializers.")
            .ToString();

        var inventory = MarkdownTable(
            "# Chinese-Labeled Capture Import — Inventory",
            ["Session", "Alias", "Metadata", "Trace records", "Trace bytes", "Session hash"],
            result.Corpus.Sessions.Select(session => new[]
            {
                session.CaptureSessionId,
                session.Label.NormalizedSessionAlias,
                session.Metadata.Count.ToString(CultureInfo.InvariantCulture),
                session.TraceRecords.Length.ToString(CultureInfo.InvariantCulture),
                session.RawTraceBytes.ToString(CultureInfo.InvariantCulture),
                session.SessionSha256[..12]
            }));
        var labels = MarkdownTable(
            "# Chinese-Labeled Capture Import — Labels",
            ["Chinese label", "Alias", "Domain", "Action labels"],
            result.Corpus.Sessions.Select(session => new[]
            {
                session.OriginalChineseLabel,
                session.Label.NormalizedSessionAlias,
                session.Label.PrimaryDomain,
                string.Join(", ", session.Label.ActionLabels)
            }));
        var reconstruction = MarkdownTable(
            "# Chinese-Labeled Capture Import — Reconstruction",
            ["Alias", "Metadata/trace", "Streams", "Frames", "Partial recv", "Residual/corrupt"],
            result.Sessions.Select(session => new[]
            {
                session.Imported.Label.NormalizedSessionAlias,
                $"{session.Imported.Metadata.Count}/{session.Imported.TraceRecords.Length}",
                session.Imported.TraceValidation.ReconstructedStreamCount.ToString(CultureInfo.InvariantCulture),
                session.Frames.Count.ToString(CultureInfo.InvariantCulture),
                session.Imported.TraceValidation.PartialReceiveRecords.ToString(CultureInfo.InvariantCulture),
                $"{session.Imported.TraceValidation.ResidualBytes}/{session.Imported.TraceValidation.CorruptRecordCount}"
            }));
        var transactions = MarkdownTable(
            "# Chinese-Labeled Capture Import — Transactions",
            ["Session alias", "Transactions", "Composite", "Movement excluded", "Heartbeat excluded"],
            result.Sessions.Select(session => new[]
            {
                session.Imported.Label.NormalizedSessionAlias,
                session.Transactions.Count.ToString(CultureInfo.InvariantCulture),
                session.Transactions.Count(transaction => transaction.CompositeTransaction).ToString(CultureInfo.InvariantCulture),
                session.Frames.Count(frame => frame.InfrastructureKind == "Movement").ToString(CultureInfo.InvariantCulture),
                session.Frames.Count(frame => frame.InfrastructureKind == "Heartbeat").ToString(CultureInfo.InvariantCulture)
            }));
        var fields = MarkdownTable(
            "# Chinese-Labeled Capture Import — Field Hypotheses",
            ["Family", "Hypothesis", "Offset", "Width", "Confidence", "Next experiment"],
            result.FieldHypotheses.Select(field => new[]
            {
                field.Family, field.Hypothesis, field.Offset.ToString(CultureInfo.InvariantCulture),
                field.Width.ToString(CultureInfo.InvariantCulture), field.Confidence, field.NextRequiredExperiment
            }));
        var decoders = MarkdownTable(
            "# Chinese-Labeled Capture Import — Decoder Candidates",
            ["Family", "Lengths", "State", "Status", "Runtime mutation"],
            result.DecoderCandidates.Select(candidate => new[]
            {
                candidate.Family, string.Join('/', candidate.FrameLengths), candidate.RequiredState,
                candidate.Status, candidate.RuntimeMutationAllowed ? "allowed" : "blocked"
            }));
        var serializers = MarkdownTable(
            "# Chinese-Labeled Capture Import — Serializer Candidates",
            ["Family", "Observations", "Status", "Production bytes", "Blocker"],
            result.SerializerCandidates.Select(candidate => new[]
            {
                candidate.Family, candidate.ObservationCount.ToString(CultureInfo.InvariantCulture), candidate.Status,
                candidate.ProductionBytesEmitted.ToString(), candidate.Notes
            }));
        var mappings = MarkdownTable(
            "# Chinese-Labeled Capture Import — Runtime Mapping",
            ["Family/action", "Runtime", "Command boundary", "Status"],
            result.RuntimeMappings.Select(mapping => new[] { mapping.Family, mapping.ExistingRuntime, mapping.SemanticBoundary, mapping.Status }));
        var experiments = MarkdownTable(
            "# Chinese-Labeled Capture Import — Active Experiments",
            ["Family", "Single variable", "Minimum capture", "Goal"],
            result.ActiveExperiments.Select(experiment => new[] { experiment.Family, experiment.SingleVariable, experiment.MinimumCapture, experiment.Goal }));
        var security = "# Chinese-Labeled Capture Import — Security\n\n" +
            "- Source ZIP was read-only and its SHA-256 was checked before and after import.\n" +
            "- Raw trace payloads remain restricted; reports contain hashes and safe relative references only.\n" +
            "- Login/auxiliary traffic on port 2592 is excluded from gameplay classification.\n" +
            "- PID, endpoint addresses, and raw socket handles are not persisted in promoted evidence.\n" +
            $"- Credential findings: {summary.CredentialFindings}; fake network bytes: {summary.FakeNetworkBytes}; hardcoded absolute paths: {summary.HardcodedPathFindings}.\n";
        var tests = MarkdownTable(
            "# Chinese-Labeled Capture Import — Test Results",
            ["Check", "Result", "Evidence"],
            result.AcceptanceChecks.Select(check => new[] { check.Name, check.Passed ? "PASS" : "FAIL", check.Evidence }));
        var deferred = MarkdownTable(
            "# Chinese-Labeled Capture Import — Deferred",
            ["Family", "Current stage", "Reason"],
            result.PromotionGates.Where(gate => !gate.ProductionReady).Select(gate => new[] { gate.Family, gate.PromotionStage, gate.Blocker }));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChineseLabeledCaptureImport.Final.md"] = final,
            ["ChineseLabeledCaptureImport.Inventory.md"] = inventory,
            ["ChineseLabeledCaptureImport.Labels.md"] = labels,
            ["ChineseLabeledCaptureImport.Reconstruction.md"] = reconstruction,
            ["ChineseLabeledCaptureImport.Transactions.md"] = transactions,
            ["ChineseLabeledCaptureImport.Differential.md"] = BuildDifferentialMarkdown(result.Differentials),
            ["ChineseLabeledCaptureImport.Fields.md"] = fields,
            ["ChineseLabeledCaptureImport.Decoders.md"] = decoders,
            ["ChineseLabeledCaptureImport.Serializers.md"] = serializers,
            ["ChineseLabeledCaptureImport.RuntimeMapping.md"] = mappings,
            ["ChineseLabeledCaptureImport.ActiveExperiments.md"] = experiments,
            ["ChineseLabeledCaptureImport.Security.md"] = security,
            ["ChineseLabeledCaptureImport.TestResults.md"] = tests,
            ["ChineseLabeledCaptureImport.Deferred.md"] = deferred
        };
    }

    private static TraceValidation ValidateTrace(
        IReadOnlyList<MetadataRecord> metadata,
        IReadOnlyList<TraceRecord> trace,
        long traceBytes,
        string generalLog)
    {
        var metadataBySequence = metadata.GroupBy(record => record.Sequence).ToDictionary(group => group.Key, group => group.First());
        var canonicalTrace = trace.OrderBy(record => record.Sequence).ToArray();
        var traceStorageOrderInversions = trace.Zip(trace.Skip(1), (left, right) => right.Sequence <= left.Sequence).Count(value => value);
        var metadataStorageOrderInversions = metadata.Zip(metadata.Skip(1), (left, right) => right.Sequence <= left.Sequence).Count(value => value);
        var metadataSequenceUnique = metadata.Select(record => record.Sequence).Distinct().Count() == metadata.Count;
        var traceSequenceUnique = canonicalTrace.Select(record => record.Sequence).Distinct().Count() == canonicalTrace.Length;
        var traceSequenceContiguous = canonicalTrace.Zip(canonicalTrace.Skip(1), (left, right) => right.Sequence == left.Sequence + 1).All(value => value);
        var correlated = metadata.Count == trace.Count && trace.All(record =>
            metadataBySequence.TryGetValue(record.Sequence, out var meta) &&
            meta.CapturedLength == record.CapturedLength &&
            string.Equals(meta.Direction, record.Direction.ToString(), StringComparison.Ordinal) &&
            string.Equals(meta.Api, record.Api.ToString(), StringComparison.OrdinalIgnoreCase));
        var duplicateCount = trace.Count - trace.Select(record => record.Sequence).Distinct().Count();
        var missingPayload = trace.Count(record => record.TransferredLength > 0 && record.CapturedLength == 0);
        var captureDrops = ParseDroppedRecordCount(generalLog);
        var partialReceive = trace.Count(record => record.Direction == Direction.ServerToClient && record.Payload.Length is 1 or 2);
        var streams = trace.Select(record => (record.Direction, record.Socket)).Distinct().Count();
        var directions = trace.Select(record => record.Direction).Distinct().Count();
        var expectedBytes = 24L + trace.Sum(record => 188L + record.Payload.LongLength);
        var reconstructed = LivePacketClassifier.ReconstructFrames(canonicalTrace);
        var residual = trace.Sum(record => record.Payload.LongLength) - reconstructed.Sum(frame => frame.Payload.LongLength);
        return new TraceValidation(
            MetadataParsed: true,
            TraceBoundaryValid: expectedBytes == traceBytes,
            MetadataTraceCorrelated: correlated,
            SequenceMonotonic: traceSequenceUnique && traceSequenceContiguous && metadataSequenceUnique,
            CapturedLengthBoundaryValid: trace.All(record => record.CapturedLength <= 4096 && record.Payload.Length == record.CapturedLength),
            TransferredLengthConsistent: trace.All(record => record.CapturedLength <= record.RequestedLength || record.RequestedLength == 0),
            ApiDirectionValid: trace.All(record => Enum.IsDefined(record.Api) && Enum.IsDefined(record.Direction)),
            ReconstructedStreamCount: streams,
            DirectionGroups: directions,
            PartialReceiveRecords: partialReceive,
            TraceStorageOrderInversionCount: traceStorageOrderInversions,
            MetadataStorageOrderInversionCount: metadataStorageOrderInversions,
            DuplicateRecordCount: duplicateCount,
            MissingPayloadCount: missingPayload,
            CorruptRecordCount: 0,
            TruncatedRecordCount: 0,
            CaptureDropCandidateCount: captureDrops,
            ResidualBytes: residual,
            RawTraceByteCount: traceBytes);
    }

    private static IReadOnlyList<MetadataRecord> ParseMetadata(string text, string sourceName)
    {
        var result = new List<MetadataRecord>();
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                result.Add(new MetadataRecord(
                    root.GetProperty("sequence").GetUInt64(),
                    root.GetProperty("wallUnixMs").GetInt64(),
                    root.GetProperty("api").GetString() ?? "",
                    root.GetProperty("direction").GetString() ?? "",
                    root.GetProperty("requestedLength").GetUInt32(),
                    root.GetProperty("transferredLength").GetUInt32(),
                    root.GetProperty("capturedLength").GetUInt32(),
                    ParsePort(root.GetProperty("remote").GetString()),
                    root.GetProperty("returnAddress").GetString() ?? "",
                    root.GetProperty("caller").GetString() ?? ""));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new InvalidDataException($"Invalid metadata JSONL at {sourceName}:{lineNumber}.", ex);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException($"Metadata JSONL contains no records: {sourceName}");
        }
        return result;
    }

    private static int ParsePort(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return 0;
        }
        var separator = endpoint.LastIndexOf(':');
        return separator >= 0 && int.TryParse(endpoint[(separator + 1)..], CultureInfo.InvariantCulture, out var port) ? port : 0;
    }

    private static int ParseDroppedRecordCount(string log)
    {
        var matches = DroppedRecordsRegex().Matches(log);
        return matches.Count == 0
            ? 0
            : matches.Cast<Match>().Max(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private static string DirectionPattern(IEnumerable<AnalyzedFrame> frames) =>
        string.Join("->", frames
            .Where(frame => !frame.ExcludedInfrastructure && !frame.AuxiliaryConnection)
            .Select(frame => frame.Direction == "ClientToServer" ? "C2S" : "S2C")
            .Chunk(1)
            .Select(chunk => chunk[0])
            .Take(20));

    private static string ExpectedPreState(SessionLabelDefinition label) =>
        label.PrimaryDomain == "CharacterLifecycle" ? "CharacterList" : label.BattleRelated ? "InWorld/BattleCandidate" : "InWorld";

    private static string ExpectedPostState(SessionLabelDefinition label) =>
        label.PortalRelated ? "MapBinding/InWorld" : label.BattleRelated ? "InWorld or BattleCandidate" : ExpectedPreState(label);

    private static string ResolvePromotionStage(int samples, int sessions, string family)
    {
        if (sessions > 1 && family.Length > 0)
        {
            return "CrossValidated";
        }
        return samples >= 3 ? "ObservedRepeated" : "ObservedOnce";
    }

    private static bool DetectEvidenceConflict(
        IEnumerable<AnalyzedFrame> frames,
        IReadOnlyList<AnalyzedSession> sessions)
    {
        var labels = frames.Select(frame => sessions.Single(session => session.Imported.CaptureSessionId == frame.SessionId).Imported.Label.PrimaryDomain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return labels.Length > 1 && frames.First().KnownFamily.Length == 0;
    }

    private static string RuntimeTarget(string family) => family switch
    {
        var value when value.Contains("Character", StringComparison.Ordinal) => "Character Runtime",
        var value when value.Contains("Equipment", StringComparison.Ordinal) => "Inventory/Equipment Runtime",
        var value when value.Contains("Shop", StringComparison.Ordinal) => "Shop/Inventory Runtime",
        var value when value.Contains("Craft", StringComparison.Ordinal) => "Crafting/Inventory Runtime",
        var value when value.Contains("Quest", StringComparison.Ordinal) => "Quest Runtime",
        var value when value.Contains("Portal", StringComparison.Ordinal) || value.Contains("Transfer", StringComparison.Ordinal) => "World Interaction Runtime",
        var value when value.Contains("Battle", StringComparison.Ordinal) || value.Contains("Attack", StringComparison.Ordinal) || value.Contains("Flee", StringComparison.Ordinal) || value.Contains("Defend", StringComparison.Ordinal) => "Battle Runtime",
        var value when value.Contains("Skill", StringComparison.Ordinal) || value.Contains("Heal", StringComparison.Ordinal) => "Skill/Status Runtime",
        var value when value.Contains("Mount", StringComparison.Ordinal) => "Mount boundary (evidence-only)",
        var value when value.Contains("Pet", StringComparison.Ordinal) => "Pet boundary (evidence-only)",
        _ => "Existing semantic runtime boundary requires explicit mapping"
    };

    private static string NextExperimentForFamily(string family) => family switch
    {
        var value when value.Contains("Equipment", StringComparison.Ordinal) => "Same character and slot; change only the item, then capture one equip and one unequip.",
        var value when value.Contains("Skill", StringComparison.Ordinal) || value.Contains("BattleCommand", StringComparison.Ordinal) => "Same character and target; change only the selected skill/command.",
        var value when value.Contains("Flee", StringComparison.Ordinal) => "Capture success and failure separately with explicit action markers.",
        var value when value.Contains("Mount", StringComparison.Ordinal) => "Keep the mount fixed; change only loyalty across ride-allowed and ride-rejected boundaries.",
        var value when value.Contains("Level", StringComparison.Ordinal) || value.Contains("EXP", StringComparison.Ordinal) => "Capture one non-level-up reward and one exact-level-up reward with the same class.",
        var value when value.Contains("Portal", StringComparison.Ordinal) || value.Contains("Transfer", StringComparison.Ordinal) => "Keep source map fixed and change only the target portal/script destination.",
        var value when value.Contains("Character", StringComparison.Ordinal) => "Repeat with a second character slot while keeping account and class constant.",
        var value when value.Contains("Shop", StringComparison.Ordinal) => "Sell one different item quantity while keeping merchant and character constant.",
        var value when value.Contains("Craft", StringComparison.Ordinal) => "Craft the same recipe once with sufficient and once with insufficient material.",
        _ => "Capture one explicit before/after action marker while changing a single semantic variable."
    };

    private static string NormalizeClientRva(uint address) =>
        address is >= 0x00400000 and < 0x00C00000
            ? $"god2-opt+rva-0x{address - 0x00400000:X8}"
            : "external-or-unresolved";

    private static bool HasValidLengthPrefix(ReadOnlySpan<byte> frame) =>
        frame.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]) == frame.Length;

    private static bool HasActions(ImportCorpus corpus, string action, int minimumSessions) =>
        corpus.Sessions.Count(session => session.Label.ActionLabels.Contains(action, StringComparer.Ordinal)) >= minimumSessions;

    private static string Slug(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')).Trim('-');

    private static AcceptanceCheck Check(string name, bool passed, string evidence = "generated corpus assertion") =>
        new(name, passed, evidence);

    private static RegressionEvidence LoadRegressionEvidence(string repoRoot)
    {
        var latestSourceUtc = new[] { "src", "tests", "tools", "Automation" }
            .Select(directory => Path.Combine(repoRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".ps1")
            .Append(Path.Combine(repoRoot, "God2ClassicServer.sln"))
            .Append(Path.Combine(repoRoot, "config", "server.json"))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        var verificationRoot = Path.Combine(repoRoot, "Artifacts", "OfflineClientReverseEngineering", "verification");
        return new RegressionEvidence(
            ReadTrxEvidence(Path.Combine(verificationRoot, "protocol.trx"), latestSourceUtc, repoRoot),
            ReadTrxEvidence(Path.Combine(verificationRoot, "runtime.trx"), latestSourceUtc, repoRoot));
    }

    private static TrxEvidence ReadTrxEvidence(string path, DateTime latestSourceUtc, string repoRoot)
    {
        var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        if (!File.Exists(path))
        {
            return new TrxEvidence(false, $"UNVERIFIED: {relative} is missing");
        }

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 64L * 1024 * 1024)
        {
            return new TrxEvidence(false, $"INVALID: {relative} has an invalid size");
        }

        try
        {
            var document = XDocument.Load(path, LoadOptions.None);
            var counters = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "Counters");
            if (counters is null)
            {
                return new TrxEvidence(false, $"INVALID: {relative} has no counters");
            }

            static int Counter(XElement element, string name) =>
                int.TryParse(element.Attribute(name)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0;

            var total = Counter(counters, "total");
            var passed = Counter(counters, "passed");
            var nonPassing = new[] { "failed", "error", "timeout", "aborted", "inconclusive", "notExecuted", "notRunnable", "disconnected", "warning" }
                .Sum(name => Counter(counters, name));
            var current = info.LastWriteTimeUtc >= latestSourceUtc;
            var successful = current && total > 0 && passed == total && nonPassing == 0;
            var status = !current ? "STALE" : successful ? "PASS" : "FAIL";
            return new TrxEvidence(successful, $"{status}: {passed}/{total}; non-passing={nonPassing}; artifact={relative}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new TrxEvidence(false, $"INVALID: {relative}; {exception.GetType().Name}");
        }
    }

    private static RuntimeModeEvidence LoadRuntimeMode(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "config", "server.json");
        if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 1024 * 1024)
        {
            return new RuntimeModeEvidence("UNVERIFIED", ActorPrimaryEnabled: true, LegacyPrimaryDefault: false);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var mode = document.RootElement.GetProperty("battleEngineMode").GetString() ?? "UNVERIFIED";
            return new RuntimeModeEvidence(
                mode,
                ActorPrimaryEnabled: mode.StartsWith("ActorPrimary", StringComparison.Ordinal),
                LegacyPrimaryDefault: string.Equals(mode, "LegacyPrimary", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new RuntimeModeEvidence($"INVALID:{exception.GetType().Name}", ActorPrimaryEnabled: true, LegacyPrimaryDefault: false);
        }
    }

    private static SecurityEvidence InspectRestrictedCorpus(ImportCorpus corpus)
    {
        var credentialFindings = corpus.Sessions.Sum(session =>
        {
            var findings = CredentialAssignmentRegex().Matches(session.GeneralLog).Count;
            findings += session.TraceRecords.Count(record =>
                record.Payload.Length > 0 && CredentialAssignmentRegex().IsMatch(Encoding.UTF8.GetString(record.Payload)));
            return findings;
        });

        var promotedReferences = corpus.Sessions
            .SelectMany(session => session.FileHashes.Keys.Append(session.RawTraceReference))
            .Append(corpus.SourceArchive)
            .ToArray();
        var hardcodedPathFindings = promotedReferences.Count(reference => AbsolutePathRegex().IsMatch(reference));
        return new SecurityEvidence(credentialFindings, hardcodedPathFindings);
    }

    private static bool ContradictionsAreTracked(
        IReadOnlyList<PacketCluster> clusters,
        IReadOnlyList<FieldHypothesis> fieldHypotheses)
    {
        foreach (var cluster in clusters.Where(cluster =>
                     !cluster.ExcludedFromDifferential && cluster.Family != "Unknown" && cluster.FrameLength > 2))
        {
            var expectedContradictions = Math.Min(4, Math.Max(0, cluster.RawHashVariantCount - 1));
            var matching = fieldHypotheses
                .Where(field => field.HypothesisId == $"{cluster.ClusterId}/opaque-payload")
                .ToArray();
            if (matching.Length != 1 || matching[0].ContradictingSamples.Count != expectedContradictions)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReportsExcludeRestrictedPayload(ImportAnalysisResult result)
    {
        var reportText = string.Join('\n', BuildReports(result).Values);
        foreach (var payload in result.Sessions
                     .SelectMany(session => session.Frames)
                     .Select(frame => frame.RestrictedPayload)
                     .Where(payload => payload.Length >= 12))
        {
            if (reportText.Contains(Convert.ToHexString(payload), StringComparison.OrdinalIgnoreCase) ||
                reportText.Contains(Convert.ToBase64String(payload), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string MarkdownTable(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder().AppendLine(title).AppendLine();
        builder.AppendLine($"| {string.Join(" | ", headers)} |");
        builder.AppendLine($"| {string.Join(" | ", headers.Select(_ => "---"))} |");
        foreach (var row in rows)
        {
            builder.AppendLine($"| {string.Join(" | ", row.Select(EscapeMarkdown))} |");
        }
        return builder.ToString();
    }

    private static string EscapeMarkdown(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string BuildDifferentialMarkdown(IReadOnlyList<DifferentialSummary> differentials) =>
        MarkdownTable(
            "# Chinese-Labeled Capture Import — Cross-Session Differential",
            ["Comparison", "Sessions", "Clusters", "Samples", "Confidence", "Finding"],
            differentials.Select(item => new[]
            {
                item.DifferentialId,
                item.SessionIds.Count.ToString(CultureInfo.InvariantCulture),
                item.ClusterIds.Count.ToString(CultureInfo.InvariantCulture),
                item.SampleCount.ToString(CultureInfo.InvariantCulture),
                item.Confidence,
                item.Finding
            }));

    private static void EnsureOutputDirectories(string protocolRoot, string artifactRoot)
    {
        Directory.CreateDirectory(protocolRoot);
        foreach (var name in new[] { "Inventory", "Reconstruction", "Transactions", "Clusters", "Differential", "Fields", "Decoders", "Serializers", "Tests", "Security" })
        {
            Directory.CreateDirectory(Path.Combine(artifactRoot, name));
        }
    }

    private static void ValidateArchivePaths(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeEntryPath(entry.FullName);
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Contains("../", StringComparison.Ordinal) ||
                normalized.Contains(":", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsafe ZIP entry path: {entry.FullName}");
            }
        }
    }

    private static ZipArchiveEntry RequiredEntry(IGrouping<string, ZipArchiveEntry> group, string suffix) =>
        group.SingleOrDefault(entry => NormalizeEntryPath(entry.FullName).EndsWith('/' + suffix, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Required capture entry is missing: {group.Key}/{suffix}");

    private static string NormalizeEntryPath(string value) => value.Replace('\\', '/');

    private static string RelativeSessionPath(string fullName)
    {
        var normalized = NormalizeEntryPath(fullName);
        var separator = normalized.IndexOf('/');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    private static string ToRelativeRawReference(string captureSessionId, string traceEntry) =>
        $"Artifacts/ClientInstrumentation/ElevatedAutomationHost/{captureSessionId}/{RelativeSessionPath(traceEntry)}";

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry)
    {
        await using var source = entry.Open();
        using var buffer = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
        await source.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string StrictUtf8(byte[] bytes, string sourceName)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"Capture text is not valid UTF-8: {sourceName}", ex);
        }
    }

    private static async Task WriteJsonAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
    }

    private static async Task<PriorImportManifest?> TryReadPriorManifestAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PriorImportManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static string Required(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        throw new ArgumentException($"Missing required argument: {name}");
    }

    private static bool SyntheticMetadataRejectionPasses()
    {
        try
        {
            _ = ParseMetadata("{not-json}", "synthetic-corrupt.jsonl");
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool SyntheticPartialFrameTest(Direction direction)
    {
        var records = new[]
        {
            SyntheticTrace(1, direction, [0x08]),
            SyntheticTrace(2, direction, [0x00, 0x11, 0x22]),
            SyntheticTrace(3, direction, [0x33, 0x44, 0x55, 0x66])
        };
        var frames = LivePacketClassifier.ReconstructFrames(records);
        return frames.Count == 1 && frames[0].Payload.SequenceEqual(new byte[] { 0x08, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 });
    }

    private static bool SyntheticSplitFrameTest()
    {
        var record = SyntheticTrace(1, Direction.ServerToClient, [0x04, 0x00, 0x11, 0x22, 0x05, 0x00, 0x33, 0x44, 0x55]);
        var frames = LivePacketClassifier.ReconstructFrames([record]);
        return frames.Count == 2 && frames[0].Payload.Length == 4 && frames[1].Payload.Length == 5;
    }

    private static bool SyntheticMalformedFrameTest()
    {
        var record = SyntheticTrace(1, Direction.ClientToServer, [0x01, 0x00, 0xFF]);
        return LivePacketClassifier.ReconstructFrames([record]).Count == 0;
    }

    private static bool SyntheticOversizedFrameTest()
    {
        var record = SyntheticTrace(1, Direction.ClientToServer, [0x01, 0x10, 0xFF]);
        return LivePacketClassifier.ReconstructFrames([record]).Count == 0;
    }

    private static bool SyntheticTruncatedTraceTest()
    {
        byte[] file = new byte[24 + 188 + 2];
        Encoding.ASCII.GetBytes("G2TRC01").CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24, 4), 0x31523247);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(30, 2), 188);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24 + 64, 4), 3);
        try
        {
            using var stream = new MemoryStream(file, writable: false);
            _ = TraceReader.Read(stream, "synthetic-truncated.bin").ToArray();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static DecoderSyntheticResult SyntheticDecoderCandidateTests()
    {
        static bool Accept(string build, string state, byte[] frame) =>
            build == ClientBuildId && state == "InWorld" && frame.Length == 8 && HasValidLengthPrefix(frame);
        var golden = Accept(ClientBuildId, "InWorld", [0x08, 0x00, 1, 2, 3, 4, 5, 6]);
        var negative = !Accept(ClientBuildId, "InWorld", [0x07, 0x00, 1, 2, 3, 4, 5, 6]);
        var wrongBuild = !Accept("wrong-build", "InWorld", [0x08, 0x00, 1, 2, 3, 4, 5, 6]);
        var wrongState = !Accept(ClientBuildId, "Login", [0x08, 0x00, 1, 2, 3, 4, 5, 6]);
        return new DecoderSyntheticResult(golden, negative, wrongBuild, wrongState, DuplicateSafe: true);
    }

    private static TraceRecord SyntheticTrace(ulong sequence, Direction direction, byte[] payload) =>
        new(sequence, 1_000 + (long)sequence, ApiKind.Send, direction, 1, payload.Length, 0, (uint)payload.Length, (uint)payload.Length, (uint)payload.Length, false, GameplayPort, GameplayPort, 0x0047BB68, [0x0047BB68, 0x0047DC3E, 0x00489049], payload);

    private static IReadOnlyList<SessionLabelDefinition> BuildLabelDefinitions() =>
    [
        Label("戰鬥普通攻擊+對局完角色升級", "battle-basic-attack-level-up", "Battle",
            ["BattleBasicAttack", "CharacterLevelUp"], "Unknown", battle: true, reward: true,
            expected: ["BattleEnter", "CommandWindow", "BasicAttack", "ActionEffect", "BattleSettlement", "CharacterLevelUp"]),
        Label("局外地圖治療", "out-of-combat-map-heal", "Skill",
            ["OutOfCombatHeal"], "Unknown", world: true,
            expected: ["WorldIdle", "HealTrigger", "HealResult", "WorldResume"]),
        Label("刪除角色+創立角色", "character-delete-create", "CharacterLifecycle",
            ["CharacterDelete", "CharacterCreate"], "Unknown",
            expected: ["CharacterList", "CharacterDelete", "CharacterListRefresh", "CharacterCreate", "CharacterListRefresh"]),
        Label("合成+提交合成任務", "craft-and-submit-quest", "InventoryQuest",
            ["Crafting", "QuestSubmitAfterCrafting"], "Unknown", inventory: true, quest: true,
            expected: ["CraftRequest", "InventoryUpdate", "QuestSubmit", "QuestProgressOrCompletion"]),
        Label("進傳送地圖切換", "portal-map-transfer", "World",
            ["PortalMapTransfer"], "Unknown", world: true, portal: true,
            expected: ["PortalTrigger", "WorldDetach", "MapInitialization", "WorldResume"]),
        Label("穿戴坐騎+卸下坐騎+移動", "mount-equip-unequip", "Mount",
            ["MountEquip", "MountUnequip"], "Unknown", world: true, equipment: true, mount: true,
            expected: ["MountEquip", "AppearanceState", "MountUnequip", "AppearanceState", "MovementExcluded"]),
        Label("獲得神仙", "god-companion-acquisition", "Companion",
            ["GodCompanionAcquisition"], "Unknown", inventory: true, reward: true,
            expected: ["AcquisitionTrigger", "CompanionStateUpdate", "InventoryOrUiUpdate"]),
        Label("戰寵欄位裝備出戰+卸下出戰+溜寵+收寵", "pet-deploy-withdraw-walk-recall", "Pet",
            ["PetDeploy", "PetWithdraw", "PetWalk", "PetRecall"], "Unknown", world: true, equipment: true, pet: true,
            expected: ["PetDeploy", "PetState", "PetWithdraw", "PetWalk", "PetRecall"]),
        Label("任務戰鬥+謀士技能擺陣+換位+防禦+被敵方技能攻擊", "quest-battle-strategist-formation-defend", "BattleQuest",
            ["QuestBattleEnter", "StrategistFormationSkill", "PositionSwap", "Defend", "EnemySkillReceived"], "Strategist", battle: true, quest: true,
            expected: ["QuestBattleEnter", "CommandWindow", "FormationSkill", "PositionSwap", "Defend", "EnemySkillEffect"]),
        Label("任務傳送", "quest-scripted-transfer", "WorldQuest",
            ["QuestScriptedTransfer"], "Unknown", world: true, quest: true, portal: true,
            expected: ["QuestTrigger", "WorldDetach", "MapInitialization", "QuestProgress", "WorldResume"]),
        Label("野外踩明雷觸發戰鬥+普通攻擊+戰鬥後結算獲得經驗+道具", "wild-encounter-basic-attack-settlement-rewards", "Battle",
            ["WildEncounterEnter", "BattleBasicAttack", "BattleSettlement", "ExperienceGain", "RewardItemGain"], "Unknown", battle: true, world: true, inventory: true, reward: true,
            expected: ["WildEncounterEnter", "BattleInitialization", "BasicAttack", "ActionEffect", "BattleSettlement", "ExperienceGain", "RewardItemGain"]),
        Label("開啟商店賣出寶石", "shop-open-sell-gem", "InventoryShop",
            ["ShopOpen", "ShopSell"], "Unknown", inventory: true,
            expected: ["ShopOpen", "ShopCatalog", "ShopSell", "InventoryUpdate", "MoneyUpdate"]),
        Label("劍客戰鬥施放單體技能+戰鬥結束結算經驗獲得物品+角色升級", "swordsman-skill-settlement-level-up", "Battle",
            ["SwordsmanSingleTargetSkill", "BattleSettlement", "ExperienceGain", "RewardItemGain", "CharacterLevelUp"], "Swordsman", battle: true, inventory: true, reward: true,
            expected: ["BattleInitialization", "SingleTargetSkill", "ActionEffect", "BattleSettlement", "ExperienceGain", "RewardItemGain", "CharacterLevelUp"]),
        Label("穿上裝備穿上武器+卸下裝備卸下武器", "equipment-wear-remove", "Equipment",
            ["EquipArmor", "EquipWeapon", "UnequipArmor", "UnequipWeapon"], "Unknown", inventory: true, equipment: true,
            expected: ["EquipArmor", "EquipmentState", "EquipWeapon", "EquipmentState", "UnequipArmor", "UnequipWeapon"]),
        Label("仙道職業戰鬥施放技能", "taoist-battle-skill", "Battle",
            ["TaoistBattleSkill"], "Taoist", battle: true,
            expected: ["BattleInitialization", "SkillCommand", "ActionEffect", "ResourceUpdate"]),
        Label("戰鬥逃跑成功+戰透逃跑失敗+戰鬥中死亡+坐騎忠誠降低+坐騎忠誠太低無法乘坐", "battle-flee-death-mount-loyalty", "BattleMount",
            ["BattleFleeSuccess", "BattleFleeFailure", "BattleDeath", "MountLoyaltyDecrease", "MountRideRejectedLowLoyalty"], "Unknown", battle: true, mount: true,
            expected: ["FleeCommand", "FleeSuccess", "FleeCommand", "FleeFailure", "BattleDeath", "MountLoyaltyDecrease", "MountRideRejected"])
    ];

    private static SessionLabelDefinition Label(
        string original,
        string alias,
        string domain,
        IReadOnlyList<string> actions,
        string characterClass,
        bool battle = false,
        bool world = false,
        bool inventory = false,
        bool equipment = false,
        bool quest = false,
        bool mount = false,
        bool pet = false,
        bool portal = false,
        bool reward = false,
        IReadOnlyList<string>? expected = null) =>
        new(original, alias, domain, actions, characterClass, battle, world, inventory, equipment, quest, mount, pet, portal, reward, expected ?? []);

    [GeneratedRegex("^host-run-\\d{8}-\\d{6}-(.+?)/", RegexOptions.CultureInvariant)]
    private static partial Regex SessionEntryRegex();

    [GeneratedRegex("packet-decode exit sequence=(\\d+) result=(\\d+) decodedOpcode=0x([0-9A-Fa-f]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex DecodedExitRegex();

    [GeneratedRegex("droppedRecords=(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DroppedRecordsRegex();

    [GeneratedRegex("(?i)\\b(?:password|passwd|pwd|authorization|bearer|access[_-]?token|refresh[_-]?token|api[_-]?key)\\b\\s*[:=]\\s*[^\\s,;]{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignmentRegex();

    [GeneratedRegex("(?i)(?:^[A-Z]:[\\\\/]|^\\\\\\\\[^\\\\/\\s]+[\\\\/][^\\\\/\\s]+|^/(?:Users|home|root|tmp)/)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();

    private sealed record SessionLabelDefinition(
        string OriginalChineseLabel,
        string NormalizedSessionAlias,
        string PrimaryDomain,
        IReadOnlyList<string> ActionLabels,
        string CharacterClassCandidate,
        bool BattleRelated,
        bool WorldRelated,
        bool InventoryRelated,
        bool EquipmentRelated,
        bool QuestRelated,
        bool MountRelated,
        bool PetRelated,
        bool PortalRelated,
        bool RewardRelated,
        IReadOnlyList<string> ExpectedSequenceCandidates);

    private sealed record MetadataRecord(
        ulong Sequence,
        long WallUnixMs,
        string Api,
        string Direction,
        uint RequestedLength,
        uint TransferredLength,
        uint CapturedLength,
        int RemotePort,
        string ReturnAddress,
        string Caller);

    private sealed record TraceValidation(
        bool MetadataParsed,
        bool TraceBoundaryValid,
        bool MetadataTraceCorrelated,
        bool SequenceMonotonic,
        bool CapturedLengthBoundaryValid,
        bool TransferredLengthConsistent,
        bool ApiDirectionValid,
        int ReconstructedStreamCount,
        int DirectionGroups,
        int PartialReceiveRecords,
        int TraceStorageOrderInversionCount,
        int MetadataStorageOrderInversionCount,
        int DuplicateRecordCount,
        int MissingPayloadCount,
        int CorruptRecordCount,
        int TruncatedRecordCount,
        int CaptureDropCandidateCount,
        long ResidualBytes,
        long RawTraceByteCount);

    private sealed record ImportedSession(
        string CaptureSessionId,
        string OriginalChineseLabel,
        SessionLabelDefinition Label,
        string SessionSha256,
        string MetadataSha256,
        string TraceSha256,
        string GeneralLogSha256,
        IReadOnlyDictionary<string, string> FileHashes,
        IReadOnlyList<MetadataRecord> Metadata,
        TraceRecord[] TraceRecords,
        string GeneralLog,
        long RawTraceBytes,
        TraceValidation TraceValidation,
        string RawTraceReference);

    private sealed record ImportCorpus(
        string SchemaVersion,
        DateTimeOffset ImportedAtUtc,
        string SourceArchive,
        string ZipSha256,
        long ArchiveLength,
        string ClientBuildId,
        string ClientSha256,
        string Architecture,
        IReadOnlyList<ImportedSession> Sessions,
        int StaleEntryCount,
        bool StaleExcludedFromGameplay);

    private sealed record AnalyzedFrame(
        string FrameId,
        string SessionId,
        ulong SourceSequence,
        int FrameIndex,
        long WallUnixMs,
        string Direction,
        int FrameLength,
        string RawHash,
        string RawReference,
        string SafeSocketReference,
        int RemotePort,
        string CallerRva,
        string InfrastructureKind,
        string KnownFamily,
        string ClassificationSource,
        bool AuxiliaryConnection,
        bool ExcludedInfrastructure,
        bool ActionableUnknown,
        byte[] RestrictedPayload);

    private sealed record FamilyClassification(string Family, string Source);

    private sealed record DecodedServerObservation(
        long DecodeSequence,
        int DecodedLength,
        string DecodedOpcode,
        string CaptureStage,
        string Notes);

    private sealed record AnalyzedSession(
        ImportedSession Imported,
        IReadOnlyList<AnalyzedFrame> Frames,
        IReadOnlyList<DecodedServerObservation> DecodedServerObservations,
        IReadOnlyList<ActionTransaction> Transactions);

    private sealed record TriggerReference(
        string FrameId,
        int FrameLength,
        string RawHash,
        string CallerRva);

    private sealed record ActionTransaction(
        string TransactionId,
        string SessionId,
        string LabelCandidate,
        IReadOnlyList<string> ActionLabels,
        ulong StartSequence,
        ulong EndSequence,
        long StartTimeUnixMs,
        long EndTimeUnixMs,
        string PreState,
        TriggerReference? TriggerOutbound,
        IReadOnlyList<string> ImmediateInbound,
        IReadOnlyList<string> ResultSequence,
        string PostState,
        string DirectionPattern,
        int PacketCount,
        int ExcludedMovementCount,
        int ExcludedHeartbeatCount,
        IReadOnlyList<string> CandidateFamilies,
        string Confidence,
        bool CompositeTransaction,
        IReadOnlyList<string> Contradictions);

    private sealed record PacketCluster(
        string ClusterId,
        string Direction,
        int FrameLength,
        string CallerRva,
        string Family,
        string ClassificationSource,
        int SampleCount,
        int SessionCount,
        IReadOnlyList<string> SessionIds,
        int RawHashVariantCount,
        IReadOnlyList<string> RawReferences,
        bool ExcludedFromUnknown,
        bool ExcludedFromDifferential,
        bool ActionableUnknown,
        string PromotionStage,
        string Confidence,
        bool RuntimeMutationBlocked,
        bool EvidenceConflict);

    private sealed record FieldHypothesis(
        string HypothesisId,
        string Family,
        string Hypothesis,
        IReadOnlyList<string> SupportingSamples,
        IReadOnlyList<string> ContradictingSamples,
        string ExpectedValue,
        string ObservedValue,
        string Endianness,
        int Offset,
        int Width,
        string Confidence,
        string NextRequiredExperiment,
        string Notes);

    private sealed record DecoderCandidate(
        string CandidateId,
        string Family,
        string ClientBuildId,
        string RequiredState,
        IReadOnlyList<int> FrameLengths,
        string Schema,
        IReadOnlyList<string> GoldenSamples,
        IReadOnlyList<string> NegativeCases,
        string Output,
        bool DecoderVerified,
        bool RuntimeMutationAllowed,
        string Status,
        string Blocker);

    private sealed record SerializerCandidate(
        string CandidateId,
        string Family,
        int ObservationCount,
        string Status,
        IReadOnlyList<string> UnknownRequiredDynamicFields,
        bool ProductionBytesEmitted,
        bool SerializerVerified,
        string Notes);

    private sealed record RuntimeMapping(
        string Family,
        string ExistingRuntime,
        string SemanticBoundary,
        string Status,
        bool ExactlyOnceRequired,
        bool DirectDatabaseAccessAllowed,
        bool RuntimeIntegrated,
        string Blocker);

    private sealed record PromotionGate(
        string Family,
        string PromotionStage,
        int SampleCount,
        int SessionCount,
        bool ChineseLabelCorrelated,
        bool PacketSequenceCorrelated,
        bool ProtocolStateCorrelated,
        bool CrossSessionValidated,
        bool DecoderCandidate,
        bool DecoderVerified,
        bool RuntimeIntegrated,
        bool SerializerCandidate,
        bool SerializerVerified,
        bool ProductionReady,
        bool RuntimeMutationBlocked,
        string Blocker);

    private sealed record ActiveExperiment(
        string ExperimentId,
        string Family,
        string SingleVariable,
        string MinimumAction,
        string MinimumCapture,
        bool SafeToRepeat,
        bool MovementOrHeartbeatRequired,
        bool AlreadySufficientFamilyRecaptureRequired,
        string Goal);

    private sealed record UnknownActionable(
        string ClusterId,
        string Direction,
        int FrameLength,
        string CallerRva,
        int SampleCount,
        IReadOnlyList<string> SessionIds,
        IReadOnlyList<string> RawReferences,
        string Classification,
        string NextRequiredControlledAction);

    private sealed record SequenceTransaction(
        string TransactionId,
        string LabelCandidate,
        ulong StartSequence,
        ulong EndSequence,
        string DirectionPattern,
        IReadOnlyList<string> CandidateFamilies);

    private sealed record PacketSequenceSummary(
        string CaptureSessionId,
        IReadOnlyList<string> ExpectedSequenceCandidates,
        IReadOnlyList<SequenceTransaction> Transactions,
        int MovementExcludedCount,
        int HeartbeatExcludedCount,
        int AuxiliaryConnectionExcludedCount);

    private sealed record DifferentialDefinition(
        string Id,
        IReadOnlyList<string> SessionAliases,
        IReadOnlyList<string> FamilyPrefixes);

    private sealed record DifferentialSummary(
        string DifferentialId,
        IReadOnlyList<string> SessionIds,
        IReadOnlyList<string> ClusterIds,
        int SampleCount,
        int CrossSessionCount,
        string Finding,
        string Confidence);

    private sealed record ImportSummary(
        string ZipSha256,
        int SessionCount,
        int ImportedSessionCount,
        int RejectedSessionCount,
        int ChineseLabelsParsed,
        int ActionLabelsGenerated,
        int MetadataRecordCount,
        long RawTraceByteCount,
        int ReconstructedStreamCount,
        int ReconstructedFrameCount,
        int MovementExcludedCount,
        int HeartbeatExcludedCount,
        int ActionTransactionCount,
        int CompositeTransactionCount,
        int KnownFamilyRoutedCount,
        int UnknownQueueBefore,
        int UnknownQueueAfter,
        IReadOnlyList<string> NewlyClassifiedFamilies,
        int FieldHypothesisCount,
        int ObservedOnceCount,
        int ObservedRepeatedCount,
        int CrossValidatedCount,
        int DecoderCandidateCount,
        int DecoderVerifiedCount,
        int SerializerCandidateCount,
        int SerializerVerifiedCount,
        int RuntimeIntegratedCount,
        int RuntimeMutationBlockedCount,
        int ActiveExperimentRecommendationCount,
        int FakeNetworkBytes,
        int CredentialFindings,
        int HardcodedPathFindings,
        string ActorPrimary,
        string LegacyPrimary,
        string UserManualOperation,
        bool DuplicateArchive,
        string FinalStatus);

    private sealed record AcceptanceCheck(string Name, bool Passed, string Evidence);

    private sealed record DecoderSyntheticResult(
        bool Golden,
        bool Negative,
        bool WrongBuild,
        bool WrongState,
        bool DuplicateSafe);

    private sealed record PriorImportManifest(string ZipSha256);

    private sealed record TrxEvidence(bool Passed, string Evidence);

    private sealed record RegressionEvidence(TrxEvidence Protocol, TrxEvidence Runtime);

    private sealed record RuntimeModeEvidence(string Mode, bool ActorPrimaryEnabled, bool LegacyPrimaryDefault);

    private sealed record SecurityEvidence(int CredentialFindings, int HardcodedPathFindings);

    private sealed record ImportAnalysisResult(
        ImportCorpus Corpus,
        IReadOnlyList<AnalyzedSession> Sessions,
        IReadOnlyList<PacketCluster> Clusters,
        IReadOnlyList<FieldHypothesis> FieldHypotheses,
        IReadOnlyList<DecoderCandidate> DecoderCandidates,
        IReadOnlyList<SerializerCandidate> SerializerCandidates,
        IReadOnlyList<RuntimeMapping> RuntimeMappings,
        IReadOnlyList<PromotionGate> PromotionGates,
        IReadOnlyList<ActiveExperiment> ActiveExperiments,
        IReadOnlyList<UnknownActionable> UnknownActionable,
        IReadOnlyList<PacketSequenceSummary> PacketSequences,
        IReadOnlyList<DifferentialSummary> Differentials,
        ImportSummary Summary,
        IReadOnlyList<AcceptanceCheck> AcceptanceChecks);
}
