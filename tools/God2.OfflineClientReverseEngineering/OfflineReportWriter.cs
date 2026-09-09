using System.Text;
using System.Text.Json;

namespace God2.OfflineClientReverseEngineering;

public static class OfflineReportWriter
{
    public static void Write(
        string workspaceRoot,
        StaticAnalysisSnapshot staticAnalysis,
        RuntimeCodeSnapshot runtime,
        DispatchRecoverySnapshot dispatch,
        ProtocolSchemaRecoveryResult protocol,
        KnowledgeRecoverySnapshot knowledge,
        MapRecoverySnapshot maps,
        RuntimeCaptureProvenance capture,
        JsonSerializerOptions options)
    {
        var reports = Path.Combine(workspaceRoot, "Reports");
        var artifacts = Path.Combine(workspaceRoot, "Artifacts", "OfflineClientReverseEngineering");
        Directory.CreateDirectory(reports);
        Directory.CreateDirectory(artifacts);
        var verification = OfflineVerificationEvidence.Load(workspaceRoot);
        TestSuiteEvidence Test(string name) => verification.Tests[name];
        string TestText(string name)
        {
            var item = Test(name);
            return item.Total is null
                ? $"{name}: {item.Status}"
                : $"{name}: {item.Passed:N0}/{item.Total:N0} {item.Status}; failed={item.Failed}; skipped={item.Skipped}; artifact={item.Artifact}";
        }
        var testsStatus = verification.AllCurrentTestsPass ? "PASS" : "EVIDENCE_INCOMPLETE_OR_NOT_CURRENT";
        var actorPrimary = verification.RuntimeMode.ActorPrimaryEnabled == false ? "NOT ENABLED" : verification.RuntimeMode.ActorPrimaryEnabled == true ? "ENABLED" : "UNVERIFIED";
        var legacyPrimary = verification.RuntimeMode.LegacyPrimaryDefault == true ? "DEFAULT" : verification.RuntimeMode.LegacyPrimaryDefault == false ? "NOT DEFAULT" : "UNVERIFIED";

        var domainCounts = string.Join("\n", knowledge.Domains.Select(item =>
            $"- {item.Domain}: indexed {item.IndexedRecordCount:N0}; declared sources {item.DeclaredRecordCount:N0}; import={item.CanDirectImportToGameplay}; {item.VerificationStatus}"));
        var tableRows = string.Join("\n", dispatch.Tables.Select(item =>
            $"| {item.State} | {item.DispatchKind} | {item.FunctionRva} | {item.JumpRva} | {item.TableRva} | {item.IndexTableRva ?? "-"} | {item.OpcodeCount} | {item.UniqueHandlerCount} | {item.DefaultOpcodeCount} |"));
        var familyList = "LoginHandshake, LoginAuthentication, CharacterLifecycle, WorldBootstrap, MapNavigation, PortalTransfer, Inventory, Equipment, Shop, Crafting, Quest, Party, Mount, Pet, Progression, BattleCommand, BattleResult";
        var evidenceBoundary = "Official-wire decoder and serializer candidates remain fail-closed. No candidate was promoted without exact dynamic fields and automated client acceptance.";

        WriteReport(reports, "OfflineClientReverseEngineering.Dispatch.md", $$"""
            # Offline Client Reverse Engineering — Dispatch

            Status: CLIENT STATIC PROTOCOL STRUCTURE RECOVERY {{(capture.CaptureAttested ? "PASS" : "RECOVERED_WITH_LEGACY_CAPTURE_PROVENANCE")}}

            Build lock: `{{staticAnalysis.ClientBuildId}}` / `{{staticAnalysis.ClientSha256}}`.

            The on-disk executable code is packed/transformed, so semantic disassembly is explicitly not inferred from its linear instruction stream. The dispatcher results below come from a hash-bound, same-process read-only (`PROCESS_VM_READ`) snapshot of the naturally unpacked code image. Capture provenance: {{capture.BuildAssociationStatus}}; session `{{capture.SessionId}}`; code SHA-256 `{{capture.CodeSha256}}`; length-table SHA-256 `{{capture.LengthTableSha256}}`. Client write access and patching were absent.

            | State | Structure | Function RVA | Jump RVA | Target table | Index table | Opcodes | Unique handlers | Default opcodes |
            |---|---|---:|---:|---:|---:|---:|---:|---:|
            {{tableRows}}

            Total state-specific registry rows: {{dispatch.StateSpecificRegistryEntryCount}}. Handler clusters: {{dispatch.UniqueStateHandlerCount}}. Existing decoded observations: {{runtime.DecodeObservationCount}} events, {{runtime.ObservedOpcodeCount}} opcodes, {{runtime.DecodeCallerClusterCount}} caller clusters. The registry preserves nested/state-dependent dispatch; it does not flatten same-valued opcodes across Login, World, and Battle.

            Outbound length table: VA `0x00894090`, {{dispatch.OutboundLengthPolicies.Count}} non-zero opcode policies ({{dispatch.FixedLengthPolicyCount}} fixed, {{dispatch.VariableLengthPolicyCount}} variable). Movement and Heartbeat were not modified.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.ReadersWriters.md", $$"""
            # Offline Client Reverse Engineering — Readers and Writers

            Packet reader structures identified: 3.

            - Central framing reader: RVA `0x0007C0B0`, called at `0x0007E91B`.
            - Decode/decrypt/decompress/checksum reader: RVA `0x00078D70`, called at `0x00078A48`.
            - Version/bootstrap parser: RVA `0x0007CE90`, called at `0x0007E7A2`.

            Packet writer structures identified: 3.

            - Length/checksum/encryption frame builder: RVA `0x00078EA0`.
            - Contextual writer: RVA `0x00078F40`.
            - Opcode envelope builder: RVA `0x0007C2B0`; transport write caller `0x0007BB68`.

            Encryption boundary: conditional rolling previous-byte transform using the build-local key state. Inbound and inverse outbound boundaries are structurally identified; key semantics remain build-local.

            Compression boundary: inbound signed-length branch with RLE-style control (`0xC0` high bits, low-six-bit count + 1). The outbound compression decision remains EvidenceBlocked.

            Framing is a little-endian 16-bit length with payload checksum. {{evidenceBoundary}}
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.ProtocolFamilies.md", $$"""
            # Offline Client Reverse Engineering — Protocol Families

            Status: PROTOCOL FAMILY GENERALIZATION PASS at the semantic/schema layer; official-wire promotion remains evidence-gated.

            {{protocol.PacketFamilyCount}} packet families: {{familyList}}.

            Every schema includes build identity, allowed protocol state, direction, framing/length policy, semantic fields, unknown-field preservation, evidence, and independent decoder/serializer gates. Generic decoders only create semantic commands; they cannot mutate HP, MP, inventory, equipment, currency, quest, experience, or level.

            Battle command actions: BasicAttack, Skill, Item, Defend, Flee, Formation, PositionSwap. Result schemas include DamageResult, HealingResult, StatusApply, StatusRemove, Death, Revive, RoundEnd, Settlement/Reward, and WorldResume projections.

            Movement and Heartbeat remain excluded and unchanged. Fake Network Bytes: {{protocol.FakeNetworkBytes}}.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.BattleSkill.md", $$"""
            # Offline Client Reverse Engineering — Battle and Skill

            BattleCommandEnvelope fields: Actor, ActionType, SkillId/ItemId, Target/TargetList, Round, CommandWindow, Formation/Position, Flags, and offset-indexed preserved unknown fields.

            Protocol skill families ({{protocol.SkillFamilyCount}}): BasicAttack, SingleTargetDamage, MultiTargetDamage, SingleTargetHeal, MultiTargetHeal, Buff, Debuff, StatusRemove, ResourceRestore, Revive, Formation, PositionSwap, Summon, PetCommand, Passive, TriggeredEffect.

            The existing catalog-driven runtime continues to cover its 20 effect families. No per-skill protocol handler was added. Generic skill mapping is `SkillCommand + SkillId + target + catalog + existing SkillActionRuntime`.

            DamageResult carries actor/target/skill, amount, absorbed, critical, HP-after, and event sequence. HealingResult carries amount, overheal, HP-after, and event sequence. Status results carry source/target/status, stacks, duration/expiry or removal reason. Settlement carries battle termination, participants, EXP/items/currency, quest events, outbox, state version, and idempotency key.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Codecs.md", $$"""
            # Offline Client Reverse Engineering — Codecs

            Decoder candidates: {{protocol.DecoderCandidateCount}}; DecoderVerified: {{protocol.DecoderVerifiedCount}}. Serializer candidates: {{protocol.SerializerCandidateCount}}; SerializerVerified: {{protocol.SerializerVerifiedCount}}. Official-wire RuntimeIntegrated: 0.

            Implemented executable gates cover build mismatch, state mismatch, decoded-length mismatch, malformed actor data, stable semantic hashing, and deep-copy preservation of unknown fields. Serializer input is restricted to authoritative runtime result objects and rejects captured/fixed packet identifiers.

            Serializer production output remains disabled because fully automated official-client acceptance with multiple dynamic values is not available. Production network bytes emitted: 0.

            {{evidenceBoundary}}
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Content.md", $$"""
            # Offline Client Reverse Engineering — Official Content Knowledge

            Status: catalog/index recovery PASS; unresolved gameplay semantics remain individually EvidenceBlocked.

            Domains: {{knowledge.DomainCount}}. Source categories: {{knowledge.SourceCategoryCount}}. Source-declared records: {{knowledge.DeclaredRecordCount:N0}}. Domain/entity indexes: {{knowledge.IndexedEntityCount:N0}}. Cross-reference fields: {{knowledge.CrossReferenceCount:N0}}.

            {{domainCounts}}

            Each `Knowledge/<Domain>/catalog.json` retains source artifacts, hashes, recovered/missing fields, verification status, import eligibility, entity IDs, names, and ID/reference edges. `Knowledge/knowledge-graph.json` is the combined machine-readable graph.

            EXP/level/growth: existing server progression runtime remains PASS, but an authoritative official-client EXP/growth table was not recovered and remains EvidenceBlocked. Effects/status, mount/pet, crafting and some item/skill semantics likewise remain explicitly unpromoted.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Map.md", $$"""
            # Offline Client Reverse Engineering — Map, Collision, Navigation

            Official `.mbd` candidates: {{maps.CandidateFileCount}}; parsed: {{maps.ParsedMapCount}}; failures: {{maps.FailedMapCount}}. Offline A*: {{maps.AStarPassedCount}}/{{maps.ParsedMapCount}} PASS. Total decoded walkable cells: {{maps.TotalWalkableCells:N0}}.

            Recovered format: `MBD v1.2`, 32-bit macro width/height, macro pointer table, 21×21 cells per block, 8 bytes per cell, 20-cell block stride with shared border. Global grid size is `(macroWidth × 20 + 1) × (macroHeight × 20 + 1)`. The all-`FFFF` first three words form the non-walkable sentinel used by the parser.

            Map dimensions and discrete collision/walkability are recovered. Coordinate world origin/scale, portal trigger coordinates, NPC/monster spawn coordinates, and verified beginner-spawn-to-exit endpoints are not present in the recovered evidence and remain EvidenceBlocked. No screenshots, OCR, hidden walking, or Movement protocol changes were used.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Headless.md", $$"""
            # Offline Client Reverse Engineering — Headless Integration

            Status: HEADLESS GAMEPLAY INTEGRATION {{Test("Headless").Status}} for the existing semantic runtime.

            {{TestText("Headless")}}. No PASS is inferred when the TRX is missing, stale, skipped, or failed.

            Advanced fast evidence: {{verification.Advanced.Status}}; {{verification.Advanced.Exhaustive ?? 0:N0}} bounded exhaustive cases, {{verification.Advanced.Property ?? 0:N0}} generated property cases, {{verification.Advanced.DifferentialEvents ?? 0:N0}} differential events, {{verification.Advanced.Schedules ?? 0:N0}} deterministic schedules, failures={{verification.Advanced.Failures?.ToString() ?? "unknown"}}; artifact `{{verification.Advanced.Artifact}}`. Historical counts are not promoted when the artifact is stale.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Transactions.md", $$"""
            # Offline Client Reverse Engineering — Cross-Module Transactions

            Status: CROSS-MODULE GAMEPLAY TRANSACTION {{Test("Runtime").Status}}.

            Added one aggregate boundary: BeforeRewardCommit → Battle Settlement → Reward → Quest Completion → Inventory → EXP → Level-up → Outbox → Commit. Copy-on-write candidate state is committed once; no intermediate module state is published.

            Directed tests cover pre-commit rollback, invalid/version conflicts, changed duplicates, duplicate concurrency, lost responses, crash-after-prepare recovery, stale-prepare rejection, immutable publication, monotonic progression, missing-map blocks and bounded allocations. Evidence source: {{TestText("Runtime")}}.

            MariaDB evidence: {{verification.MariaDb.Status}}; cases={{verification.MariaDb.Cases?.ToString() ?? "unknown"}}, rollbacks={{verification.MariaDb.Rollbacks?.ToString() ?? "unknown"}}, recoveries={{verification.MariaDb.Recovery?.ToString() ?? "unknown"}}, exactly-once={{verification.MariaDb.ExactlyOnce?.ToString() ?? "unknown"}}, inconsistencies={{verification.MariaDb.Inconsistency?.ToString() ?? "unknown"}}; artifact `{{verification.MariaDb.Artifact}}`. A stale artifact is retained only as historical evidence.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Deferred.md", $$"""
            # Offline Client Reverse Engineering — EvidenceBlocked / Deferred

            - 10 decoder candidates remain unverified because required dynamic official-wire fields are encrypted/opaque or semantically unresolved.
            - 41 serializer candidates remain blocked pending writer-field proof and fully automated official-client acceptance across multiple dynamic values.
            - Login credential/character lifecycle field semantics are not inferred from opaque frames.
            - Official EXP/level/growth tables are not recovered.
            - NPC, monster, portal and beginner spawn/exit coordinates are not verified.
            - World coordinate origin/scale and outbound compression decision are not verified.
            - Packed on-disk code cannot be treated as semantic disassembly; the read-only unpacked snapshot is build-locked evidence.

            None of these items authorize manual capture, user gameplay, unknown live packets, memory writes, patching, ActorPrimary, or guessed network bytes.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.TestResults.md", $$"""
            # Offline Client Reverse Engineering — Test Results

            Overall test evidence: {{testsStatus}}. Latest source timestamp: {{verification.LatestSourceUtc:O}}. Source fingerprint: `{{verification.SourceFingerprintSha256}}`.

            - {{TestText("Headless")}}
            - {{TestText("Protocol")}}
            - {{TestText("Runtime")}}
            - {{TestText("Full")}}
            - Advanced fast: {{verification.Advanced.Status}}; exhaustive={{verification.Advanced.Exhaustive?.ToString() ?? "unknown"}}; property={{verification.Advanced.Property?.ToString() ?? "unknown"}}; differential={{verification.Advanced.DifferentialEvents?.ToString() ?? "unknown"}}; schedules={{verification.Advanced.Schedules?.ToString() ?? "unknown"}}; failures={{verification.Advanced.Failures?.ToString() ?? "unknown"}}.
            - MariaDB: {{verification.MariaDb.Status}}; cases={{verification.MariaDb.Cases?.ToString() ?? "unknown"}}; inconsistencies={{verification.MariaDb.Inconsistency?.ToString() ?? "unknown"}}.
            - Resource soak: {{verification.Resources.Status}}; duration={{verification.Resources.DurationSeconds?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}} seconds; loops={{verification.Resources.CompletedLoops?.ToString() ?? "unknown"}}; actor/session/DB leaks={{verification.Resources.ActorLeaks?.ToString() ?? "unknown"}}/{{verification.Resources.SessionLeaks?.ToString() ?? "unknown"}}/{{verification.Resources.DbConnectionLeaks?.ToString() ?? "unknown"}}.
            - Security: {{verification.Security.Status}}; credential findings={{verification.Security.CredentialFindings?.ToString() ?? "unknown"}}; hardcoded paths={{verification.Security.HardcodedPathFindings?.ToString() ?? "unknown"}}; fake network bytes={{verification.Security.FakeNetworkBytes?.ToString() ?? "unknown"}}.
            """);

        WriteReport(reports, "OfflineClientReverseEngineering.Final.md", $$"""
            # Offline-First Client Reverse Engineering — Final

            - CLIENT STATIC PROTOCOL STRUCTURE RECOVERY {{(capture.CaptureAttested ? "PASS" : "RECOVERED WITH LEGACY CAPTURE PROVENANCE")}}
            - PROTOCOL FAMILY GENERALIZATION PASS (semantic/schema layer; official-wire codecs evidence-gated)
            - OFFICIAL CLIENT CONTENT KNOWLEDGE RECOVERY PARTIAL (catalog/index/collision recovered; listed semantic/coordinate gaps remain)
            - HEADLESS GAMEPLAY INTEGRATION {{Test("Headless").Status}}
            - CROSS-MODULE GAMEPLAY TRANSACTION {{Test("Runtime").Status}}

            Overall: OFFLINE-FIRST CLIENT REVERSE ENGINEERING + PROTOCOL GENERALIZATION + CONTENT RECOVERY + HEADLESS INTEGRATION PHASE 1 PARTIAL — evidence-gated official-wire codecs and unresolved client content semantics prevent a full PASS.

            Safety: read-only client memory only; binary/memory writes 0; Fake Network Bytes {{protocol.FakeNetworkBytes}}; ActorPrimary {{actorPrimary}}; LegacyPrimary {{legacyPrimary}}; Movement/Heartbeat unchanged; manual operation NOT REQUIRED. Runtime mode source: `{{verification.RuntimeMode.Artifact}}` ({{verification.RuntimeMode.Status}}). Security artifact status: {{verification.Security.Status}}.
            """);

        var final = new
        {
            SchemaVersion = "offline-client-reverse-engineering-v1",
            Status = "PARTIAL_EVIDENCE_GATED",
            staticAnalysis.ClientBuildId,
            staticAnalysis.ClientSha256,
            DispatchEntries = dispatch.StateSpecificRegistryEntryCount,
            HandlerClusters = dispatch.UniqueStateHandlerCount,
            PacketReaders = 3,
            PacketWriters = 3,
            protocol.PacketFamilyCount,
            protocol.DecoderCandidateCount,
            protocol.DecoderVerifiedCount,
            protocol.SerializerCandidateCount,
            protocol.SerializerVerifiedCount,
            OfficialWireRuntimeIntegratedCount = 0,
            knowledge.DomainCount,
            knowledge.SourceCategoryCount,
            knowledge.DeclaredRecordCount,
            knowledge.IndexedEntityCount,
            MapCandidates = maps.CandidateFileCount,
            MapParsed = maps.ParsedMapCount,
            MapAStarPassed = maps.AStarPassedCount,
            VerificationStatus = testsStatus,
            verification.LatestSourceUtc,
            verification.SourceFingerprintSha256,
            Tests = verification.Tests,
            AdvancedFast = verification.Advanced,
            MariaDb = verification.MariaDb,
            Resources = verification.Resources,
            Capture = capture,
            Safety = new
            {
                FakeNetworkBytes = protocol.FakeNetworkBytes,
                ClientWriteAccessUsed = false,
                ClientBinaryModified = false,
                SecurityEvidence = verification.Security,
                RuntimeMode = verification.RuntimeMode,
                ActorPrimary = actorPrimary,
                LegacyPrimary = legacyPrimary,
                UserManualOperation = "NOT REQUIRED"
            },
            RemainingEvidenceBlocked = new[]
            {
                "official-wire dynamic decoder fields",
                "official-client serializer acceptance",
                "official EXP/level/growth tables",
                "NPC/monster/portal/spawn coordinates",
                "world coordinate origin/scale",
                "outbound compression decision"
            }
        };
        File.WriteAllText(Path.Combine(artifacts, "final-summary.json"), JsonSerializer.Serialize(final, options));
        File.WriteAllText(Path.Combine(artifacts, "run-manifest.json"), JsonSerializer.Serialize(new
        {
            final.SchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            AnalysisMode = "offline-first + build-locked read-only runtime memory",
            Inputs = new[]
            {
                "God2_opt.exe SHA256 build lock",
                "runtime-capture-manifest.json with input hashes and PID association",
                "official extracted client resources",
                "TRX and advanced verification artifacts with current/stale status"
            },
            Outputs = Directory.EnumerateFiles(reports, "OfflineClientReverseEngineering.*.md")
                .Select(path => Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray()
        }, options));
    }

    private static void WriteReport(string root, string fileName, string content)
    {
        var normalized = string.Join(Environment.NewLine, content.Split('\n').Select(line => line.TrimEnd())) + Environment.NewLine;
        File.WriteAllText(Path.Combine(root, fileName), normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
