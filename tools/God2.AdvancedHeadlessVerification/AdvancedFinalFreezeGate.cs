namespace God2.AdvancedHeadlessVerification;

public sealed record AdvancedFinalFreezeGateInput(
    bool CodeReviewRemediationPassed,
    bool BuildPassed,
    bool FullRegressionPassed,
    bool ProtocolRegressionPassed,
    bool RuntimeRegressionPassed,
    bool HeadlessRegressionPassed,
    bool AdvancedFastPassed,
    bool MariaDbPassed,
    bool MapParsePassed,
    bool ResourceSoakPassed,
    double ResourceSoakDurationSeconds,
    bool SecurityScanPassed,
    string SecurityFreshness,
    bool BuildIdentityMatches,
    bool AssemblyHashesUnchanged,
    bool SourceHashMatches,
    bool EvidenceFreshnessPassed,
    bool ReportIntegrityPassed,
    bool RealTestExecutionObserved,
    bool TrxPresent,
    bool TrxValid,
    bool TrxDuplicateRecordsDeduplicated,
    bool HardcodedPassDetected,
    int CredentialFindingCount,
    int SensitivePayloadFindingCount,
    int HardcodedAbsolutePathFindingCount,
    int FakeNetworkBytes,
    int QueueOutboxJournalFinalCount,
    int KeyedLockFinalCount,
    int KeyedLockReferenceFinalCount,
    int DbConnectionLeakCount,
    bool OldEvidenceMixed,
    bool ImportedEvidenceProvenanceValid,
    bool OriginalZipActuallyAvailable,
    bool OriginalZipAvailableReported,
    bool OriginalZipRequiredForRevalidation,
    bool ActorPrimaryEnabled,
    bool LegacyPrimaryDefault,
    string UserManualGameplay,
    string NewManualCapture);

public sealed record AdvancedFinalFreezeGateResult(string FinalStatus, bool FreezeReady, IReadOnlyList<string> Blockers);
public sealed record AdvancedFinalFreezeNegativeScenario(string Id, AdvancedFinalFreezeGateInput Input, bool ExpectedFreezeReady);

public static class AdvancedFinalFreezeGate
{
    public const string PassedStatus = "ADVANCED FINAL FREEZE PASS";
    public const string BlockedStatus = "ADVANCED FINAL FREEZE NOT_FINALIZED";

    public static AdvancedFinalFreezeGateResult Evaluate(AdvancedFinalFreezeGateInput value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var blockers = new List<string>();
        Require(value.CodeReviewRemediationPassed, "CodeReviewRemediation");
        Require(value.BuildPassed, "Build");
        Require(value.FullRegressionPassed, "FullRegression");
        Require(value.ProtocolRegressionPassed, "ProtocolRegression");
        Require(value.RuntimeRegressionPassed, "RuntimeRegression");
        Require(value.HeadlessRegressionPassed, "HeadlessRegression");
        Require(value.AdvancedFastPassed, "AdvancedFast");
        Require(value.MariaDbPassed, "MariaDb");
        Require(value.MapParsePassed, "MapParseAndAStar");
        Require(value.ResourceSoakPassed, "ResourceSoak");
        Require(value.ResourceSoakDurationSeconds >= 7200, "ResourceSoakDuration");
        Require(value.SecurityScanPassed, "SecurityScan");
        Require(value.SecurityFreshness == "Fresh", "SecurityFreshness");
        Require(value.BuildIdentityMatches, "BuildIdentity");
        Require(value.AssemblyHashesUnchanged, "AssemblyHashStability");
        Require(value.SourceHashMatches, "SourceManifestHash");
        Require(value.EvidenceFreshnessPassed, "EvidenceFreshness");
        Require(value.ReportIntegrityPassed, "ReportIntegrity");
        Require(value.RealTestExecutionObserved, "RealTestExecution");
        Require(value.TrxPresent, "TrxPresent");
        Require(value.TrxValid, "TrxValid");
        Require(value.TrxDuplicateRecordsDeduplicated, "TrxDeduplication");
        Require(!value.HardcodedPassDetected, "HardcodedPassResult");
        Require(value.CredentialFindingCount == 0, "CredentialFindings");
        Require(value.SensitivePayloadFindingCount == 0, "SensitivePayloadFindings");
        Require(value.HardcodedAbsolutePathFindingCount == 0, "HardcodedAbsolutePathFindings");
        Require(value.FakeNetworkBytes == 0, "FakeNetworkBytes");
        Require(value.QueueOutboxJournalFinalCount == 0, "QueueOutboxJournalResidue");
        Require(value.KeyedLockFinalCount == 0 && value.KeyedLockReferenceFinalCount == 0, "KeyedLockResidue");
        Require(value.DbConnectionLeakCount == 0, "DbConnectionLeak");
        Require(!value.OldEvidenceMixed, "OldEvidenceMixed");
        Require(value.ImportedEvidenceProvenanceValid, "ImportedEvidenceProvenance");
        Require(value.OriginalZipActuallyAvailable == value.OriginalZipAvailableReported, "OriginalZipAvailabilityClaim");
        Require(!value.OriginalZipRequiredForRevalidation, "OriginalZipRequiredForRevalidation");
        Require(!value.ActorPrimaryEnabled, "ActorPrimary");
        Require(value.LegacyPrimaryDefault, "LegacyPrimary");
        Require(value.UserManualGameplay == "NOT REQUIRED", "UserManualGameplay");
        Require(value.NewManualCapture == "NOT REQUIRED", "NewManualCapture");
        return blockers.Count == 0
            ? new AdvancedFinalFreezeGateResult(PassedStatus, true, blockers)
            : new AdvancedFinalFreezeGateResult(BlockedStatus, false, blockers);

        void Require(bool condition, string blocker)
        {
            if (!condition) blockers.Add(blocker);
        }
    }

    public static AdvancedFinalFreezeGateInput CreatePassingFixture() => new(
        true, true, true, true, true, true, true, true, true, true, 7200,
        true, "Fresh", true, true, true, true, true, true, true, true, true,
        false, 0, 0, 0, 0, 0, 0, 0, 0, false, true, false, false, false, false,
        true, "NOT REQUIRED", "NOT REQUIRED");

    public static IReadOnlyList<AdvancedFinalFreezeNegativeScenario> RequiredNegativeScenarios()
    {
        var ok = CreatePassingFixture();
        return
        [
            Blocked("01_missing_soak", ok with { ResourceSoakPassed = false }),
            Blocked("02_short_soak", ok with { ResourceSoakDurationSeconds = 7199.999 }),
            Blocked("03_stale_security", ok with { SecurityFreshness = "Stale" }),
            Blocked("04_wrong_build_identity", ok with { BuildIdentityMatches = false }),
            Blocked("05_assembly_changed", ok with { AssemblyHashesUnchanged = false }),
            Blocked("06_hardcoded_pass", ok with { HardcodedPassDetected = true }),
            Blocked("07_missing_trx", ok with { TrxPresent = false }),
            Blocked("08_corrupt_trx", ok with { TrxValid = false }),
            Blocked("09_duplicate_trx_not_deduplicated", ok with { TrxDuplicateRecordsDeduplicated = false }),
            Blocked("10_writer_without_execution", ok with { RealTestExecutionObserved = false }),
            Blocked("11_credential_finding", ok with { CredentialFindingCount = 1 }),
            Blocked("12_sensitive_payload", ok with { SensitivePayloadFindingCount = 1 }),
            Blocked("13_fake_network_bytes", ok with { FakeNetworkBytes = 1 }),
            Blocked("14_actor_primary", ok with { ActorPrimaryEnabled = true }),
            Blocked("15_legacy_not_default", ok with { LegacyPrimaryDefault = false }),
            Blocked("16_queue_backlog", ok with { QueueOutboxJournalFinalCount = 1 }),
            Blocked("17_keyed_lock_growth", ok with { KeyedLockFinalCount = 1 }),
            Blocked("18_db_connection_leak", ok with { DbConnectionLeakCount = 1 }),
            Blocked("19_source_hash_mismatch", ok with { SourceHashMatches = false }),
            Blocked("20_old_evidence_mixed", ok with { OldEvidenceMixed = true }),
            new("21_missing_zip_with_valid_provenance", ok, true),
            Blocked("22_missing_zip_claimed_available", ok with { OriginalZipAvailableReported = true }),
            Blocked("23_manual_gameplay_required", ok with { UserManualGameplay = "REQUIRED" }),
            Blocked("24_manual_capture_required", ok with { NewManualCapture = "REQUIRED" })
        ];

        static AdvancedFinalFreezeNegativeScenario Blocked(string id, AdvancedFinalFreezeGateInput input) => new(id, input, false);
    }
}
