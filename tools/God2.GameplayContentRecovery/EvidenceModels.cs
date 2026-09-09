using System.Text.Json;

namespace God2.GameplayContentRecovery;

public static class RecoveryVersions
{
    public const string Phase = "GameplayContentRecoveryPhase1";
    public const string Extractor = "god2-content-extractor/1.0.0";
    public const string Phase2 = "GameplayContentRecoveryPhase2";
    public const string ExtractorPhase2 = "god2-content-extractor/2.0.0";
    public const string Phase3 = "GameplayContentRecoveryPhase3";
    public const string ExtractorPhase3 = "god2-semantic-promoter/3.0.0";
    public const string ConverterName = "OpenccNetLib-S2Twp";
    public const string ConverterVersion = "1.6.1";
    public const string GlossaryVersion = "god2-zh-tw/1.0.0";
    public const string TargetLanguage = "zh-TW";
}

public sealed record SourceInventoryEntry(
    string SourceIdentity,
    string SourceType,
    string SourceFile,
    string SourceHash,
    long SizeBytes,
    int? RecordCount,
    string? Decoder,
    string ContentClassification);

public sealed record RawEvidenceRecord(
    string RecordId,
    string Domain,
    string SourceType,
    string SourceFile,
    string SourceIdentity,
    int? SourceRow,
    long? SourceOffset,
    string SourceHash,
    string OriginalLanguage,
    string? OriginalText,
    string RawMetadata,
    string SourcePayloadHash,
    string Confidence,
    string EvidenceStatus);

public sealed record LocalizationResult(
    string OriginalText,
    string OriginalLanguage,
    string ConvertedText,
    string ConversionMethod,
    string ConversionStatus,
    string LanguageReviewStatus,
    bool PlaceholderPreserved,
    bool MarkupPreserved,
    bool ControlCodesPreserved,
    string SourceHash,
    string ConvertedTextHash);

public sealed record LocalizationAuditRecord(
    string LocalizationId,
    string Domain,
    string AuthorityKey,
    string FieldName,
    LocalizationResult Result);

public sealed record StagingRecord(
    string StagingId,
    string Domain,
    string AuthorityKey,
    string? OriginalText,
    string OriginalLanguage,
    string? ConvertedText,
    string ConversionMethod,
    string SourceHash,
    string? ConvertedTextHash,
    string ConversionStatus,
    string LanguageReviewStatus,
    string TransformationRule,
    string NormalizedData,
    string? OpaqueMetadata,
    string EvidenceStatus);

public sealed record ValidationFinding(
    string ValidationId,
    string Domain,
    string AuthorityKey,
    string Gate,
    string Status,
    string Severity,
    string Details);

public sealed record ValidatedRecord(
    string ValidatedId,
    string Domain,
    string AuthorityKey,
    string NormalizedData,
    string NormalizedHash,
    string EvidenceStatus,
    string LocalizationStatus,
    string SourceHash);

public sealed record ProductionPromotion(
    string Domain,
    string AuthorityKey,
    string TargetTable,
    string TargetRowIdentity,
    string NormalizedHash,
    string EvidenceStatus,
    string LocalizationStatus);

public sealed record GlossaryEntry(
    string SimplifiedText,
    string TraditionalText,
    string Category,
    string Source,
    string SourceIdentity,
    string Confidence,
    string VerifiedBy,
    string Notes,
    string GlossaryVersion = RecoveryVersions.GlossaryVersion);

public sealed record ContentEntity(
    string Domain,
    string AuthorityKey,
    string SourceFile,
    string SourceIdentity,
    int? SourceRow,
    string SourceHash,
    string? Name,
    string? Description,
    string OriginalLanguage,
    string Confidence,
    string EvidenceStatus,
    Dictionary<string, object?> Fields,
    IReadOnlyList<string> MissingRequiredFields,
    string TransformationRule);

public sealed class RecoveryWorkspace
{
    public string Phase { get; init; } = RecoveryVersions.Phase;

    public string ExtractorVersion { get; init; } = RecoveryVersions.Extractor;

    public required string RunId { get; init; }

    public required DateTime StartedAtUtc { get; init; }

    public List<SourceInventoryEntry> Sources { get; } = [];

    public List<RawEvidenceRecord> Raw { get; } = [];

    public List<ContentEntity> Entities { get; } = [];

    public List<LocalizationAuditRecord> Localization { get; } = [];

    public List<StagingRecord> Staging { get; } = [];

    public List<ValidationFinding> Validation { get; } = [];

    public List<ValidatedRecord> Validated { get; } = [];

    public List<ProductionPromotion> Promotions { get; } = [];

    public Dictionary<string, object?> Diagnostics { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> DatabaseBefore { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> DatabaseAfter { get; } = new(StringComparer.Ordinal);
}

public static class RecoveryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Compact<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}
