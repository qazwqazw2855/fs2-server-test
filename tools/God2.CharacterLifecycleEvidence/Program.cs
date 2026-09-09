using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var repoRoot = FindRepositoryRoot(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
var generatedAtUtc = DateTimeOffset.UtcNow;
var evidenceRoot = Path.Combine(repoRoot, "Artifacts", "CharacterLifecycleEvidence");
var goldenCreateRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterCreate");
var goldenDeleteRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterDelete");
var goldenListRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterList");
var goldenCreateResultRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterCreateResult");
var goldenDeleteResultRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterDeleteResult");
var goldenListRefreshRoot = Path.Combine(repoRoot, "Artifacts", "Golden", "CharacterListRefresh");

foreach (var directory in new[] { evidenceRoot, goldenCreateRoot, goldenDeleteRoot, goldenListRoot, goldenCreateResultRoot, goldenDeleteResultRoot, goldenListRefreshRoot })
{
    Directory.CreateDirectory(directory);
}

var evidence = BuildEvidenceIndex(repoRoot, generatedAtUtc);
var staticAnalysis = BuildStaticAnalysis(repoRoot, generatedAtUtc);
var createMatrix = BuildBlockedMatrix("CreateCharacter", generatedAtUtc);
var deleteMatrix = BuildBlockedMatrix("DeleteCharacter", generatedAtUtc);
var status = new CharacterLifecycleStatus(
    GeneratedAtUtc: generatedAtUtc,
    CreateC2S: "BlockedByEvidence",
    DeleteC2S: "BlockedByEvidence",
    CreateResultS2C: "SerializerBlockedByEvidence",
    DeleteResultS2C: "SerializerBlockedByEvidence",
    CharacterListRefresh: "BlockedByEvidence",
    RuntimeLifecycle: "WiredAndTested",
    LifeSkillPersistence: "WiredAndTested",
    OfficialClientCreateCapture: "NotRunNoRawEvidenceAndNoCreateUiLocator",
    FakeNetworkBytes: 0);

var options = new JsonSerializerOptions { WriteIndented = true };
WriteJson(Path.Combine(evidenceRoot, "index.json"), evidence, options);
WriteJson(Path.Combine(evidenceRoot, "protocol-index.json"), evidence, options);
WriteJson(Path.Combine(evidenceRoot, "static-analysis.json"), staticAnalysis, options);
WriteJson(Path.Combine(evidenceRoot, "create-matrix.json"), createMatrix, options);
WriteJson(Path.Combine(evidenceRoot, "delete-matrix.json"), deleteMatrix, options);
WriteJson(Path.Combine(evidenceRoot, "status.json"), status, options);
WriteText(Path.Combine(goldenCreateRoot, "README.md"), GoldenReadme("CharacterCreate", generatedAtUtc));
WriteText(Path.Combine(goldenDeleteRoot, "README.md"), GoldenReadme("CharacterDelete", generatedAtUtc));
WriteText(Path.Combine(goldenListRoot, "README.md"), GoldenReadme("CharacterList", generatedAtUtc));
WriteText(Path.Combine(goldenCreateResultRoot, "README.md"), GoldenReadme("CharacterCreateResult", generatedAtUtc));
WriteText(Path.Combine(goldenDeleteResultRoot, "README.md"), GoldenReadme("CharacterDeleteResult", generatedAtUtc));
WriteText(Path.Combine(goldenListRefreshRoot, "README.md"), GoldenReadme("CharacterListRefresh", generatedAtUtc));

Console.WriteLine($"CharacterLifecycle Evidence Index: {Path.Combine(evidenceRoot, "index.json")}");
Console.WriteLine($"CharacterLifecycle Static Analysis: {Path.Combine(evidenceRoot, "static-analysis.json")}");
Console.WriteLine("CharacterLifecycle final reports are owned by God2.CharacterLifecycleFinalReport.");

static string FindRepositoryRoot(string start)
{
    var current = new DirectoryInfo(Path.GetFullPath(start));
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
    {
        current = current.Parent;
    }

    return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
}

static CharacterLifecycleEvidenceIndex BuildEvidenceIndex(string repoRoot, DateTimeOffset generatedAtUtc)
{
    var records = new List<CharacterLifecycleEvidenceRecord>();
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/PacketCoverageMatrix.csv",
        "CharacterCoverage",
        "Character",
        "Unknown",
        "Character phase is PARTIAL; raw CharacterCreate/Delete/ListRefresh packets are not recovered.",
        "High");
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/PacketSequenceMap.json",
        "VisualCreateSequence",
        "CharacterCreate",
        "Unknown",
        "CharacterCreate visual flow was observed; raw opcode status is NotRecoveredToday.",
        "Medium");
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/UnknownPacketCatalog.json",
        "UnknownCatalog",
        "CharacterDelete",
        "Unknown",
        "CharacterDelete and CharacterListRefresh remain not-yet-recovered packet families.",
        "High");
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/VerifiedPacketCatalog.json",
        "VerifiedCatalogExclusion",
        "CharacterCreateDelete",
        "Unknown",
        "No verified CharacterCreate or CharacterDelete raw packet entry is present.",
        "High");
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/login-to-character-select-records.json",
        "CharacterListBootstrap",
        "CharacterList",
        "ServerToClient",
        "Contains login-to-character-select bootstrap evidence, not Create/Delete/ListRefresh field evidence.",
        "Medium");
    AddKnownFileRecord(
        records,
        repoRoot,
        "src/God2.ClassicServer.Protocol/Knowledge/protocol-knowledge-base.json",
        "KnowledgeBaseCreateVisual",
        "CharacterCreate",
        "Unknown",
        "Knowledge base references create-character-success visual artifact; raw opcode status remains NotRecoveredToday.",
        "Medium");

    var inventory = Path.Combine(repoRoot, "Artifacts", "ProtocolEvidenceRecovery", "LoginDeepRecovery", "source-inventory.json");
    if (File.Exists(inventory))
    {
        var text = File.ReadAllText(inventory);
        var createSource = Regex.Match(text, "\"SourcePath\"\\s*:\\s*\"(?<path>[^\"]*create-character-success\\.json)\"[\\s\\S]*?\"Sha256\"\\s*:\\s*\"(?<sha>[A-Fa-f0-9]+)\"", RegexOptions.CultureInvariant);
        if (createSource.Success)
        {
            records.Add(new CharacterLifecycleEvidenceRecord(
                ArtifactPath: createSource.Groups["path"].Value,
                CaptureTimestamp: "2026-07-26T05:31:07.1683851+00:00",
                Direction: "Unknown",
                ConnectionStage: "OfficialVisualAction",
                OpcodeCandidate: null,
                FrameLength: null,
                RawSha256: createSource.Groups["sha"].Value.ToUpperInvariant(),
                Classification: "CreateVisualOnly",
                CharacterName: null,
                Class: null,
                Gender: null,
                LifeSkill: null,
                CharacterIdOrSlot: null,
                Confidence: "Medium",
                KnownFields: [],
                UnknownFields: ["Opcode", "Length", "NameOffset", "ClassOffset", "GenderOffset", "LifeSkillOffset", "AppearanceTemplate", "SequenceChecksum"],
                ExclusionReason: "Source inventory preserves a visual success artifact reference, but RawLength and RawHex are null."));
        }
    }

    return new CharacterLifecycleEvidenceIndex(
        SchemaVersion: "character-lifecycle-evidence-index-v1",
        GeneratedAtUtc: generatedAtUtc,
        EvidencePolicy: "Unknown packets are blocked until official raw evidence exists.",
        Records: records.OrderBy(record => record.Classification, StringComparer.Ordinal).ThenBy(record => record.ArtifactPath, StringComparer.Ordinal).ToArray());
}

static CharacterLifecycleStaticAnalysis BuildStaticAnalysis(string repoRoot, DateTimeOffset generatedAtUtc)
{
    var clientRoot = FindOfficialClientRoot(repoRoot);
    var targets = new List<string>();
    if (!string.IsNullOrWhiteSpace(clientRoot))
    {
        foreach (var name in new[]
        {
            "God2_opt.exe",
            "Launcher.exe",
            "gamewindow1.smf",
            "ctserver.ini",
            "God2Con.csvZ",
            "God2Con2.csvZ",
            "lang1.csvZ",
            "lang2.csvZ",
            "client_p.godZ"
        })
        {
            var path = Path.Combine(clientRoot, name);
            if (File.Exists(path))
            {
                targets.Add(path);
            }
        }
    }

    var findings = new List<CharacterLifecycleStaticFinding>();
    var terms = new[]
    {
        "CreateCharacter",
        "CharacterCreate",
        "create character",
        "DeleteCharacter",
        "CharacterDelete",
        "delete character",
        "CharacterList",
        "life skill",
        "LifeSkill",
        "profession",
        "gender",
        "class",
        "slot",
        "opcode",
        "packet",
        "send",
        "recv",
        "name",
        "角色",
        "創",
        "创",
        "刪",
        "删",
        "職",
        "性",
        "生活",
        "技能"
    };

    foreach (var target in targets)
    {
        var info = new FileInfo(target);
        if (info.Length > 5_000_000)
        {
            findings.Add(CharacterLifecycleStaticFinding.Skipped(
                Module: MakeRelativeOrSafePath(repoRoot, target),
                Reason: "SkippedLargeModuleForStringScan",
                Detail: $"Length={info.Length}"));
            continue;
        }

        var bytes = File.ReadAllBytes(target);
        AddStaticStringFindings(repoRoot, findings, target, "ASCII", EnumerateAsciiStrings(bytes, 4), terms);
        AddStaticStringFindings(repoRoot, findings, target, "UTF-16LE", EnumerateUtf16LeStrings(bytes, 4), terms);
    }

    var orderedFindings = findings
        .OrderBy(item => item.Module, StringComparer.Ordinal)
        .ThenBy(item => item.Offset ?? long.MaxValue)
        .ThenBy(item => item.Pattern, StringComparer.Ordinal)
        .Take(200)
        .ToArray();

    var hasLifecycleTerm = orderedFindings.Any(item =>
        item.Pattern.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
        item.Pattern.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
        item.Pattern.Contains("CharacterList", StringComparison.OrdinalIgnoreCase) ||
        item.Pattern.Contains("角色", StringComparison.OrdinalIgnoreCase) ||
        item.Pattern.Contains("刪", StringComparison.OrdinalIgnoreCase));
    var status = string.IsNullOrWhiteSpace(clientRoot)
        ? "OfficialClientPathUnavailable"
        : hasLifecycleTerm
            ? "StaticStringsFoundNoProtocolLayout"
            : "NoCharacterLifecycleStaticStringsFound";

    var conclusions = new[]
    {
        "Official client files were read-only scanned for ASCII and UTF-16LE strings.",
        "No client patching, injection, protocol replay, or fake packet generation was performed.",
        "String hits alone do not prove opcode, length, field offset, field encoding, field write order, checksum, or sequence semantics.",
        "No Create/Delete/ListRefresh serializer gate can be lifted from this static scan."
    };

    return new CharacterLifecycleStaticAnalysis(
        SchemaVersion: "character-lifecycle-static-analysis-v1",
        GeneratedAtUtc: generatedAtUtc,
        Status: status,
        ClientRoot: clientRoot is null ? null : MakeRelativeOrSafePath(repoRoot, clientRoot),
        TargetCount: targets.Count,
        Findings: orderedFindings,
        Conclusions: conclusions);
}

static string? FindOfficialClientRoot(string repoRoot)
{
    var profilePath = Path.Combine(repoRoot, "Artifacts", "ClientInstrumentation", "LauncherAutomation", "launcher-profile.json");
    if (!File.Exists(profilePath))
    {
        return null;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
    if (!document.RootElement.TryGetProperty("targetExecutable", out var targetExecutable) ||
        targetExecutable.GetString() is not { Length: > 0 } launcherPath)
    {
        return null;
    }

    var directory = Path.GetDirectoryName(launcherPath);
    return string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
        ? null
        : directory;
}

static void AddStaticStringFindings(
    string repoRoot,
    List<CharacterLifecycleStaticFinding> findings,
    string module,
    string encoding,
    IEnumerable<BinaryStringCandidate> strings,
    IReadOnlyList<string> terms)
{
    foreach (var candidate in strings)
    {
        foreach (var term in terms)
        {
            if (candidate.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new CharacterLifecycleStaticFinding(
                    Module: MakeRelativeOrSafePath(repoRoot, module),
                    Offset: candidate.Offset,
                    Encoding: encoding,
                    Pattern: term,
                    MatchedText: SafeSnippet(candidate.Value),
                    FunctionOrOffset: $"0x{candidate.Offset:X}",
                    CallChain: "UnavailableWithoutDisassembly",
                    ReferencedOpcode: "UNKNOWN",
                    ExpectedLength: "UNKNOWN",
                    WriteOrder: "UNKNOWN",
                    FieldWidth: "UNKNOWN",
                    StringTermination: "UNKNOWN",
                    ClassValueSource: "UNKNOWN",
                    GenderValueSource: "UNKNOWN",
                    LifeSkillValueSource: "UNKNOWN",
                    AppearanceTemplateSource: "UNKNOWN",
                    CharacterIdSlotSource: "UNKNOWN",
                    ChecksumSequenceCandidate: "UNKNOWN",
                    Confidence: "LowStaticStringOnly",
                    Limitation: "String reference does not establish packet layout or runtime-safe UI automation."));
                break;
            }
        }
    }
}

static IEnumerable<BinaryStringCandidate> EnumerateAsciiStrings(byte[] bytes, int minimumLength)
{
    var start = -1;
    for (var index = 0; index < bytes.Length; index++)
    {
        if (bytes[index] is >= 0x20 and <= 0x7E)
        {
            if (start < 0)
            {
                start = index;
            }

            continue;
        }

        if (start >= 0 && index - start >= minimumLength)
        {
            yield return new BinaryStringCandidate(start, Encoding.ASCII.GetString(bytes, start, index - start));
        }

        start = -1;
    }

    if (start >= 0 && bytes.Length - start >= minimumLength)
    {
        yield return new BinaryStringCandidate(start, Encoding.ASCII.GetString(bytes, start, bytes.Length - start));
    }
}

static IEnumerable<BinaryStringCandidate> EnumerateUtf16LeStrings(byte[] bytes, int minimumLength)
{
    for (var alignment = 0; alignment <= 1; alignment++)
    {
        var start = -1;
        var chars = new StringBuilder();
        for (var index = alignment; index + 1 < bytes.Length; index += 2)
        {
            if (bytes[index + 1] == 0 && bytes[index] is >= 0x20 and <= 0x7E)
            {
                if (start < 0)
                {
                    start = index;
                }

                chars.Append((char)bytes[index]);
                continue;
            }

            if (start >= 0 && chars.Length >= minimumLength)
            {
                yield return new BinaryStringCandidate(start, chars.ToString());
            }

            start = -1;
            chars.Clear();
        }

        if (start >= 0 && chars.Length >= minimumLength)
        {
            yield return new BinaryStringCandidate(start, chars.ToString());
        }
    }
}

static string MakeRelativeOrSafePath(string repoRoot, string path)
{
    var fullPath = Path.GetFullPath(path);
    var fullRoot = Path.GetFullPath(repoRoot);
    if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
    }

    if (Directory.Exists(fullPath))
    {
        return $"ExternalClientRoot:{Path.GetFileName(fullPath)}:{Sha256Text(fullPath)[..12]}";
    }

    return $"OfficialClient/{Path.GetFileName(fullPath)}";
}

static string Sha256Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static string SafeSnippet(string value)
{
    var singleLine = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    singleLine = Regex.Replace(singleLine, "(password|pwd|token|secret)\\s*[:=]\\s*[^\\s,;]+", "$1=<redacted>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    singleLine = Regex.Replace(singleLine, "\\b(username|account|password|pwd|token|secret|credential)\\b", "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    singleLine = Regex.Replace(singleLine, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    return singleLine.Length <= 120 ? singleLine : string.Concat(singleLine.AsSpan(0, 117), "...");
}

static void AddKnownFileRecord(
    List<CharacterLifecycleEvidenceRecord> records,
    string repoRoot,
    string relativePath,
    string classification,
    string family,
    string direction,
    string note,
    string confidence)
{
    var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    var exists = File.Exists(path);
    records.Add(new CharacterLifecycleEvidenceRecord(
        ArtifactPath: relativePath,
        CaptureTimestamp: exists ? File.GetLastWriteTimeUtc(path).ToString("o") : null,
        Direction: direction,
        ConnectionStage: "OfflineEvidenceIndex",
        OpcodeCandidate: null,
        FrameLength: null,
        RawSha256: exists ? Sha256File(path) : null,
        Classification: classification,
        CharacterName: null,
        Class: family,
        Gender: null,
        LifeSkill: null,
        CharacterIdOrSlot: null,
        Confidence: confidence,
        KnownFields: [],
        UnknownFields: ["CreateOpcode", "CreateLength", "DeleteOpcode", "DeleteLength", "CreateResult", "DeleteResult", "ListRefresh"],
        ExclusionReason: exists ? note : "Referenced evidence file is missing from this workspace."));
}

static CharacterLifecycleByteMatrix BuildBlockedMatrix(string packetFamily, DateTimeOffset generatedAtUtc) =>
    new(
        SchemaVersion: "character-lifecycle-byte-matrix-v1",
        PacketFamily: packetFamily,
        GeneratedAtUtc: generatedAtUtc,
        Status: "BlockedByEvidence",
        EvidenceCases: [],
        Offsets:
        [
            new CharacterLifecycleOffsetFinding(null, null, null, "Unknown", "Unknown", "Unknown", "Unknown", "Unknown", "No raw official frame exists.", "BlockedByEvidence", [])
        ],
        Summary: "No byte-by-byte matrix was produced because no raw official frame exists for this packet family.");

static string GoldenReadme(string family, DateTimeOffset generatedAtUtc) => $"""
# {family} Golden Artifact

GeneratedAtUtc: {generatedAtUtc:o}

Status: NOT CREATED.
Reason: no non-self-produced official raw packet evidence exists for this family in the current workspace.
""";

static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
    WriteText(path, JsonSerializer.Serialize(value, options));

static void WriteText(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static string Sha256File(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

public sealed record CharacterLifecycleEvidenceIndex(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string EvidencePolicy,
    IReadOnlyList<CharacterLifecycleEvidenceRecord> Records);

public sealed record CharacterLifecycleEvidenceRecord(
    string ArtifactPath,
    string? CaptureTimestamp,
    string Direction,
    string ConnectionStage,
    string? OpcodeCandidate,
    int? FrameLength,
    string? RawSha256,
    string Classification,
    string? CharacterName,
    string? Class,
    string? Gender,
    string? LifeSkill,
    string? CharacterIdOrSlot,
    string Confidence,
    IReadOnlyList<string> KnownFields,
    IReadOnlyList<string> UnknownFields,
    string ExclusionReason);

public sealed record CharacterLifecycleByteMatrix(
    string SchemaVersion,
    string PacketFamily,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    IReadOnlyList<string> EvidenceCases,
    IReadOnlyList<CharacterLifecycleOffsetFinding> Offsets,
    string Summary);

public sealed record CharacterLifecycleOffsetFinding(
    int? Offset,
    int? Width,
    string? BaselineBytes,
    string ClassVariation,
    string GenderVariation,
    string LifeSkillVariation,
    string NameVariation,
    string RepeatedRunVariation,
    string CandidateMeaning,
    string Confidence,
    IReadOnlyList<string> EvidenceCases);

public sealed record CharacterLifecycleStatus(
    DateTimeOffset GeneratedAtUtc,
    string CreateC2S,
    string DeleteC2S,
    string CreateResultS2C,
    string DeleteResultS2C,
    string CharacterListRefresh,
    string RuntimeLifecycle,
    string LifeSkillPersistence,
    string OfficialClientCreateCapture,
    int FakeNetworkBytes);

public sealed record CharacterLifecycleStaticAnalysis(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    string? ClientRoot,
    int TargetCount,
    IReadOnlyList<CharacterLifecycleStaticFinding> Findings,
    IReadOnlyList<string> Conclusions);

public sealed record CharacterLifecycleStaticFinding(
    string Module,
    long? Offset,
    string Encoding,
    string Pattern,
    string MatchedText,
    string FunctionOrOffset,
    string CallChain,
    string ReferencedOpcode,
    string ExpectedLength,
    string WriteOrder,
    string FieldWidth,
    string StringTermination,
    string ClassValueSource,
    string GenderValueSource,
    string LifeSkillValueSource,
    string AppearanceTemplateSource,
    string CharacterIdSlotSource,
    string ChecksumSequenceCandidate,
    string Confidence,
    string Limitation)
{
    public static CharacterLifecycleStaticFinding Skipped(string Module, string Reason, string Detail) =>
        new(
            Module,
            Offset: null,
            Encoding: "N/A",
            Pattern: Reason,
            MatchedText: Detail,
            FunctionOrOffset: "N/A",
            CallChain: "N/A",
            ReferencedOpcode: "UNKNOWN",
            ExpectedLength: "UNKNOWN",
            WriteOrder: "UNKNOWN",
            FieldWidth: "UNKNOWN",
            StringTermination: "UNKNOWN",
            ClassValueSource: "UNKNOWN",
            GenderValueSource: "UNKNOWN",
            LifeSkillValueSource: "UNKNOWN",
            AppearanceTemplateSource: "UNKNOWN",
            CharacterIdSlotSource: "UNKNOWN",
            ChecksumSequenceCandidate: "UNKNOWN",
            Confidence: "Informational",
            Limitation: "Module was not scanned.");
}

public sealed record BinaryStringCandidate(long Offset, string Value);
