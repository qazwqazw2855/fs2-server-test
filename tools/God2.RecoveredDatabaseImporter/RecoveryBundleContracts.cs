using System.Text.Json;

namespace God2.RecoveredDatabaseImporter;

public static class RecoveryBundleContract
{
    public const string ManifestSchemaVersion = "god2-ultimate-package-manifest-v1";
    public const string ImportResultSchemaVersion = "god2-recovery-bundle-import-result-v1";
    public const string ExpectedClientExecutable = "God2_opt.exe";
    public const string ExpectedClientArchitecture = "x86";
    public const string ExpectedClientVersion = "1.0.0.1";
    public const string ExpectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B";
    public const string ExpectedClientIdentityStatus = "ExactClientIdentityComputedAndValidated";
    public const string ExpectedRecoveryAttestationStatus = "ExactRecoveryEventBuildSessionProcessAttested";
    public const long MaximumManifestBytes = 4 * 1024 * 1024;
    public const long MaximumStructuredJsonBytes = 64 * 1024 * 1024;
    public const long MaximumEntryBytes = 1024L * 1024 * 1024;
    public const long MaximumTotalUncompressedBytes = 8L * 1024 * 1024 * 1024;
    public const int MaximumFileCount = 100_000;
    public const int MaximumJsonLineChars = 4 * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedAuthorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VERIFIED",
        "DERIVED",
        "OBSERVED",
        "HYPOTHESIS",
        "UNKNOWN",
        "UNKNOWN_SERVER_ONLY",
        "REJECTED"
    };

    public static bool CanPromote(string authority) =>
        string.Equals(authority, "VERIFIED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(authority, "DERIVED", StringComparison.OrdinalIgnoreCase);
}

public sealed record RecoveryBundleFileDescriptor(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string Provenance,
    string Authority,
    string SchemaVersion);

public sealed record ClientBuildIdentity(
    string Executable,
    string Architecture,
    string FileVersion,
    string Sha256,
    string ValidationStatus,
    bool ExactBindingValidated,
    string RecoveryAttestationStatus,
    string ComputedSha256,
    string ComputedFileVersion,
    string ComputedArchitecture);

public sealed record RecoveryBundleManifest(
    string SchemaVersion,
    string PackageId,
    ClientBuildIdentity ClientBuild,
    IReadOnlyList<RecoveryBundleFileDescriptor> Files);

public sealed record ContentValidationSummary(
    int JsonDocumentCount,
    int JsonLineCount,
    int AuthorityValueCount,
    int ProductionPromotionCount,
    int TraditionalChineseDisplayFindingCount,
    int BrokenReferenceCount,
    int ContentOrphanCount,
    int UnknownDropSafetyCount,
    IReadOnlyList<string> Findings);

public sealed record RecoveryBundleInspection(
    string SourceZipPath,
    long SourceZipSizeBytes,
    string SourceZipSha256,
    string ArchiveRootPrefix,
    string ManifestSha256,
    RecoveryBundleManifest Manifest,
    ClientBuildIdentity ClientBuild,
    ContentValidationSummary ContentValidation,
    DateTimeOffset InspectedAtUtc);

public sealed record ServerCapability(
    string Domain,
    bool Present,
    IReadOnlyList<string> EvidenceFiles);

public sealed record CompiledContractCheck(
    string Domain,
    string Status,
    IReadOnlyList<string> RequiredSymbols,
    IReadOnlyList<string> VerifiedSymbols,
    IReadOnlyList<string> MissingSymbols,
    IReadOnlyList<string> AssemblyEvidence);

public sealed record CompiledServerContractInventory(
    string Mode,
    bool BuildOutputExecutedByImporter,
    int AssemblyCount,
    IReadOnlyList<CompiledContractCheck> Contracts);

public sealed record MigrationModelVerification(
    string Mode,
    bool ProductionConnectionAttempted,
    bool ProductionMutationAttempted,
    int ParsedMigrationCount,
    int ParsedStatementCount,
    int TableCount,
    int ColumnCount,
    string SchemaModelSha256,
    bool CandidateAdditiveOnly,
    bool CandidateAppliedToModel,
    bool CandidateSecondApplyNoOp,
    IReadOnlyList<string> CandidateAddedTables,
    IReadOnlyList<string> Findings);

public sealed record SemanticReplayCaseResult(
    string CaseId,
    string Domain,
    string Status,
    int MutationCount,
    SemanticOracleResult? Oracle,
    string SourceEvidence,
    string? PlaintextPacketHash,
    string? SemanticEventHash,
    string? Finding);

public sealed record SemanticReplayVerification(
    string Mode,
    string FixtureStatus,
    int CaseCount,
    int EquivalentCount,
    int CounterexampleCount,
    IReadOnlyList<SemanticReplayCaseResult> Cases);

public sealed record OfflineRecoveryVerification(
    string SchemaVersion,
    string Status,
    CompiledServerContractInventory CompiledServer,
    MigrationModelVerification MigrationModel,
    SemanticReplayVerification Replay,
    bool ProductionDatabaseConnectionAttempted,
    bool ProductionDatabaseMutationAttempted,
    int FakeNetworkByteCount);

public sealed record ServerInventory(
    string SolutionPath,
    int ProjectCount,
    int SourceFileCount,
    IReadOnlyList<string> Projects,
    IReadOnlyList<ServerCapability> Capabilities);

public sealed record DatabaseInventory(
    string Mode,
    bool ProductionConnectionAttempted,
    bool ProductionMutationAttempted,
    int MigrationCount,
    string MigrationHead,
    string MigrationHeadSha256,
    IReadOnlyList<string> Tables,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Columns);

public sealed record RecoveryGap(
    string Domain,
    string BundleStatus,
    string ServerStatus,
    string DatabaseStatus,
    string OverallStatus,
    IReadOnlyList<string> BundleEvidence,
    IReadOnlyList<string> ServerEvidence,
    IReadOnlyList<string> DatabaseEvidence,
    string RequiredAction);

public sealed record ChangePlanItem(
    string Domain,
    string Status,
    string Target,
    string Action,
    string EvidenceGate);

public sealed record RecoveryIntegrationPlans(
    IReadOnlyList<ChangePlanItem> Runtime,
    IReadOnlyList<ChangePlanItem> Protocol,
    IReadOnlyList<ChangePlanItem> Replay);

public sealed record OracleCounterexample(
    string SourceEvidence,
    string ExpectedState,
    string ActualState,
    string ResponsibleSubsystem,
    string FirstDivergencePath);

public sealed record SemanticOracleResult(
    bool Equivalent,
    string ExpectedHash,
    string ActualHash,
    OracleCounterexample? Counterexample);

public sealed record RecoveryImportOptions(
    string RepositoryRoot,
    string BundlePath,
    string OutputRoot);

public sealed record RecoveryImportResult(
    string SchemaVersion,
    string Status,
    string PackageId,
    string SourceZipSha256,
    string ArtifactRoot,
    string StagingRoot,
    int StagedFileCount,
    int GapCount,
    int ReadyDomainCount,
    int BlockedDomainCount,
    bool ProductionDatabaseConnectionAttempted,
    bool ProductionDatabaseMutationAttempted,
    string ResultManifestPath,
    DateTimeOffset CompletedAtUtc);

public sealed class RecoveryImportException : IOException
{
    public RecoveryImportException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public RecoveryImportException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

internal static class RecoveryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
