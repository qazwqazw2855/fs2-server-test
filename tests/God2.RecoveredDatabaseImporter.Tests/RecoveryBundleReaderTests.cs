using System.Security.Cryptography;
using System.Text;
using God2.RecoveredDatabaseImporter;

namespace God2.RecoveredDatabaseImporter.Tests;

public sealed class RecoveryBundleReaderTests
{
    private readonly RecoveryBundleReader _reader = new();

    [Fact]
    public async Task InspectAsync_ValidDeterministicBundle_ValidatesAllHardGates()
    {
        using var fixture = new RecoveryBundleFixture();

        var inspection = await _reader.InspectAsync(fixture.CreateArchive(), CancellationToken.None);

        Assert.Equal("fixture-package", inspection.Manifest.PackageId);
        Assert.Equal(13, inspection.Manifest.Files.Count);
        Assert.Equal(RecoveryBundleContract.ExpectedClientExecutable, inspection.ClientBuild.Executable);
        Assert.Equal(RecoveryBundleContract.ExpectedClientArchitecture, inspection.ClientBuild.Architecture);
        Assert.Equal(RecoveryBundleContract.ExpectedClientVersion, inspection.ClientBuild.FileVersion);
        Assert.Equal(RecoveryBundleContract.ExpectedClientSha256, inspection.ClientBuild.Sha256);
        Assert.Equal(RecoveryBundleContract.ExpectedClientIdentityStatus, inspection.ClientBuild.ValidationStatus);
        Assert.True(inspection.ClientBuild.ExactBindingValidated);
        Assert.Equal(RecoveryBundleContract.ExpectedRecoveryAttestationStatus, inspection.ClientBuild.RecoveryAttestationStatus);
        Assert.Equal(RecoveryBundleContract.ExpectedClientSha256, inspection.ClientBuild.ComputedSha256);
        Assert.Equal(RecoveryBundleContract.ExpectedClientVersion, inspection.ClientBuild.ComputedFileVersion);
        Assert.Equal(RecoveryBundleContract.ExpectedClientArchitecture, inspection.ClientBuild.ComputedArchitecture);
        Assert.Equal(5, inspection.ContentValidation.JsonLineCount);
        Assert.Equal(1, inspection.ContentValidation.UnknownDropSafetyCount);
        Assert.Equal(0, inspection.ContentValidation.BrokenReferenceCount);
        Assert.Equal(0, inspection.ContentValidation.ContentOrphanCount);
        Assert.Equal(0, inspection.ContentValidation.TraditionalChineseDisplayFindingCount);
        Assert.Matches("^[0-9a-f]{64}$", inspection.SourceZipSha256);
        Assert.Matches("^[0-9a-f]{64}$", inspection.ManifestSha256);
    }

    [Fact]
    public void Fixture_CreateArchive_IsByteForByteDeterministic()
    {
        using var fixture = new RecoveryBundleFixture();

        var first = SHA256.HashData(File.ReadAllBytes(fixture.CreateArchive()));
        var second = SHA256.HashData(File.ReadAllBytes(fixture.CreateArchive()));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task InspectAsync_SingleEnclosingDirectory_IsSupported()
    {
        using var fixture = new RecoveryBundleFixture { ArchivePrefix = "God2-Recovery-Bundle/" };

        var inspection = await _reader.InspectAsync(fixture.CreateArchive(), CancellationToken.None);

        Assert.Equal("God2-Recovery-Bundle/", inspection.ArchiveRootPrefix);
    }

    [Fact]
    public async Task InspectAsync_HashMismatch_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ManifestHashOverridePath = "canonical/items.jsonl" };

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.file_hash_mismatch");
    }

    [Fact]
    public async Task InspectAsync_MissingManifestFile_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { OmitArchivePath = "canonical/items.jsonl" };

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.file_missing");
    }

    [Fact]
    public async Task InspectAsync_UnlistedArchiveFile_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ExtraArchiveFile = ("unlisted.txt", "unlisted") };

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.unlisted_file");
    }

    [Fact]
    public async Task InspectAsync_DuplicateArchiveEntry_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture
        {
            DuplicateArchiveFile = ("manifest/environment.json", "{}")
        };

        await AssertCodeAsync(fixture.CreateArchive(), "bundle.duplicate_entry");
    }

    [Fact]
    public async Task InspectAsync_PathTraversalEntry_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ExtraArchiveFile = ("../escape.txt", "unsafe") };

        await AssertCodeAsync(fixture.CreateArchive(), "bundle.unsafe_path");
    }

    [Fact]
    public async Task InspectAsync_WrongTargetBuild_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(fileVersion: "1.0.0.2");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.mismatch");
    }

    [Fact]
    public async Task InspectAsync_MissingManifestClientBuild_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { OmitManifestClientBuild = true };

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.client_build_missing");
    }

    [Fact]
    public async Task InspectAsync_MissingManifestValidationStatus_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { OmitManifestValidationStatus = true };

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.client_build_validation_status_missing");
    }

    [Fact]
    public async Task InspectAsync_BlockedManifestValidationStatus_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture
        {
            ManifestValidationStatus = "EvidenceBlockedClientBuildMismatch"
        };

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.validation_status_not_trusted");
    }

    [Fact]
    public async Task InspectAsync_ManifestExactBindingFalse_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ManifestExactBindingValidated = false };

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.exact_binding_not_validated");
    }

    [Fact]
    public async Task InspectAsync_ManifestExactBindingMissing_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ManifestExactBindingValidated = null };

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.exact_binding_missing");
    }

    [Fact]
    public async Task InspectAsync_ManifestRecoveryAttestationBlocked_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture
        {
            ManifestRecoveryAttestationStatus = "EvidenceBlockedRecoveryBuildAttestationIncomplete"
        };

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.recovery_attestation_not_trusted");
    }

    [Fact]
    public async Task InspectAsync_ManifestComputedIdentityMismatch_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture { ManifestComputedArchitecture = "x64" };

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.computed_identity_mismatch");
    }

    [Fact]
    public async Task InspectAsync_MissingBuildIdentityBindingStatus_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(bindingStatus: null);

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.binding_status_missing");
    }

    [Fact]
    public async Task InspectAsync_BlockedBuildIdentityBindingStatus_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(bindingStatus: "EvidenceBlockedClientIdentityNotComputed");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.validation_status_not_trusted");
    }

    [Fact]
    public async Task InspectAsync_BuildIdentityExactBindingFalse_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(exactBindingValidated: false);

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.exact_binding_not_validated");
    }

    [Fact]
    public async Task InspectAsync_BuildIdentityRecoveryAttestationBlocked_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(recoveryAttestationStatus: "EvidenceBlockedRecoveryBuildAttestationIncomplete");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.recovery_attestation_not_trusted");
    }

    [Fact]
    public async Task InspectAsync_BuildIdentityComputedIdentityMismatch_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(computedFileVersion: "1.0.0.2");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.computed_identity_mismatch");
    }

    [Fact]
    public async Task InspectAsync_ManifestAndBuildIdentityCaseMismatch_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        var lowerSha256 = RecoveryBundleContract.ExpectedClientSha256.ToLowerInvariant();
        fixture.SetBuildIdentity(sha256: lowerSha256, computedSha256: lowerSha256);

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.manifest_mismatch");
    }

    [Fact]
    public async Task InspectAsync_DerivedBuildIdentityDescriptor_IsNotTrusted()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetBuildIdentity(descriptorAuthority: "DERIVED");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.authority_not_promotable");
    }

    [Fact]
    public async Task InspectAsync_UnsupportedDescriptorAuthority_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetDescriptorAuthority("manifest/environment.json", "UNREVIEWED");

        await AssertCodeAsync(fixture.CreateArchive(), "manifest.file_authority_invalid");
    }

    [Fact]
    public async Task InspectAsync_InvalidJsonLine_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetRaw("canonical/items.jsonl", "{invalid-json}\n", "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "schema.jsonl_invalid");
    }

    [Fact]
    public async Task InspectAsync_SimplifiedProductionDisplayText_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/maps.jsonl",
        [
            new { schemaVersion = "god2-map-v1", authority = "VERIFIED", id = 1, nameZhTw = "\u6D4B\u8BD5\u5730\u56FE" }
        ], "VERIFIED", "god2-map-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.simplified_chinese_production_text");
    }

    [Fact]
    public async Task InspectAsync_BrokenCanonicalReference_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/monster-drops.jsonl",
        [
            new
            {
                schemaVersion = "god2-monster-drop-v1",
                authority = "UNKNOWN_SERVER_ONLY",
                monsterId = 999,
                itemId = 10,
                authoritativeRate = (decimal?)null,
                rateAuthority = "UNKNOWN_SERVER_ONLY",
                defaultDisabledRate = 0m,
                enabled = false
            }
        ], "UNKNOWN_SERVER_ONLY", "god2-monster-drop-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.broken_reference");
    }

    [Fact]
    public async Task InspectAsync_ReferenceToAbsentTargetDomain_IsRejectedAsOrphan()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.Remove("canonical/items.jsonl");

        var exception = await AssertCodeAsync(fixture.CreateArchive(), "content.broken_reference");

        Assert.Contains("1 broken canonical reference(s) across 1 distinct orphan record(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_TwoBrokenReferencesFromOneRecord_CountOneDistinctOrphan()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.Remove("canonical/items.jsonl");
        fixture.Remove("canonical/monsters.jsonl");

        var exception = await AssertCodeAsync(fixture.CreateArchive(), "content.broken_reference");

        Assert.Contains("2 broken canonical reference(s) across 1 distinct orphan record(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_MissingSchemaRequiredPrimaryIdentity_IsRejectedAndCounted()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = "VERIFIED", nameZhTw = "\u7121\u7DE8\u865F\u7269\u54C1" }
        ], "VERIFIED", "god2-item-v1");

        var exception = await AssertCodeAsync(fixture.CreateArchive(), "content.missing_primary_identity");

        Assert.Contains("1 canonical record(s) without a required primary identity", exception.Message, StringComparison.Ordinal);
        Assert.Contains("2 distinct orphan record(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_RenamedCanonicalFile_UsesSchemaIdentityContract()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.Remove("canonical/items.jsonl");
        fixture.SetJsonLines("canonical/renamed-content.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = "VERIFIED", id = 10, nameZhTw = "\u6E2C\u8A66\u77F3" }
        ], "VERIFIED", "god2-item-v1");

        var inspection = await _reader.InspectAsync(fixture.CreateArchive(), CancellationToken.None);

        Assert.Equal(0, inspection.ContentValidation.BrokenReferenceCount);
        Assert.Equal(0, inspection.ContentValidation.ContentOrphanCount);
    }

    [Fact]
    public async Task InspectAsync_DuplicateCanonicalIdentity_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = "VERIFIED", id = 10, nameZhTw = "\u6E2C\u8A66\u77F3" },
            new { schemaVersion = "god2-item-v1", authority = "VERIFIED", id = 10, nameZhTw = "\u6E2C\u8A66\u6728" }
        ], "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.duplicate_identity");
    }

    [Fact]
    public async Task InspectAsync_UntrustedAuthorityProductionPromotion_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new
            {
                schemaVersion = "god2-item-v1",
                authority = "HYPOTHESIS",
                id = 10,
                nameZhTw = "\u6E2C\u8A66\u77F3",
                enabled = true
            }
        ], "HYPOTHESIS", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "authority.illegal_production_promotion");
    }

    [Fact]
    public async Task InspectAsync_DerivedDescriptorCannotDirectlyEnableProduction_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new
            {
                schemaVersion = "god2-item-v1",
                authority = "DERIVED",
                id = 10,
                nameZhTw = "\u6E2C\u8A66\u77F3",
                enabled = true
            }
        ], "DERIVED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "authority.illegal_production_promotion");
    }

    [Fact]
    public async Task InspectAsync_RecordSchemaVersionMissing_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { authority = "VERIFIED", id = 10, nameZhTw = "\u6E2C\u8A66\u77F3" }
        ], "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "schema.version_missing");
    }

    [Fact]
    public async Task InspectAsync_RecordSchemaVersionMismatch_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v2", authority = "VERIFIED", id = 10, nameZhTw = "\u6E2C\u8A66\u77F3" }
        ], "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "schema.version_mismatch");
    }

    [Fact]
    public async Task InspectAsync_NonStringAuthority_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = 1, id = 10, nameZhTw = "\u6E2C\u8A66\u77F3" }
        ], "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "authority.type_invalid");
    }

    [Fact]
    public async Task InspectAsync_PromotableEnvelopeCannotHideUntrustedRecord_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/items.jsonl",
        [
            new { schemaVersion = "god2-item-v1", authority = "HYPOTHESIS", id = 10, nameZhTw = "\u6E2C\u8A66\u77F3", enabled = false }
        ], "VERIFIED", "god2-item-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "authority.envelope_overstates_record");
    }

    [Fact]
    public async Task InspectAsync_NestedUntrustedAuthorityCannotHideInsidePromotableEnvelope_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJson("runtime/formulas.json", new
        {
            schemaVersion = "god2-formulas-v1",
            authority = "DERIVED",
            formulas = new[]
            {
                new { authority = "HYPOTHESIS", enabled = false, expression = "unknown" }
            }
        }, "DERIVED", "god2-formulas-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "authority.envelope_overstates_record");
    }

    [Fact]
    public async Task InspectAsync_ExactBuildIdentityWithUntrustedAuthority_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetDescriptorAuthority("manifest/build-identity.json", "OBSERVED");

        await AssertCodeAsync(fixture.CreateArchive(), "build_identity.authority_not_promotable");
    }

    [Fact]
    public async Task InspectAsync_NormalizedDuplicateJsonProperty_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetRaw(
            "manifest/environment.json",
            """{"schemaVersion":"god2-environment-v1","schema_version":"god2-environment-v1","authority":"OBSERVED"}""",
            "OBSERVED",
            "god2-environment-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "schema.duplicate_property");
    }

    [Fact]
    public async Task StageAsync_SourceZipChangedAfterInspection_IsRejectedBeforeWriting()
    {
        using var fixture = new RecoveryBundleFixture();
        using var replacement = new RecoveryBundleFixture();
        replacement.SetJson("manifest/environment.json", new
        {
            schemaVersion = "god2-environment-v1",
            authority = "OBSERVED",
            os = "Replacement"
        }, "OBSERVED", "god2-environment-v1");
        var archivePath = fixture.CreateArchive();
        var inspection = await _reader.InspectAsync(archivePath, CancellationToken.None);
        File.Copy(replacement.CreateArchive(), archivePath, overwrite: true);
        var stagingRoot = Path.Combine(fixture.OutputRoot, "staging");

        var exception = await Assert.ThrowsAsync<RecoveryImportException>(
            () => _reader.StageAsync(inspection, stagingRoot, CancellationToken.None));

        Assert.Equal("bundle.changed_before_staging", exception.Code);
        Assert.False(Directory.Exists(stagingRoot));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InspectAsync_UnknownDropMustDeclareNullRateAndDisabledFlag_IsRejected(
        bool includeAuthoritativeRate,
        bool includeEnabled)
    {
        using var fixture = new RecoveryBundleFixture();
        var record = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "god2-monster-drop-v1",
            ["authority"] = "UNKNOWN_SERVER_ONLY",
            ["monsterId"] = 1,
            ["itemId"] = 10,
            ["rateAuthority"] = "UNKNOWN_SERVER_ONLY",
            ["defaultDisabledRate"] = 0m
        };
        if (includeAuthoritativeRate)
        {
            record["authoritativeRate"] = null;
        }
        if (includeEnabled)
        {
            record["enabled"] = false;
        }
        fixture.SetJsonLines("canonical/monster-drops.jsonl", [record], "UNKNOWN_SERVER_ONLY", "god2-monster-drop-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.unknown_drop_unsafe");
    }

    [Fact]
    public async Task InspectAsync_ExactNativeWriterMonsterDropLine_WithFieldLevelUnknownRate_IsAccepted()
    {
        using var fixture = new RecoveryBundleFixture();
        var nativeWriterBytes = await File.ReadAllBytesAsync(
            Path.Combine(
                fixture.RepositoryRoot,
                "tests",
                "God2.RecoveredDatabaseImporter.Tests",
                "Fixtures",
                "native-writer-monster-drop.jsonl"),
            CancellationToken.None);
        Assert.Equal(1094, nativeWriterBytes.Length);
        Assert.Equal(
            "a83799bd9a0e8ebb876d47b04074e022bd7b66f4179de2608be82aa0a9c7db4d",
            Convert.ToHexString(SHA256.HashData(nativeWriterBytes)).ToLowerInvariant());
        fixture.SetRaw(
            "canonical/monster-drops.jsonl",
            Encoding.UTF8.GetString(nativeWriterBytes),
            "OBSERVED",
            "god2-ultimate-recovery-v1");

        var inspection = await _reader.InspectAsync(fixture.CreateArchive(), CancellationToken.None);

        Assert.Equal(1, inspection.ContentValidation.UnknownDropSafetyCount);
    }

    [Fact]
    public async Task InspectAsync_ObservedMonsterDropCannotBypassFieldLevelRateAuthority_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("content/monsters-drops.jsonl",
        [
            new
            {
                schemaVersion = "god2-ultimate-recovery-v1",
                family = "MonsterDrop",
                authority = "OBSERVED",
                monsterId = 1,
                itemId = 10,
                authoritativeRate = (decimal?)null,
                defaultDisabledRate = 0m,
                enabled = false,
                rateAuthority = "OBSERVED"
            }
        ], "OBSERVED", "god2-ultimate-recovery-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.unknown_drop_unsafe");
    }

    [Fact]
    public async Task InspectAsync_UnknownDropCannotHideConflictingEnablementAlias_IsRejected()
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/monster-drops.jsonl",
        [
            new
            {
                schemaVersion = "god2-monster-drop-v1",
                authority = "DERIVED",
                monsterId = 1,
                itemId = 10,
                authoritativeRate = (decimal?)null,
                authoritativeRateAuthority = "UNKNOWN_SERVER_ONLY",
                defaultDisabledRate = 0m,
                enabled = false,
                isDropEnabled = true
            }
        ], "VERIFIED", "god2-monster-drop-v1");

        await AssertCodeAsync(fixture.CreateArchive(), "content.unknown_drop_unsafe");
    }

    [Theory]
    [InlineData(0.25, 0, false)]
    [InlineData(null, 1, false)]
    [InlineData(null, 0, true)]
    public async Task InspectAsync_UnsafeUnknownDrop_IsRejected(double? authoritativeRate, double disabledRate, bool enabled)
    {
        using var fixture = new RecoveryBundleFixture();
        fixture.SetJsonLines("canonical/monster-drops.jsonl",
        [
            new
            {
                schemaVersion = "god2-monster-drop-v1",
                authority = "UNKNOWN_SERVER_ONLY",
                monsterId = 1,
                itemId = 10,
                authoritativeRate,
                rateAuthority = "UNKNOWN_SERVER_ONLY",
                defaultDisabledRate = disabledRate,
                enabled
            }
        ], "UNKNOWN_SERVER_ONLY", "god2-monster-drop-v1");

        await AssertCodeAsync(fixture.CreateArchive(), enabled
            ? "authority.illegal_production_promotion"
            : "content.unknown_drop_unsafe");
    }

    private async Task<RecoveryImportException> AssertCodeAsync(string archivePath, string code)
    {
        var exception = await Assert.ThrowsAsync<RecoveryImportException>(
            () => _reader.InspectAsync(archivePath, CancellationToken.None));
        Assert.Equal(code, exception.Code);
        return exception;
    }
}
