using System.Text.Json;

namespace God2.GameplayContentRecovery;

public sealed class ContentRecoveryPipeline
{
    private readonly ZhTwLocalization _localization;

    public ContentRecoveryPipeline(ZhTwLocalization localization)
    {
        _localization = localization;
    }

    public void TransformAndValidate(RecoveryWorkspace workspace)
    {
        foreach (var entity in workspace.Entities)
        {
            var name = ConvertField(workspace, entity, "Name", entity.Name);
            var description = ConvertField(workspace, entity, "Description", entity.Description);
            var normalized = new Dictionary<string, object?>(entity.Fields, StringComparer.Ordinal)
            {
                ["authorityKey"] = entity.AuthorityKey,
                ["originalName"] = entity.Name,
                ["nameZhTw"] = name?.ConvertedText,
                ["originalDescription"] = entity.Description,
                ["descriptionZhTw"] = description?.ConvertedText,
                ["sourceType"] = RecoverySourceClassification.FromFile(entity.SourceFile),
                ["sourceFile"] = entity.SourceFile,
                ["sourceIdentity"] = entity.SourceIdentity,
                ["sourceRow"] = entity.SourceRow,
                ["sourceHash"] = entity.SourceHash,
                ["extractorVersion"] = workspace.ExtractorVersion,
                ["confidence"] = entity.Confidence,
                ["evidenceStatus"] = entity.EvidenceStatus,
                ["transformationRule"] = entity.TransformationRule,
                ["originalLanguage"] = name?.OriginalLanguage ?? description?.OriginalLanguage ?? entity.OriginalLanguage,
                ["targetLanguage"] = RecoveryVersions.TargetLanguage,
                ["conversionMethod"] = CombineMethods(name, description),
                ["converterVersion"] = RecoveryVersions.ConverterVersion,
                ["glossaryVersion"] = RecoveryVersions.GlossaryVersion,
                ["missingRequiredFields"] = entity.MissingRequiredFields
            };
            var normalizedJson = RecoveryJson.Compact(normalized);
            var localizationStatus = WorstStatus(name?.ConversionStatus, description?.ConversionStatus);
            var reviewStatus = name?.LanguageReviewStatus == "LanguageReviewRequired" || description?.LanguageReviewStatus == "LanguageReviewRequired"
                ? "LanguageReviewRequired"
                : "NotRequired";
            var stagingId = ContentHash.StableId(entity.Domain, entity.AuthorityKey, entity.SourceHash, normalizedJson);
            workspace.Staging.Add(new StagingRecord(
                stagingId,
                entity.Domain,
                entity.AuthorityKey,
                entity.Name ?? entity.Description,
                name?.OriginalLanguage ?? description?.OriginalLanguage ?? entity.OriginalLanguage,
                name?.ConvertedText ?? description?.ConvertedText,
                CombineMethods(name, description),
                entity.SourceHash,
                name?.ConvertedTextHash ?? description?.ConvertedTextHash,
                localizationStatus,
                reviewStatus,
                entity.TransformationRule,
                normalizedJson,
                RecoveryJson.Compact(new { entity.MissingRequiredFields }),
                entity.MissingRequiredFields.Count == 0 ? entity.EvidenceStatus : "EvidenceBlocked"));

            ValidateLocalization(workspace, entity, name, description);
            ValidateRanges(workspace, entity);
            ValidateRequiredFields(workspace, entity);

            if (localizationStatus != "ConversionFailed")
            {
                workspace.Validated.Add(new ValidatedRecord(
                    ContentHash.StableId("validated", stagingId),
                    entity.Domain,
                    entity.AuthorityKey,
                    normalizedJson,
                    ContentHash.Sha256(normalizedJson),
                    entity.MissingRequiredFields.Count == 0 ? entity.EvidenceStatus : "EvidenceBlocked",
                    localizationStatus,
                    entity.SourceHash));
            }
        }

        ValidateDuplicates(workspace);
        ValidateCrossReferences(workspace);
    }

    private LocalizationResult? ConvertField(RecoveryWorkspace workspace, ContentEntity entity, string fieldName, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = _localization.Convert(text, entity.SourceHash);
        workspace.Localization.Add(new LocalizationAuditRecord(
            ContentHash.StableId(entity.Domain, entity.AuthorityKey, fieldName, entity.SourceHash, text),
            entity.Domain,
            entity.AuthorityKey,
            fieldName,
            result));
        return result;
    }

    private static void ValidateLocalization(
        RecoveryWorkspace workspace,
        ContentEntity entity,
        LocalizationResult? name,
        LocalizationResult? description)
    {
        foreach (var pair in new[] { (Field: "Name", Value: name), (Field: "Description", Value: description) })
        {
            if (pair.Value is null)
            {
                continue;
            }

            var passed = pair.Value.ConversionStatus != "ConversionFailed" &&
                         pair.Value.PlaceholderPreserved &&
                         pair.Value.MarkupPreserved &&
                         pair.Value.ControlCodesPreserved;
            AddFinding(workspace, entity, $"Localization.{pair.Field}", passed, passed ? "Localization conversion and structural preservation passed." : "Localization conversion or token preservation failed.");
        }
    }

    private static void ValidateRanges(RecoveryWorkspace workspace, ContentEntity entity)
    {
        var failures = new List<string>();
        foreach (var field in new[] { "maxHp", "maxMp", "level", "mpCost", "systemSellPriceCandidate", "systemRecyclePriceCandidate", "x", "y" })
        {
            if (!entity.Fields.TryGetValue(field, out var value) || value is null)
            {
                continue;
            }

            if (Convert.ToInt64(value) < 0)
            {
                failures.Add(field);
            }
        }

        AddFinding(workspace, entity, "NumericRange", failures.Count == 0, failures.Count == 0 ? "No negative gameplay values were found." : $"Negative values: {string.Join(", ", failures)}.");
    }

    private static void ValidateRequiredFields(RecoveryWorkspace workspace, ContentEntity entity)
    {
        AddFinding(
            workspace,
            entity,
            "PromotionRequiredFields",
            entity.MissingRequiredFields.Count == 0,
            entity.MissingRequiredFields.Count == 0
                ? "All required promotion fields are evidence-backed."
                : $"EvidenceBlocked fields: {string.Join(", ", entity.MissingRequiredFields)}.",
            entity.MissingRequiredFields.Count == 0 ? "Information" : "EvidenceGap");
    }

    private static void ValidateDuplicates(RecoveryWorkspace workspace)
    {
        foreach (var group in workspace.Entities.GroupBy(entity => (entity.Domain, entity.AuthorityKey)).Where(group => group.Count() > 1))
        {
            var representative = group.First();
            workspace.Validation.Add(new ValidationFinding(
                ContentHash.StableId("duplicate", group.Key.Domain, group.Key.AuthorityKey),
                group.Key.Domain,
                group.Key.AuthorityKey,
                "DuplicateAuthorityKey",
                "FAIL",
                "Error",
                $"{group.Count()} records claim the same authority key; no record is promoted."));
        }
    }

    private static void ValidateCrossReferences(RecoveryWorkspace workspace)
    {
        var authorities = workspace.Entities.Select(entity => entity.AuthorityKey).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in workspace.Entities.Where(entity => entity.Domain == "MonsterDropEvidence"))
        {
            var references = entity.Fields.TryGetValue("itemReferences", out var value) && value is IEnumerable<string> strings ? strings : [];
            var broken = references.Where(reference => !authorities.Contains(reference)).ToArray();
            AddFinding(workspace, entity, "DropItemReference", broken.Length == 0, broken.Length == 0 ? "All resolved item references exist in staging." : $"Missing item references: {string.Join(", ", broken)}.");
        }
    }

    private static void AddFinding(RecoveryWorkspace workspace, ContentEntity entity, string gate, bool passed, string details, string failedSeverity = "Error")
    {
        workspace.Validation.Add(new ValidationFinding(
            ContentHash.StableId(entity.Domain, entity.AuthorityKey, gate),
            entity.Domain,
            entity.AuthorityKey,
            gate,
            passed ? "PASS" : "FAIL",
            passed ? "Information" : failedSeverity,
            details));
    }

    private static string CombineMethods(LocalizationResult? name, LocalizationResult? description)
    {
        var values = new[] { name?.ConversionMethod, description?.ConversionMethod }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0 ? "NotApplicable" : string.Join("+", values);
    }

    private static string WorstStatus(string? first, string? second)
    {
        var values = new[] { first, second }.Where(value => value is not null).ToArray();
        if (values.Length == 0)
        {
            return "NotApplicable";
        }

        foreach (var status in new[] { "ConversionFailed", "LanguageReviewRequired", "MixedLanguageNormalized", "ConvertedToTraditional", "TraditionalVerified", "NotApplicable" })
        {
            if (values.Contains(status, StringComparer.Ordinal))
            {
                return status;
            }
        }

        return "LanguageReviewRequired";
    }
}
