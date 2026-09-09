using System.Security.Cryptography;
using System.Text;
using God2.AdvancedHeadlessVerification;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class AdvancedFinalFreezeRevalidationTests
{
    [Fact]
    public void Evidence_validation_rehashes_bound_artifacts_at_finalize_time()
    {
        var runRoot = Path.Combine(Path.GetTempPath(), $"god2-evidence-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runRoot);
        try
        {
            var identity = Identity();
            var now = DateTimeOffset.UtcNow;
            var artifacts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build"] = "build-summary.json",
                ["regression"] = "regression-summary.json",
                ["code-review"] = "code-review-remediation-summary.json",
                ["mariadb"] = "mariadb-summary.json",
                ["map"] = "map-summary.json",
                ["soak"] = "soak-summary.json",
                ["security"] = "security-scan-summary.json"
            };
            foreach (var artifact in artifacts.Values)
            {
                RevalidationJson.Write(Path.Combine(runRoot, artifact), new
                {
                    identity.RunId,
                    identity.BuildIdentityId,
                    identity.SourceManifestHash,
                    StartTimeUtc = now.AddMinutes(-1),
                    EndTimeUtc = now,
                    Status = "PASS"
                });
            }

            var entries = artifacts.Select(pair => new RevalidationEvidenceEntry(
                pair.Key,
                pair.Key,
                identity.BuildIdentityId,
                identity.SourceManifestHash,
                AssemblyHash(identity, pair.Key),
                now.AddMinutes(-1),
                now,
                identity.AdvancedVerificationToolHash,
                RevalidationJson.Hash(Path.Combine(runRoot, pair.Value)),
                "PASS",
                "Fresh",
                [],
                [],
                null,
                pair.Value)).ToArray();
            RevalidationJson.Write(Path.Combine(runRoot, "evidence-manifest.json"), new
            {
                SchemaVersion = "advanced-final-freeze-evidence-v1",
                identity.RunId,
                identity.BuildIdentityId,
                identity.SourceManifestHash,
                GeneratedAtUtc = now,
                Evidence = entries
            });
            RevalidationJson.Write(Path.Combine(runRoot, "evidence-freshness.json"), new
            {
                SchemaVersion = "advanced-final-freeze-freshness-v1",
                identity.RunId,
                identity.BuildIdentityId,
                Status = "PASS",
                FreshCount = entries.Length,
                NonFreshCount = 0,
                Entries = entries.Select(value => new { value.EvidenceId, value.FreshnessStatus })
            });

            var valid = RevalidationFinalizer.ValidateEvidenceBindings(runRoot, identity);
            Assert.True(valid.Passed, string.Join(',', valid.Blockers));

            File.AppendAllText(Path.Combine(runRoot, "soak-summary.json"), " ", Encoding.UTF8);
            var tampered = RevalidationFinalizer.ValidateEvidenceBindings(runRoot, identity);
            Assert.False(tampered.Passed);
            Assert.False(tampered.ArtifactHashesMatch);
            Assert.Contains("EvidenceArtifactHash:soak", tampered.Blockers);
        }
        finally
        {
            if (Directory.Exists(runRoot)) Directory.Delete(runRoot, recursive: true);
        }
    }

    [Fact]
    public void Security_inspection_reads_restricted_content_without_exporting_values()
    {
        var path = Path.Combine(Path.GetTempPath(), $"god2-security-test-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllText(path, "password=\"actual-secret-value\"\npayload=" + new string('A', 160), Encoding.UTF8);

            var result = RevalidationSecurityScanner.InspectFileForTesting(path);

            Assert.Equal(1, result.PlaintextPasswordFindings);
            Assert.Equal(1, result.SensitivePayloadFindings);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Security_inspection_distinguishes_test_and_service_identifiers_from_real_accounts()
    {
        var safePath = Path.Combine(Path.GetTempPath(), $"god2-safe-identifiers-{Guid.NewGuid():N}.json");
        var unsafePath = Path.Combine(Path.GetTempPath(), $"god2-real-identifier-{Guid.NewGuid():N}.json");
        var disguisedSecretPath = Path.Combine(Path.GetTempPath(), $"god2-disguised-secret-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(safePath, "account=\"god2test\"\nusername=\"god2_server\"", Encoding.UTF8);
            File.WriteAllText(unsafePath, "account=\"customer-account-42\"", Encoding.UTF8);
            File.WriteAllText(disguisedSecretPath, "password=\"god2test\"", Encoding.UTF8);

            Assert.Equal(0, RevalidationSecurityScanner.InspectFileForTesting(safePath).CredentialFindings);
            Assert.Equal(1, RevalidationSecurityScanner.InspectFileForTesting(unsafePath).ReusableTokenFindings);
            Assert.Equal(1, RevalidationSecurityScanner.InspectFileForTesting(disguisedSecretPath).PlaintextPasswordFindings);
        }
        finally
        {
            if (File.Exists(safePath)) File.Delete(safePath);
            if (File.Exists(unsafePath)) File.Delete(unsafePath);
            if (File.Exists(disguisedSecretPath)) File.Delete(disguisedSecretPath);
        }
    }

    [Fact]
    public void Security_binary_streaming_does_not_double_count_overlap_matches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"god2-security-overlap-{Guid.NewGuid():N}.bin");
        try
        {
            var prefix = new string('X', (64 * 1024) - 161) + "\n";
            File.WriteAllText(path, prefix + "password=\"actual-secret-value\"" + new string('Y', 1024), Encoding.Latin1);

            Assert.Equal(1, RevalidationSecurityScanner.InspectFileForTesting(path).PlaintextPasswordFindings);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Generated_public_report_validation_rejects_sensitive_content()
    {
        RevalidationSecurityScanner.ValidateGeneratedPublicReport("RunId: safe-run\nStatus: derived");
        Assert.Throws<InvalidOperationException>(() =>
            RevalidationSecurityScanner.ValidateGeneratedPublicReport("password=\"actual-secret-value\""));
    }

    [Fact]
    public void Diagnostic_sanitizer_removes_credentials_payloads_and_absolute_paths()
    {
        var input = "password=actual-secret-value at C:\\Users\\Operator\\server.cs\npayload=" + new string('A', 160);

        var sanitized = RevalidationSecurityScanner.SanitizeDiagnostic(input);

        Assert.DoesNotContain("actual-secret-value", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new string('A', 160), sanitized, StringComparison.Ordinal);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_source_scan_ignores_expressions_but_detects_quoted_literals()
    {
        var expressionPath = Path.Combine(Path.GetTempPath(), $"god2-password-expression-{Guid.NewGuid():N}.cs");
        var literalPath = Path.Combine(Path.GetTempPath(), $"god2-password-literal-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(expressionPath, "settings.Password = options.Password;", Encoding.UTF8);
            File.WriteAllText(literalPath, "var password = \"actual-secret-value\";", Encoding.UTF8);

            Assert.Equal(0, RevalidationSecurityScanner.InspectFileForTesting(expressionPath).CredentialFindings);
            Assert.Equal(1, RevalidationSecurityScanner.InspectFileForTesting(literalPath).PlaintextPasswordFindings);
        }
        finally
        {
            if (File.Exists(expressionPath)) File.Delete(expressionPath);
            if (File.Exists(literalPath)) File.Delete(literalPath);
        }
    }

    [Fact]
    public void Security_copy_check_includes_bin_and_publish_outputs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"god2-local-copy-test-{Guid.NewGuid():N}");
        var approved = Path.Combine(root, "config", "test-accounts.local.json");
        var leaked = Path.Combine(root, "src", "Host", "bin", "Release", "publish", "test-accounts.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(approved)!);
        Directory.CreateDirectory(Path.GetDirectoryName(leaked)!);
        try
        {
            File.WriteAllText(approved, "{}", Encoding.UTF8);
            File.WriteAllText(leaked, "{}", Encoding.UTF8);

            Assert.Equal(1, RevalidationSecurityScanner.CountUnexpectedLocalAccountCopies(root, approved));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resource_soak_live_diagnostics_report_activity_and_drain_to_zero()
    {
        var diagnostics = new ResourceSoakLiveDiagnostics();
        using (diagnostics.TrackSession())
        using (diagnostics.TrackBattleActor())
        using (diagnostics.TrackQueueWork())
        using (diagnostics.TrackOutbox(2))
        using (diagnostics.TrackJournal(3))
        using (diagnostics.TrackPendingTimer())
        using (diagnostics.TrackKeyedLocks(4, 5))
        using (diagnostics.TrackIdempotency(6))
        using (diagnostics.TrackTransaction())
        {
            Assert.Equal(new ResourceSoakLiveSnapshot(1, 1, 1, 2, 3, 1, 4, 5, 6, 1), diagnostics.Snapshot());
        }

        Assert.Equal(new ResourceSoakLiveSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), diagnostics.Snapshot());
        Assert.Equal(new ResourceSoakLiveSnapshot(1, 1, 1, 2, 3, 1, 4, 5, 6, 1), diagnostics.PeakSnapshot());
    }

    private static RevalidationBuildIdentity Identity() => new(
        "advanced-final-freeze-build-identity-v1",
        "test-run",
        DateTimeOffset.UtcNow,
        "test-build",
        "God2ClassicServer.sln",
        ["Debug", "Release"],
        ["net10.0"],
        "source-hash",
        "project-hash",
        "build-output-hash",
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["tests/a.dll"] = "test-assembly" },
        new Dictionary<string, string> { ["src/runtime.dll"] = "runtime-assembly" },
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        "offline-tool",
        "advanced-tool",
        "migration.sql",
        "migration-hash",
        "client-build",
        "client-hash",
        new Dictionary<string, string>(),
        []);

    private static string AssemblyHash(RevalidationBuildIdentity identity, string evidenceId) => evidenceId switch
    {
        "build" => identity.BuildOutputManifestHash,
        "regression" or "code-review" => Composite(identity.TestAssemblyHashes),
        "mariadb" or "soak" => Composite(identity.RuntimeAssemblyHashes),
        "map" => identity.OfflineReverseEngineeringToolHash,
        "security" => identity.AdvancedVerificationToolHash,
        _ => string.Empty
    };

    private static string Composite(IReadOnlyDictionary<string, string> values)
    {
        var canonical = string.Join('\n', values.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => $"{value.Key}|{value.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
