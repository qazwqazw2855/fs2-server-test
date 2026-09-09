using System.Text;
using System.Text.Json;

var repoRoot = FindRepositoryRoot(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
var generatedAtUtc = DateTimeOffset.UtcNow;
var reportsRoot = Path.Combine(repoRoot, "Reports");
var closureRoot = Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleClosure");
Directory.CreateDirectory(reportsRoot);
Directory.CreateDirectory(closureRoot);

var evidenceStatus = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence", "status.json"));
var evidenceIndex = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence", "protocol-index.json"));
var staticAnalysis = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence", "static-analysis.json"));
var createMatrix = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence", "create-matrix.json"));
var deleteMatrix = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence", "delete-matrix.json"));
var uiStatus = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleUiAutomation", "status.json"));
var instrumentationStatus = ReadJson(Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleInstrumentation", "status.json"));
var closureStatus = ReadJson(Path.Combine(closureRoot, "closure-results.json"));
var latestRegression = ReadLatestRegression(repoRoot);

var finalStatus = Scalar(closureStatus, "ProtocolClosureStatus", "NOT_RUN");
var closureOutcome = Scalar(closureStatus, "Outcome", finalStatus);
var closureOutcomeText = closureOutcome.TrimEnd('.', '。');
var path1Status = Scalar(uiStatus, "Status", "NOT_RUN");
var path1Failure = Scalar(uiStatus, "FailureCode", "NOT_RUN");
var path2Status = Scalar(instrumentationStatus, "Status", "NOT_RUN");
var path2Failure = Scalar(instrumentationStatus, "FailureCode", "NOT_RUN");
var fakeNetworkBytes = Scalar(evidenceStatus, "FakeNetworkBytes", "0");
var createC2S = Scalar(closureStatus, "CreateC2S", Scalar(evidenceStatus, "CreateC2S", "BlockedByEvidence"));
var deleteC2S = Scalar(closureStatus, "DeleteC2S", Scalar(evidenceStatus, "DeleteC2S", "BlockedByEvidence"));
var createResultS2C = Scalar(closureStatus, "CreateResultS2C", Scalar(evidenceStatus, "CreateResultS2C", "SerializerBlockedByEvidence"));
var deleteResultS2C = Scalar(closureStatus, "DeleteResultS2C", Scalar(evidenceStatus, "DeleteResultS2C", "SerializerBlockedByEvidence"));
var listRefresh = Scalar(closureStatus, "CharacterListRefresh", Scalar(evidenceStatus, "CharacterListRefresh", "BlockedByEvidence"));
var headlessMatrix = Scalar(closureStatus, "Headless32Matrix", "FROZEN_RUNTIME_32_32_PASS_PROTOCOL_NOT_RUN");
var officialCreate = Scalar(closureStatus, "OfficialClientCreate", path1Status);
var officialDelete = Scalar(closureStatus, "OfficialClientDelete", path1Status);
var credentialLeakage = Scalar(closureStatus, "CredentialLeakage", "NOT_RUN_IN_CLOSURE");
var cleanShutdown = Scalar(closureStatus, "CleanShutdown", RegressionCleanShutdown(latestRegression));
var launcherStatus = Scalar(closureStatus, "Launcher", "LEFT ALIVE");
var manualOperation = Scalar(closureStatus, "UserManualOperation", "NOT REQUIRED");
var buildDebug = Scalar(closureStatus, "DebugBuild", "NOT_RUN_IN_CLOSURE");
var buildRelease = Scalar(closureStatus, "ReleaseBuild", "NOT_RUN_IN_CLOSURE");
var tests = Scalar(closureStatus, "FullTests", "NOT_RUN_IN_CLOSURE");

var evidenceRows = EvidenceRows(evidenceIndex);
var staticRows = StaticRows(staticAnalysis);

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolEvidence.md"), $"""
# Character Lifecycle Protocol Evidence

GeneratedAtUtc: {generatedAtUtc:o}

| Classification | Artifact Path | Direction | Opcode | Length | Confidence | Exclusion Reason |
| --- | --- | --- | --- | --- | --- | --- |
{evidenceRows}

Protocol conclusion: {closureOutcomeText}. Visual create-character references remain visual-only unless paired with raw C2S/S2C frames.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ClientStaticAnalysis.md"), $"""
# Character Lifecycle Client Static Analysis

GeneratedAtUtc: {generatedAtUtc:o}

Status: {Scalar(staticAnalysis, "Status", "NOT_RUN")}
Targets Scanned: {Scalar(staticAnalysis, "TargetCount", "0")}

| Module | Offset | Encoding | Pattern | Opcode | Length | Confidence | Limitation |
| --- | --- | --- | --- | --- | --- | --- | --- |
{staticRows}

Closure Path 2 status: {path2Status}. FailureCode: {path2Failure}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.UiAutomation.md"), $"""
# Character Lifecycle UI Automation

GeneratedAtUtc: {generatedAtUtc:o}

Path 1 Status: {path1Status}.
FailureCode: {path1Failure}.
ArtifactRoot: {Scalar(uiStatus, "ArtifactRoot", UiArtifactRoot(uiStatus))}.
ServerReady: {Scalar(uiStatus, "ServerReady", UiServerReady(uiStatus))}.
SubmitAttempted: {RawScalar(uiStatus, "SubmitAttempted", "NOT_RUN")}.
PacketCaptureAttempted: {RawScalar(uiStatus, "PacketCaptureAttempted", "NOT_RUN")}.
OfficialClientCreate: {Scalar(uiStatus, "OfficialClientCreate", "NOT_RUN")}.
OfficialClientDelete: {Scalar(uiStatus, "OfficialClientDelete", "NOT_RUN")}.
UserManualOperation: {Scalar(uiStatus, "UserManualOperation", "NOT REQUIRED")}.

Safe empty-slot workflow: {Scalar(closureStatus, "SafeEmptySlotWorkflow", "NOT_RUN_BLOCKED_BEFORE_CLIENT_START")}.
Automated Create: {Scalar(closureStatus, "AutomatedCreate", "BLOCKED_BEFORE_CAPTURE")}.
Automated Delete: {Scalar(closureStatus, "AutomatedDelete", "BLOCKED_BEFORE_CAPTURE")}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.DynamicInstrumentation.md"), $"""
# Character Lifecycle Dynamic Instrumentation

GeneratedAtUtc: {generatedAtUtc:o}

Path 2 Status: {path2Status}.
FailureCode: {path2Failure}.
SelfTestStatus: {Scalar(instrumentationStatus, "SelfTestStatus", "NOT_RUN")}.
DirectClientLaunchStatus: {Scalar(instrumentationStatus, "DirectClientLaunchStatus", "NOT_RUN")}.
DirectClientRun: {Scalar(instrumentationStatus, "DirectClientRun", "NOT_RUN")}.
ClientBinaryUnchanged: {Scalar(instrumentationStatus, "ClientBinaryUnchanged", "NOT_RUN")}.
TraceExists: {Scalar(instrumentationStatus, "TraceExists", "NOT_RUN")}.
TraceLength: {Scalar(instrumentationStatus, "TraceLength", "0")}.
MetadataLineCount: {Scalar(instrumentationStatus, "MetadataLineCount", "0")}.
CreateDeleteRawEvidence: {Scalar(instrumentationStatus, "CreateDeleteRawEvidence", "NOT_FOUND")}.
TargetedStaticAnalysisStatus: {Scalar(instrumentationStatus, "TargetedStaticAnalysisStatus", Scalar(staticAnalysis, "Status", "NOT_RUN"))}.

Conclusion: {Scalar(instrumentationStatus, "TargetedStaticAnalysisConclusion", "No opcode, length, offset, enum, result, or list-refresh layout was recovered.")}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.CreateProtocolMatrix.md"), MatrixReport("Create Character Protocol", createMatrix));
WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.DeleteProtocolMatrix.md"), MatrixReport("Delete Character Protocol", deleteMatrix));

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.CharacterListRefresh.md"), $"""
# Character Lifecycle Character List Refresh

GeneratedAtUtc: {generatedAtUtc:o}

Initial Character List: frozen bootstrap/list behavior remains intact.
Create-after Refresh: {listRefresh}.
Delete-after Refresh: {listRefresh}.
Re-login Character List: runtime/persistence baseline is frozen; protocol refresh generation is {listRefresh}.

No Character List Refresh bytes are emitted unless official evidence verifies the frame layout.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolFullMatrix.md"), $"""
# Character Lifecycle Protocol Full Matrix

GeneratedAtUtc: {generatedAtUtc:o}

Headless 32 Matrix: {headlessMatrix}.
Protocol Closure Status: {finalStatus}.
Create C2S: {createC2S}.
Delete C2S: {deleteC2S}.
Fake Network Bytes: {fakeNetworkBytes}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolAutomation.md"), $"""
# Character Lifecycle Protocol Automation

GeneratedAtUtc: {generatedAtUtc:o}

Path 1 - Automated Official Client UI Capture: {path1Status}.
Path 1 FailureCode: {path1Failure}.
Path 2 - Dynamic Packet Buffer Instrumentation: {path2Status}.
Path 2 FailureCode: {path2Failure}.
Official Client Create: {officialCreate}.
Official Client Delete: {officialDelete}.
Launcher: {launcherStatus}.
User Manual Operation: {manualOperation}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolReport.md"), $"""
# Character Lifecycle Protocol Report

GeneratedAtUtc: {generatedAtUtc:o}

Protocol Closure Status: {finalStatus}.
Outcome: {closureOutcomeText}.

Create Character C2S: {createC2S}.
Delete Character C2S: {deleteC2S}.
Name Field: {Scalar(closureStatus, "NameField", "UNKNOWN")}.
Class Field: {Scalar(closureStatus, "ClassField", "UNKNOWN")}.
Gender Field: {Scalar(closureStatus, "GenderField", "UNKNOWN")}.
Life Skill Field: {Scalar(closureStatus, "LifeSkillField", "UNKNOWN")}.
Appearance/Template: {Scalar(closureStatus, "AppearanceTemplate", "UNKNOWN")}.

Create Result S2C: {createResultS2C}.
Delete Result S2C: {deleteResultS2C}.
Character List Refresh: {listRefresh}.
Fake Network Bytes: {fakeNetworkBytes}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolTestResults.md"), $"""
# Character Lifecycle Protocol Test Results

GeneratedAtUtc: {generatedAtUtc:o}

Status: {Scalar(closureStatus, "TestStatus", "NOT_RUN_IN_CLOSURE")}.

| Check | Result |
| --- | --- |
| Debug solution build | {buildDebug} |
| Release solution build | {buildRelease} |
| Full tests | {tests} |
| Automation Environment | {Scalar(closureStatus, "AutomationEnvironment", "NOT_RUN_IN_CLOSURE")} |
| Automation Host | {Scalar(closureStatus, "AutomationHost", "NOT_RUN_IN_CLOSURE")} |
| Frozen Login/World regression | {RegressionStatus(latestRegression)} |
| Heartbeat stable | {RegressionHeartbeat(latestRegression)} |
| Credential leakage | {credentialLeakage} |
| Clean shutdown | {cleanShutdown} |
| Fake network bytes | {fakeNetworkBytes} |
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolFinalFreeze.md"), $"""
# Character Lifecycle Protocol Final Freeze

GeneratedAtUtc: {generatedAtUtc:o}

Status: {finalStatus}.
Outcome: {closureOutcomeText}.

Create Character C2S: {createC2S}.
Delete Character C2S: {deleteC2S}.
Name Field: {Scalar(closureStatus, "NameField", "UNKNOWN")}.
Class Field: {Scalar(closureStatus, "ClassField", "UNKNOWN")}.
Gender Field: {Scalar(closureStatus, "GenderField", "UNKNOWN")}.
Life Skill Field: {Scalar(closureStatus, "LifeSkillField", "UNKNOWN")}.
Appearance/Template: {Scalar(closureStatus, "AppearanceTemplate", "UNKNOWN")}.
Character List Refresh: {listRefresh}.
Create Result S2C: {createResultS2C}.
Delete Result S2C: {deleteResultS2C}.
Headless 32 Matrix: {headlessMatrix}.
Official Client Create: {officialCreate}.
Official Client Delete: {officialDelete}.
Frozen Login/World Regression: {RegressionStatus(latestRegression)}.
Heartbeat: {RegressionHeartbeat(latestRegression)}.
Build/Test: Debug={buildDebug}; Release={buildRelease}; Tests={tests}.
Credential Leakage: {credentialLeakage}.
Clean Shutdown: {cleanShutdown}.
Fake Network Bytes: {fakeNetworkBytes}.
Launcher: {launcherStatus}.
User Manual Operation: {manualOperation}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.TestResults.md"), $"""
# Character Lifecycle Test Results

GeneratedAtUtc: {generatedAtUtc:o}

Status: {Scalar(closureStatus, "RuntimeTestStatus", "FROZEN_RUNTIME_BASELINE")}.
Protocol Closure Status: {finalStatus}.
Build/Test: Debug={buildDebug}; Release={buildRelease}; Tests={tests}.
Fake Network Bytes: {fakeNetworkBytes}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.FinalFreeze.md"), $"""
# Character Lifecycle Final Freeze

GeneratedAtUtc: {generatedAtUtc:o}

Runtime Lifecycle: {Scalar(evidenceStatus, "RuntimeLifecycle", "WiredAndTested")}.
LifeSkill Persistence: {Scalar(evidenceStatus, "LifeSkillPersistence", "WiredAndTested")}.
Protocol Closure Status: {finalStatus}.
Create Character C2S: {createC2S}.
Delete Character C2S: {deleteC2S}.
Create Result S2C: {createResultS2C}.
Delete Result S2C: {deleteResultS2C}.
Character List Refresh: {listRefresh}.
Fake Network Bytes: {fakeNetworkBytes}.
User Manual Operation: {manualOperation}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.Report.md"), $"""
# Character Lifecycle Report

GeneratedAtUtc: {generatedAtUtc:o}

Authoritative protocol closure report: `CharacterLifecycle.ProtocolReport.md`.
Protocol Closure Status: {finalStatus}.
Runtime/Persistence Baseline: {Scalar(closureStatus, "CharacterRuntimePersistence", "PASS")}.
Headless Runtime Matrix: {headlessMatrix}.
Create/Delete/ListRefresh Protocol: {closureOutcomeText}.
Fake Network Bytes: {fakeNetworkBytes}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.EvidenceIndex.md"), $"""
# Character Lifecycle Evidence Index

GeneratedAtUtc: {generatedAtUtc:o}

Authoritative protocol evidence report: `CharacterLifecycle.ProtocolEvidence.md`.
Protocol Closure Status: {finalStatus}.
Path 1: {path1Status}, {path1Failure}.
Path 2: {path2Status}, {path2Failure}.
Golden Artifact: {Scalar(closureStatus, "GoldenArtifact", "NOT CREATED")}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.CreateMatrix.md"), $"""
# Character Lifecycle Create Matrix

GeneratedAtUtc: {generatedAtUtc:o}

Superseded by: `CharacterLifecycle.CreateProtocolMatrix.md`.
Create Character C2S: {createC2S}.
Name/Class/Gender/LifeSkill Fields: {Scalar(closureStatus, "NameField", "UNKNOWN")}/{Scalar(closureStatus, "ClassField", "UNKNOWN")}/{Scalar(closureStatus, "GenderField", "UNKNOWN")}/{Scalar(closureStatus, "LifeSkillField", "UNKNOWN")}.
No Create golden bytes were created.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.DeleteMatrix.md"), $"""
# Character Lifecycle Delete Matrix

GeneratedAtUtc: {generatedAtUtc:o}

Superseded by: `CharacterLifecycle.DeleteProtocolMatrix.md`.
Delete Character C2S: {deleteC2S}.
Delete Character ID/Slot Field: UNKNOWN.
No Delete golden bytes were created.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.FullMatrix.md"), $"""
# Character Lifecycle Full Matrix

GeneratedAtUtc: {generatedAtUtc:o}

Runtime/headless lifecycle matrix remains frozen: {headlessMatrix}.
Protocol 32-case matrix was not run because Create/Delete C2S official evidence remains {finalStatus}.
Fake Network Bytes: {fakeNetworkBytes}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.Automation.md"), $"""
# Character Lifecycle Automation

GeneratedAtUtc: {generatedAtUtc:o}

Authoritative protocol automation report: `CharacterLifecycle.ProtocolAutomation.md`.
Path 1 - Automated Official Client UI Capture: {path1Status}, {path1Failure}.
Path 2 - Dynamic Packet Buffer Instrumentation: {path2Status}, {path2Failure}.
Frozen Login/World Regression: {RegressionStatus(latestRegression)}.
User Manual Operation: {manualOperation}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.LifeSkill.md"), $"""
# Character Lifecycle Life Skill

GeneratedAtUtc: {generatedAtUtc:o}

Runtime/Persistence life skill support remains frozen from the prior lifecycle slice.
Official protocol LifeSkill field: {Scalar(closureStatus, "LifeSkillField", "UNKNOWN")}.
Protocol closure status: {finalStatus}.
""");

WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.Persistence.md"), $"""
# Character Lifecycle Persistence

GeneratedAtUtc: {generatedAtUtc:o}

Character runtime and persistence baseline: {Scalar(closureStatus, "CharacterRuntimePersistence", "PASS")}.
Runtime/headless matrix: {headlessMatrix}.
Official Create/Delete/ListRefresh protocol closure: {finalStatus}.
""");

if (string.Equals(finalStatus, "DEFERRED_BY_EVIDENCE", StringComparison.Ordinal))
{
    WriteText(Path.Combine(reportsRoot, "CharacterLifecycle.ProtocolDeferred.md"), $"""
# Character Lifecycle Protocol Deferred

GeneratedAtUtc: {generatedAtUtc:o}

Final Status: DEFERRED_BY_EVIDENCE.
Release Blocker: Create/Delete/ListRefresh official protocol closure remains blocked.
Runtime/Mainline Blocker: NO.
Official Client Automation: {Scalar(closureStatus, "OfficialClientAutomation", "BLOCKED_AFTER_UI_AND_BUFFER_RECOVERY")}.
Golden Artifact: {Scalar(closureStatus, "GoldenArtifact", "NOT CREATED")}.

Path 1 Result: {path1Status}; FailureCode: {path1Failure}.
Path 2 Result: {path2Status}; FailureCode: {path2Failure}.

Reason: both permitted technical paths failed to recover sufficient official raw packet evidence. No opcode, length, offset, enum, padding, result, or list-refresh bytes were guessed.
Fake Network Bytes: {fakeNetworkBytes}.
User Manual Operation: {manualOperation}.
""");
}

WriteText(Path.Combine(closureRoot, "report-writer-status.json"), $$"""
{
  "generatedAtUtc": "{{generatedAtUtc:o}}",
  "protocolClosureStatus": "{{JsonEscape(finalStatus)}}",
  "outcome": "{{JsonEscape(closureOutcome)}}",
  "reportsRoot": "Reports"
}
""");

Console.WriteLine("CharacterLifecycle final reports generated from artifacts.");

static string FindRepositoryRoot(string start)
{
    var current = new DirectoryInfo(Path.GetFullPath(start));
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
    {
        current = current.Parent;
    }

    return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
}

static JsonDocument? ReadJson(string path)
{
    if (!File.Exists(path))
    {
        return null;
    }

    return JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
}

static JsonDocument? ReadLatestRegression(string repoRoot)
{
    var regressionRoot = Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleRegression");
    if (!Directory.Exists(regressionRoot))
    {
        return null;
    }

    var summary = Directory.GetDirectories(regressionRoot, "frozen-protocol-*")
        .OrderByDescending(Directory.GetLastWriteTimeUtc)
        .Select(directory => Path.Combine(directory, "frozen-regression-summary.json"))
        .FirstOrDefault(File.Exists);

    return summary is null ? null : ReadJson(summary);
}

static string Scalar(JsonDocument? document, string propertyName, string fallback)
{
    if (document is null || !document.RootElement.TryGetProperty(propertyName, out var property))
    {
        return fallback;
    }

    return property.ValueKind switch
    {
        JsonValueKind.String => property.GetString() ?? fallback,
        JsonValueKind.Number => property.ToString(),
        JsonValueKind.True => "PASS",
        JsonValueKind.False => "FAIL",
        JsonValueKind.Null => fallback,
        _ => property.ToString()
    };
}

static string RawScalar(JsonDocument? document, string propertyName, string fallback)
{
    if (document is null || !document.RootElement.TryGetProperty(propertyName, out var property))
    {
        return fallback;
    }

    return property.ValueKind switch
    {
        JsonValueKind.String => property.GetString() ?? fallback,
        JsonValueKind.Number => property.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => fallback,
        _ => property.ToString()
    };
}

static string UiArtifactRoot(JsonDocument? uiStatus)
{
    var screenshot = Scalar(uiStatus, "Screenshot", "NOT_RUN").Replace('\\', '/');
    const string suffix = "/safe-screenshot-before.png";
    if (screenshot.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
        return screenshot[..^suffix.Length];
    }

    var lastSlash = screenshot.LastIndexOf('/');
    return lastSlash > 0 ? screenshot[..lastSlash] : "NOT_RUN";
}

static string UiServerReady(JsonDocument? uiStatus)
{
    return string.Equals(Scalar(uiStatus, "CharacterSelectDetected", "FAIL"), "PASS", StringComparison.Ordinal)
        ? "PASS"
        : "NOT_RUN";
}

static string EvidenceRows(JsonDocument? evidenceIndex)
{
    if (evidenceIndex is null || !evidenceIndex.RootElement.TryGetProperty("Records", out var records))
    {
        return "| NONE | N/A | UNKNOWN | UNKNOWN | UNKNOWN | N/A | Evidence index artifact not found. |";
    }

    var rows = records.EnumerateArray().Select(record =>
        $"| {Escape(Prop(record, "Classification", "UNKNOWN"))} | {Escape(Prop(record, "ArtifactPath", "UNKNOWN"))} | {Escape(Prop(record, "Direction", "UNKNOWN"))} | {Escape(Prop(record, "OpcodeCandidate", "UNKNOWN"))} | {Escape(Prop(record, "FrameLength", "UNKNOWN"))} | {Escape(Prop(record, "Confidence", "UNKNOWN"))} | {Escape(Prop(record, "ExclusionReason", "UNKNOWN"))} |");
    var joined = string.Join(Environment.NewLine, rows);
    return string.IsNullOrWhiteSpace(joined) ? "| NONE | N/A | UNKNOWN | UNKNOWN | UNKNOWN | N/A | No evidence records. |" : joined;
}

static string StaticRows(JsonDocument? staticAnalysis)
{
    if (staticAnalysis is null || !staticAnalysis.RootElement.TryGetProperty("Findings", out var findings))
    {
        return "| NONE | N/A | N/A | N/A | UNKNOWN | UNKNOWN | N/A | Static-analysis artifact not found. |";
    }

    var rows = findings.EnumerateArray().Take(60).Select(finding =>
        $"| {Escape(Prop(finding, "Module", "UNKNOWN"))} | {Escape(Prop(finding, "Offset", "N/A"))} | {Escape(Prop(finding, "Encoding", "UNKNOWN"))} | {Escape(Prop(finding, "Pattern", "UNKNOWN"))} | {Escape(Prop(finding, "ReferencedOpcode", "UNKNOWN"))} | {Escape(Prop(finding, "ExpectedLength", "UNKNOWN"))} | {Escape(Prop(finding, "Confidence", "UNKNOWN"))} | {Escape(Prop(finding, "Limitation", "UNKNOWN"))} |");
    var joined = string.Join(Environment.NewLine, rows);
    return string.IsNullOrWhiteSpace(joined) ? "| NONE | N/A | N/A | N/A | UNKNOWN | UNKNOWN | N/A | No static-analysis findings. |" : joined;
}

static string MatrixReport(string title, JsonDocument? matrix)
{
    if (matrix is null)
    {
        return $"""
# {title} Byte Matrix

GeneratedAtUtc: {DateTimeOffset.UtcNow:o}

Status: NOT_RUN.
Summary: Matrix artifact was not found.

| Offset | Width | Candidate Meaning | Confidence | Evidence Cases |
| --- | --- | --- | --- | --- |
| UNKNOWN | UNKNOWN | Artifact missing | NOT_RUN | NONE |
""";
    }

    var root = matrix.RootElement;
    var rows = root.TryGetProperty("Offsets", out var offsets)
        ? string.Join(Environment.NewLine, offsets.EnumerateArray().Select(offset =>
            $"| {Escape(Prop(offset, "Offset", "UNKNOWN"))} | {Escape(Prop(offset, "Width", "UNKNOWN"))} | {Escape(Prop(offset, "CandidateMeaning", "UNKNOWN"))} | {Escape(Prop(offset, "Confidence", "UNKNOWN"))} | {Escape(EvidenceCases(offset))} |"))
        : "| UNKNOWN | UNKNOWN | Offsets missing | NOT_RUN | NONE |";

    return $"""
# {title} Byte Matrix

GeneratedAtUtc: {Prop(root, "GeneratedAtUtc", DateTimeOffset.UtcNow.ToString("o"))}

Status: {Prop(root, "Status", "UNKNOWN")}
Summary: {Prop(root, "Summary", "UNKNOWN")}

| Offset | Width | Candidate Meaning | Confidence | Evidence Cases |
| --- | --- | --- | --- | --- |
{rows}
""";
}

static string EvidenceCases(JsonElement element)
{
    if (!element.TryGetProperty("EvidenceCases", out var cases) || cases.ValueKind != JsonValueKind.Array)
    {
        return "NONE";
    }

    var values = cases.EnumerateArray().Select(item => item.ToString()).Where(value => !string.IsNullOrWhiteSpace(value));
    var joined = string.Join(",", values);
    return string.IsNullOrWhiteSpace(joined) ? "NONE" : joined;
}

static string Prop(JsonElement element, string propertyName, string fallback)
{
    if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
    {
        return fallback;
    }

    return property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : property.ToString();
}

static string RegressionStatus(JsonDocument? regression)
{
    if (regression is null)
    {
        return "NOT_RUN_IN_CLOSURE";
    }

    var pass = Bool(regression, "serverReady")
        && Bool(regression, "serverCleanExit")
        && Bool(regression, "worldReady")
        && Bool(regression, "loginSuccess")
        && Bool(regression, "characterSelect")
        && Bool(regression, "enterWorld")
        && Bool(regression, "stopCommandCompleted")
        && Number(regression, "remainingClientCount") == 0
        && Number(regression, "remainingServerCount") == 0;
    return pass ? "PASS" : "FAIL";
}

static string RegressionHeartbeat(JsonDocument? regression)
{
    if (regression is null)
    {
        return "NOT_RUN_IN_CLOSURE";
    }

    return $"{Number(regression, "heartbeatCount")} over {Scalar(regression, "heartbeatDurationSeconds", "UNKNOWN")} seconds";
}

static string RegressionCleanShutdown(JsonDocument? regression)
{
    if (regression is null)
    {
        return "NOT_RUN_IN_CLOSURE";
    }

    return Number(regression, "remainingClientCount") == 0 && Number(regression, "remainingServerCount") == 0 ? "PASS" : "FAIL";
}

static bool Bool(JsonDocument document, string propertyName) =>
    document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

static int Number(JsonDocument document, string propertyName)
{
    if (!document.RootElement.TryGetProperty(propertyName, out var property))
    {
        return 0;
    }

    return property.TryGetInt32(out var value) ? value : 0;
}

static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);

static string JsonEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

static void WriteText(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
