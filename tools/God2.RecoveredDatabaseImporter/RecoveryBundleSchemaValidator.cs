using System.Text.Json;
using God2.GameplayContentRecovery;

namespace God2.RecoveredDatabaseImporter;

public sealed class RecoveryBundleSchemaValidator
{
    private readonly ZhTwLocalization _localization = new(God2Glossary.Create());
    private readonly Dictionary<string, HashSet<string>> _identities = new(StringComparer.Ordinal);
    private readonly List<CanonicalReference> _references = [];
    private readonly HashSet<string> _orphanRecords = new(StringComparer.Ordinal);
    private readonly List<string> _findings = [];
    private int _jsonDocuments;
    private int _jsonLines;
    private int _authorityValues;
    private int _promotions;
    private int _traditionalFindings;
    private int _unknownDropSafety;
    private int _missingPrimaryIdentities;

    public void ValidateJsonDocument(
        string relativePath,
        RecoveryBundleFileDescriptor descriptor,
        JsonElement root,
        bool isJsonLine)
    {
        if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new RecoveryImportException(
                "schema.root_not_object_or_array",
                $"Structured recovery data must use an object or array root: {relativePath}.");
        }

        if (isJsonLine)
        {
            _jsonLines++;
        }
        else
        {
            _jsonDocuments++;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in root.EnumerateArray())
            {
                ValidateRecord(relativePath, descriptor, item, $"[{index++}]");
            }
            return;
        }

        ValidateRecord(relativePath, descriptor, root, isJsonLine ? $"$line[{_jsonLines}]" : "$");
    }

    public ContentValidationSummary Complete()
    {
        var broken = 0;
        foreach (var reference in _references)
        {
            if (!_identities.TryGetValue(reference.TargetDomain, out var identities) || !identities.Contains(reference.Identity))
            {
                broken++;
                _orphanRecords.Add(reference.Source);
                _findings.Add($"Broken reference: {reference.Source} -> {reference.TargetDomain}:{reference.Identity}");
            }
        }

        var orphanCount = _orphanRecords.Count;

        if (_missingPrimaryIdentities != 0)
        {
            throw new RecoveryImportException(
                "content.missing_primary_identity",
                $"Recovery Bundle contains {_missingPrimaryIdentities} canonical record(s) without a required primary identity, {broken} broken reference(s), and {orphanCount} distinct orphan record(s). First: {_findings[0]}");
        }

        if (broken != 0)
        {
            throw new RecoveryImportException(
                "content.broken_reference",
                $"Recovery Bundle contains {broken} broken canonical reference(s) across {orphanCount} distinct orphan record(s). First: {_findings.First(value => value.StartsWith("Broken reference:", StringComparison.Ordinal))}");
        }

        return new ContentValidationSummary(
            _jsonDocuments,
            _jsonLines,
            _authorityValues,
            _promotions,
            _traditionalFindings,
            broken,
            orphanCount,
            _unknownDropSafety,
            _findings.ToArray());
    }

    private void ValidateRecord(
        string relativePath,
        RecoveryBundleFileDescriptor descriptor,
        JsonElement record,
        string recordPath)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            throw new RecoveryImportException(
                "schema.record_not_object",
                $"Recovery record must be a JSON object: {relativePath}{recordPath}.");
        }

        var schemaVersion = TryGetString(record, "schemaVersion");
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new RecoveryImportException(
                "schema.version_missing",
                $"Recovery record is missing schemaVersion: {relativePath}{recordPath}.");
        }
        if (!string.Equals(schemaVersion, descriptor.SchemaVersion, StringComparison.Ordinal))
        {
            throw new RecoveryImportException(
                "schema.version_mismatch",
                $"Record schemaVersion does not match its manifest descriptor: {relativePath}{recordPath}; record={schemaVersion}; manifest={descriptor.SchemaVersion}.");
        }

        ValidateAuthorityValues(relativePath, record, recordPath);
        var recordAuthority = TryGetString(record, "authority");
        if (relativePath.StartsWith("canonical/", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(recordAuthority))
        {
            throw new RecoveryImportException(
                "authority.canonical_record_missing",
                $"Canonical records must declare authority explicitly: {relativePath}{recordPath}.");
        }
        var authority = recordAuthority ?? descriptor.Authority;
        ValidateAuthority(authority, $"{relativePath}{recordPath}");
        _promotions += ValidateAuthorityEnvelopeAndPromotions(
            relativePath,
            descriptor,
            record,
            authority,
            recordPath);

        if (relativePath.StartsWith("canonical/", StringComparison.OrdinalIgnoreCase))
        {
            ValidateProductionDisplayText(relativePath, record, recordPath);
            IndexCanonicalRecord(relativePath, descriptor, record, recordPath);
        }

        if ((relativePath.StartsWith("canonical/", StringComparison.OrdinalIgnoreCase) ||
             relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase)) &&
            IsMonsterDropRecord(relativePath, descriptor.SchemaVersion, record))
        {
            ValidateUnknownDropSafety(relativePath, record, recordPath);
        }
    }

    private void ValidateAuthorityValues(string relativePath, JsonElement element, string path)
    {
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (Normalize(property.Name).EndsWith("authority", StringComparison.Ordinal))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new RecoveryImportException(
                        "authority.type_invalid",
                        $"Evidence authority must be a string at {relativePath}{propertyPath}.");
                }
                var authority = property.Value.GetString() ?? string.Empty;
                ValidateAuthority(authority, $"{relativePath}{propertyPath}");
                _authorityValues++;
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                ValidateAuthorityValues(relativePath, property.Value, propertyPath);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                ValidateAuthorityArray(relativePath, property.Value, propertyPath);
            }
        }
    }

    private void ValidateAuthorityArray(string relativePath, JsonElement array, string path)
    {
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                ValidateAuthorityValues(relativePath, item, $"{path}[{index}]");
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                ValidateAuthorityArray(relativePath, item, $"{path}[{index}]");
            }
            index++;
        }
    }

    private static void ValidateAuthority(string authority, string source)
    {
        if (!RecoveryBundleContract.AllowedAuthorities.Contains(authority))
        {
            throw new RecoveryImportException(
                "authority.invalid",
                $"Unsupported evidence authority '{authority}' at {source}.");
        }
    }

    private static int ValidateAuthorityEnvelopeAndPromotions(
        string relativePath,
        RecoveryBundleFileDescriptor descriptor,
        JsonElement element,
        string inheritedAuthority,
        string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var promotions = 0;
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    promotions += ValidateAuthorityEnvelopeAndPromotions(
                        relativePath,
                        descriptor,
                        item,
                        inheritedAuthority,
                        $"{path}[{index}]");
                }
                index++;
            }
            return promotions;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var localAuthority = TryGetString(element, "authority") ?? inheritedAuthority;
        ValidateAuthority(localAuthority, $"{relativePath}{path}");
        if (RecoveryBundleContract.CanPromote(descriptor.Authority) && !RecoveryBundleContract.CanPromote(localAuthority))
        {
            throw new RecoveryImportException(
                "authority.envelope_overstates_record",
                $"A promotable manifest descriptor cannot contain non-promotable evidence: {relativePath}{path}; file={descriptor.Authority}; record={localAuthority}.");
        }

        var result = 0;
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            var name = Normalize(property.Name);
            if ((name is "productionenabled" or "productiondropenabled" or "isdropenabled" or "enabled" or "candirectimporttogameplay") &&
                property.Value.ValueKind == JsonValueKind.True)
            {
                if (!string.Equals(descriptor.Authority, "VERIFIED", StringComparison.OrdinalIgnoreCase) ||
                    !RecoveryBundleContract.CanPromote(localAuthority))
                {
                    throw new RecoveryImportException(
                        "authority.illegal_production_promotion",
                        $"Production enablement requires a VERIFIED file descriptor and VERIFIED or DERIVED record evidence: {relativePath}{propertyPath}; file={descriptor.Authority}; record={localAuthority}.");
                }
                result++;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                result += ValidateAuthorityEnvelopeAndPromotions(
                    relativePath,
                    descriptor,
                    property.Value,
                    localAuthority,
                    propertyPath);
            }
        }
        return result;
    }

    private void ValidateProductionDisplayText(string relativePath, JsonElement element, string path)
    {
        foreach (var property in element.EnumerateObject())
        {
            var normalized = Normalize(property.Name);
            var propertyPath = $"{path}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.String && IsProductionDisplayProperty(normalized))
            {
                var value = property.Value.GetString() ?? string.Empty;
                if (_localization.ContainsConvertibleSimplified(value))
                {
                    _traditionalFindings++;
                    throw new RecoveryImportException(
                        "content.simplified_chinese_production_text",
                        $"Formal canonical display text must be Traditional Chinese: {relativePath}{propertyPath}.");
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                ValidateProductionDisplayText(relativePath, property.Value, propertyPath);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        ValidateProductionDisplayText(relativePath, item, $"{propertyPath}[{index}]");
                    }
                    index++;
                }
            }
        }
    }

    private static bool IsProductionDisplayProperty(string name)
    {
        if (name.Contains("original", StringComparison.Ordinal) ||
            name.Contains("raw", StringComparison.Ordinal) ||
            name.Contains("source", StringComparison.Ordinal) ||
            name.Contains("provenance", StringComparison.Ordinal) ||
            name.Contains("hash", StringComparison.Ordinal) ||
            name.EndsWith("id", StringComparison.Ordinal) ||
            name.Contains("schema", StringComparison.Ordinal) ||
            name.Contains("authority", StringComparison.Ordinal) ||
            name.Contains("status", StringComparison.Ordinal) ||
            name.Contains("version", StringComparison.Ordinal) ||
            name.Contains("path", StringComparison.Ordinal) ||
            name.Contains("code", StringComparison.Ordinal))
        {
            return false;
        }

        return name.Contains("name", StringComparison.Ordinal) ||
               name.Contains("description", StringComparison.Ordinal) ||
               name.Contains("text", StringComparison.Ordinal) ||
               name.Contains("title", StringComparison.Ordinal) ||
               name.Contains("objective", StringComparison.Ordinal) ||
               name.Contains("reward", StringComparison.Ordinal) ||
               name.Contains("dialog", StringComparison.Ordinal);
    }

    private void IndexCanonicalRecord(
        string relativePath,
        RecoveryBundleFileDescriptor descriptor,
        JsonElement record,
        string recordPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath).ToLowerInvariant();
        var primary = PrimaryIdentity(fileName, descriptor.SchemaVersion);
        var source = $"{relativePath}{recordPath}";
        if (primary is not null)
        {
            var identity = TryGetScalar(record, primary.Value.PropertyNames);
            if (string.IsNullOrWhiteSpace(identity) || string.Equals(identity, "0", StringComparison.Ordinal))
            {
                _missingPrimaryIdentities++;
                _orphanRecords.Add(source);
                _findings.Add($"Missing primary identity: {source} requires {primary.Value.Domain} ({string.Join("/", primary.Value.PropertyNames)}).");
            }
            else
            {
                if (!_identities.TryGetValue(primary.Value.Domain, out var values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    _identities.Add(primary.Value.Domain, values);
                }

                if (!values.Add(identity))
                {
                    throw new RecoveryImportException(
                        "content.duplicate_identity",
                        $"Duplicate canonical identity {primary.Value.Domain}:{identity} at {relativePath}{recordPath}.");
                }
            }
        }

        var schemaName = Normalize(descriptor.SchemaVersion);
        if (fileName is "portals" or "map-regions" ||
            schemaName.Contains("portal", StringComparison.Ordinal) ||
            schemaName.Contains("mapregion", StringComparison.Ordinal))
        {
            AddReferences(record, source, "Map", "sourceMapId", "targetMapId", "destinationMapId");
        }
        if (fileName.Contains("spawn", StringComparison.Ordinal) || schemaName.Contains("spawn", StringComparison.Ordinal))
        {
            AddReferences(record, source, "Map", "mapId");
            AddReferences(record, source, "Monster", "monsterId", "monsterTemplateId");
            AddReferences(record, source, "Npc", "npcId", "npcTemplateId");
        }
        if (fileName.Contains("drop", StringComparison.Ordinal) || schemaName.Contains("drop", StringComparison.Ordinal))
        {
            AddReferences(record, source, "Monster", "monsterId", "monsterTemplateId");
            AddReferences(record, source, "Item", "itemId", "itemTemplateId");
        }
        if (fileName.Contains("quest", StringComparison.Ordinal) || schemaName.Contains("quest", StringComparison.Ordinal))
        {
            AddReferences(record, source, "Npc", "npcId", "startNpcId", "endNpcId");
            AddReferences(record, source, "Item", "itemId", "itemTemplateId");
        }
    }

    private void AddReferences(JsonElement record, string source, string targetDomain, params string[] propertyNames)
    {
        var normalizedNames = propertyNames.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        Visit(record);
        return;

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (normalizedNames.Contains(Normalize(property.Name)))
                    {
                        var value = Scalar(property.Value);
                        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "0", StringComparison.Ordinal))
                        {
                            _references.Add(new CanonicalReference(source, targetDomain, value));
                        }
                    }
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
    }

    private void ValidateUnknownDropSafety(string relativePath, JsonElement record, string recordPath)
    {
        var hasAuthoritativeField = TryGetProperty(record, "authoritativeRate", out var authoritative);
        var rateAuthority = TryGetString(record, "authoritativeRateAuthority") ??
                            TryGetString(record, "rateAuthority") ??
                            TryGetString(record, "dropRateAuthority");
        if (!hasAuthoritativeField || authoritative.ValueKind != JsonValueKind.Null ||
            !string.Equals(rateAuthority, "UNKNOWN_SERVER_ONLY", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoveryImportException(
                "content.unknown_drop_unsafe",
                $"Typed MonsterDrop must declare authoritativeRate=NULL with field-level authority UNKNOWN_SERVER_ONLY: {relativePath}{recordPath}.");
        }

        var disabledRateOk = TryGetNumber(record, "defaultDisabledRate", out var disabledRate) && disabledRate == 0m;
        var enabledOk = TryGetBoolean(record, "enabled", out var enabledValue) && !enabledValue;
        var conflictingEnablement = new[]
        {
            "isDropEnabled",
            "productionDropEnabled",
            "productionEnabled",
            "canDirectImportToGameplay"
        }.Any(name => TryGetProperty(record, name, out var value) && value.ValueKind != JsonValueKind.False);
        if (!disabledRateOk || !enabledOk || conflictingEnablement)
        {
            throw new RecoveryImportException(
                "content.unknown_drop_unsafe",
                $"Unknown drop rates must remain NULL, default-disabled zero, disabled, and UNKNOWN_SERVER_ONLY: {relativePath}{recordPath}.");
        }

        _unknownDropSafety++;
    }

    private static bool IsMonsterDropRecord(string relativePath, string schemaVersion, JsonElement record)
    {
        if (relativePath.Contains("drop", StringComparison.OrdinalIgnoreCase) ||
            Normalize(schemaVersion).Contains("monsterdrop", StringComparison.Ordinal))
        {
            return true;
        }
        var family = TryGetString(record, "family") ?? TryGetString(record, "entityFamily");
        return family is not null &&
               (family.Equals("MonsterDrop", StringComparison.OrdinalIgnoreCase) ||
                family.Equals("DropRelation", StringComparison.OrdinalIgnoreCase) ||
                family.Equals("DropTable", StringComparison.OrdinalIgnoreCase));
    }

    private static (string Domain, string[] PropertyNames)? PrimaryIdentity(string fileName, string schemaVersion)
    {
        var schema = Normalize(schemaVersion);
        if (schema.StartsWith("god2mapv", StringComparison.Ordinal) || fileName == "maps")
        {
            return ("Map", ["id", "mapId"]);
        }
        if (schema.StartsWith("god2npcv", StringComparison.Ordinal) || fileName == "npcs")
        {
            return ("Npc", ["id", "npcId", "npcTemplateId"]);
        }
        if (schema.StartsWith("god2monsterv", StringComparison.Ordinal) || fileName == "monsters")
        {
            return ("Monster", ["id", "monsterId", "monsterTemplateId"]);
        }
        if (schema.StartsWith("god2itemv", StringComparison.Ordinal) || fileName == "items")
        {
            return ("Item", ["id", "itemId", "itemTemplateId"]);
        }
        if (schema.StartsWith("god2skillv", StringComparison.Ordinal) || fileName == "skills")
        {
            return ("Skill", ["id", "skillId", "skillTemplateId"]);
        }
        if (schema.StartsWith("god2questv", StringComparison.Ordinal) || fileName == "quests")
        {
            return ("Quest", ["id", "questId", "questTemplateId"]);
        }
        return null;
    }

    private static string? TryGetScalar(JsonElement element, IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value))
            {
                return Scalar(value);
            }
        }
        return null;
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        var normalized = Normalize(name);
        foreach (var property in element.EnumerateObject())
        {
            if (Normalize(property.Name) == normalized)
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    internal static string? TryGetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        if (TryGetProperty(element, name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }
        value = false;
        return false;
    }

    private static bool TryGetNumber(JsonElement element, string name, out decimal value)
    {
        if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }
        value = 0;
        return false;
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty
    };

    internal static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed record CanonicalReference(string Source, string TargetDomain, string Identity);
}
