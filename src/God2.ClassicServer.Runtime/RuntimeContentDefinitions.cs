using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum ContentValidationStatus
{
    Valid,
    Quarantined
}

public sealed record ContentValidationIssue(
    string Code,
    RuntimeObjectKind RuntimeType,
    int TemplateId,
    int? MapId,
    ContentValidationStatus Status,
    string Message,
    string Source);

public sealed record ValidatedContentDefinitions<TDefinition>(
    IReadOnlyList<TDefinition> Valid,
    IReadOnlyList<TDefinition> Quarantined,
    IReadOnlyList<ContentValidationIssue> Issues);

public sealed record NpcDatabaseRecord(
    int TemplateId,
    string Code,
    string Name,
    int? MapId,
    int? PositionX,
    int? PositionY,
    WorldDirection Direction,
    string Appearance,
    string InteractionType,
    int? MerchantBindingId,
    bool Enabled,
    string RawMetadata,
    string Source,
    string ContentVersion,
    int? PlacementId = null,
    string SpawnCondition = "Always",
    OfficialNpcWireIdentity? WireIdentity = null);

public sealed record MonsterSpawnDatabaseRecord(
    int SpawnId,
    int MonsterTemplateId,
    string Code,
    string Name,
    int? MapId,
    int? PositionX,
    int? PositionY,
    WorldDirection Direction,
    TimeSpan RespawnTime,
    int SpawnRadius,
    int Count,
    bool Enabled,
    string RawMetadata,
    string Source,
    string ContentVersion,
    int? Level = null,
    long? MaximumHp = null,
    int? AttackPower = null,
    int? Defense = null,
    long? MaximumMp = null,
    int? MagicAttackPower = null,
    int? MagicDefense = null,
    int? Metal = null,
    int? Wood = null,
    int? Water = null,
    int? Fire = null,
    int? Earth = null);

public sealed record PortalDatabaseRecord(
    long PortalId,
    string Name,
    int? SourceMapId,
    int? SourceX,
    int? SourceY,
    int? TargetMapId,
    int? TargetX,
    int? TargetY,
    string TriggerType,
    string Requirement,
    bool Enabled,
    string RawMetadata,
    string Source,
    string ContentVersion,
    int SourceRadius = 0);

public sealed record MerchantDatabaseRecord(
    int MerchantTemplateId,
    int? NpcTemplateId,
    string Name,
    string MerchantGroupId,
    string CurrencyType,
    IReadOnlyList<MerchantItemDefinition> Items,
    bool Enabled,
    string RawMetadata,
    string Source,
    string ContentVersion);

public sealed class NpcContentMapper
{
    public NpcPlacementRecord Map(NpcDatabaseRecord record) =>
        new(
            record.PlacementId ?? record.TemplateId,
            record.TemplateId,
            record.MapId ?? 0,
            new WorldPosition3(record.PositionX ?? 0, record.PositionY ?? 0),
            record.Direction,
            record.SpawnCondition,
            record.MerchantBindingId,
            record.Source,
            Confidence: record.MapId is not null && record.PositionX is not null && record.PositionY is not null ? 0.80 : 0.0,
            Evidence: record.Source,
            record.Name,
            record.Appearance,
            record.InteractionType,
            record.Enabled,
            RawPosition(record.MapId, record.PositionX, record.PositionY),
            record.RawMetadata,
            record.ContentVersion,
            record.WireIdentity);

    private static string RawPosition(int? mapId, int? x, int? y) =>
        $"MapId={mapId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"};PositionX={x?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"};PositionY={y?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown"}";
}

public sealed class MonsterContentMapper
{
    public MonsterSpawnDefinition Map(MonsterSpawnDatabaseRecord record)
    {
        var mapped = TryMap(record);
        if (!mapped.Succeeded || mapped.Value is null)
        {
            throw new InvalidOperationException($"{mapped.Error.Code}: {mapped.Error.Message}");
        }

        return mapped.Value;
    }

    public OperationResult<MonsterSpawnDefinition> TryMap(MonsterSpawnDatabaseRecord record)
    {
        if (record.MapId is not int mapId ||
            record.PositionX is not int positionX ||
            record.PositionY is not int positionY ||
            record.Level is not int level ||
            record.MaximumHp is not long maximumHp ||
            record.AttackPower is not int attackPower ||
            record.Defense is not int defense)
        {
            var missing = new List<string>();
            if (record.MapId is null) missing.Add(nameof(record.MapId));
            if (record.PositionX is null) missing.Add(nameof(record.PositionX));
            if (record.PositionY is null) missing.Add(nameof(record.PositionY));
            if (record.Level is null) missing.Add(nameof(record.Level));
            if (record.MaximumHp is null) missing.Add(nameof(record.MaximumHp));
            if (record.AttackPower is null) missing.Add(nameof(record.AttackPower));
            if (record.Defense is null) missing.Add(nameof(record.Defense));
            return OperationResult<MonsterSpawnDefinition>.Failure(
                "content.monster_spawn_evidence_incomplete",
                $"Monster spawn is missing required production evidence: {string.Join(", ", missing)}.",
                record.Source);
        }

        if (!record.Enabled || mapId < 0 || positionX < 0 || positionY < 0 ||
            level < 0 || maximumHp <= 0 || attackPower < 0 || defense < 0 ||
            record.MaximumMp is < 0 ||
            record.MagicAttackPower is < 0 ||
            record.MagicDefense is < 0 ||
            record.Metal is < 0 ||
            record.Wood is < 0 ||
            record.Water is < 0 ||
            record.Fire is < 0 ||
            record.Earth is < 0 ||
            record.RespawnTime <= TimeSpan.Zero || record.SpawnRadius < 0 || record.Count <= 0)
        {
            return OperationResult<MonsterSpawnDefinition>.Failure(
                "content.monster_spawn_policy_invalid",
                "Monster spawn production values are disabled, negative, zero where prohibited, or otherwise outside the evidence-backed policy range.",
                record.Source);
        }

        return OperationResult<MonsterSpawnDefinition>.Success(new MonsterSpawnDefinition(
            record.SpawnId,
            record.MonsterTemplateId,
            mapId,
            new WorldPosition3(positionX, positionY),
            record.Direction,
            record.RespawnTime,
            record.SpawnRadius,
            record.Count,
            "StaticSpawn",
            record.Source,
            record.Name,
            record.Enabled,
            record.RawMetadata,
            record.ContentVersion,
            level,
            maximumHp,
            attackPower,
            defense,
            record.MaximumMp ?? 0,
            record.MagicAttackPower ?? 0,
            record.MagicDefense ?? 0,
            record.Metal ?? 0,
            record.Wood ?? 0,
            record.Water ?? 0,
            record.Fire ?? 0,
            record.Earth ?? 0));
    }
}

public sealed class PortalContentMapper
{
    public PortalDefinition Map(PortalDatabaseRecord record) =>
        new(
            record.PortalId,
            new PortalEndpoint(record.SourceMapId ?? 0, new WorldPosition3(record.SourceX ?? 0, record.SourceY ?? 0)),
            new PortalEndpoint(record.TargetMapId ?? 0, new WorldPosition3(record.TargetX ?? 0, record.TargetY ?? 0)),
            record.TriggerType,
            record.Requirement,
            record.Source,
            record.Name,
            record.Enabled,
            record.RawMetadata,
            record.ContentVersion,
            record.SourceRadius);
}

public sealed class MerchantContentMapper
{
    public MerchantMapping Map(MerchantDatabaseRecord record) =>
        new(
            record.MerchantTemplateId,
            record.NpcTemplateId ?? 0,
            record.NpcTemplateId ?? 0,
            record.Source,
            record.Name,
            record.MerchantGroupId,
            record.CurrencyType,
            record.Items,
            record.Enabled,
            record.RawMetadata,
            record.ContentVersion);
}

public sealed class RuntimeContentValidator
{
    public ValidatedContentDefinitions<NpcPlacementRecord> ValidateNpcs(
        IEnumerable<NpcPlacementRecord> definitions,
        IReadOnlySet<int> mapIds,
        IReadOnlySet<int> merchantIds)
    {
        var issues = new List<ContentValidationIssue>();
        var duplicateKeys = definitions
            .GroupBy(definition => definition.PlacementId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        return Validate(
            definitions,
            RuntimeObjectKind.Npc,
            definition => definition.TemplateId,
            definition => definition.MapId,
            definition => definition.Enabled,
            definition => definition.Source,
            definition =>
            {
                var local = new List<ContentValidationIssue>();
                if (duplicateKeys.Contains(definition.PlacementId))
                {
                    local.Add(Issue("content.duplicate_placement_id", RuntimeObjectKind.Npc, definition.TemplateId, definition.MapId, "Duplicate NPC PlacementId.", definition.Source));
                }

                if (!mapIds.Contains(definition.MapId))
                {
                    local.Add(Issue("content.missing_map_reference", RuntimeObjectKind.Npc, definition.TemplateId, definition.MapId, "NPC references a map that is not present in the map catalog.", definition.Source));
                }

                if (definition.MerchantBindingId is not null && !merchantIds.Contains(definition.MerchantBindingId.Value))
                {
                    local.Add(Issue("content.invalid_merchant_binding", RuntimeObjectKind.Npc, definition.TemplateId, definition.MapId, "NPC references a merchant binding that is not present in merchant content.", definition.Source));
                }

                return local;
            },
            issues);
    }

    public ValidatedContentDefinitions<MonsterSpawnDefinition> ValidateMonsters(
        IEnumerable<MonsterSpawnDefinition> definitions,
        IReadOnlySet<int> mapIds)
    {
        return Validate(
            definitions,
            RuntimeObjectKind.Monster,
            definition => definition.TemplateId,
            definition => definition.MapId,
            definition => definition.Enabled,
            definition => definition.Evidence,
            definition =>
            {
                var local = new List<ContentValidationIssue>();
                if (!mapIds.Contains(definition.MapId))
                {
                    local.Add(Issue("content.missing_map_reference", RuntimeObjectKind.Monster, definition.TemplateId, definition.MapId, "Monster spawn references a map that is not present in the map catalog.", definition.Evidence));
                }

                if (definition.MaximumHp <= 0 || definition.Level < 0 || definition.AttackPower < 0 || definition.Defense < 0 ||
                    definition.RespawnTime <= TimeSpan.Zero || definition.SpawnRadius < 0 || definition.Count <= 0)
                {
                    local.Add(Issue("content.monster_spawn_policy_invalid", RuntimeObjectKind.Monster, definition.TemplateId, definition.MapId, "Monster spawn contains incomplete or invalid production policies.", definition.Evidence));
                }

                return local;
            },
            []);
    }

    public ValidatedContentDefinitions<PortalDefinition> ValidatePortals(
        IEnumerable<PortalDefinition> definitions,
        IReadOnlySet<int> mapIds)
    {
        return Validate(
            definitions,
            RuntimeObjectKind.Portal,
            definition => definition.TemplateId,
            definition => definition.Source.MapId,
            definition => definition.Enabled,
            definition => definition.Evidence,
            definition =>
            {
                var issues = new List<ContentValidationIssue>();
                if (!mapIds.Contains(definition.Source.MapId))
                {
                    issues.Add(Issue("content.missing_map_reference", RuntimeObjectKind.Portal, definition.TemplateId, definition.Source.MapId, "Portal source map is not present in the map catalog.", definition.Evidence));
                }

                if (!mapIds.Contains(definition.Target.MapId))
                {
                    issues.Add(Issue("content.missing_map_reference", RuntimeObjectKind.Portal, definition.TemplateId, definition.Target.MapId, "Portal target map is not present in the map catalog.", definition.Evidence));
                }

                return issues;
            },
            []);
    }

    public ValidatedContentDefinitions<MerchantMapping> ValidateMerchants(
        IEnumerable<MerchantMapping> definitions,
        IReadOnlySet<int> npcTemplateIds)
    {
        return Validate(
            definitions,
            RuntimeObjectKind.Merchant,
            definition => definition.MerchantTemplateId,
            _ => null,
            definition => definition.Enabled,
            definition => definition.Evidence,
            definition => npcTemplateIds.Contains(definition.NpcTemplateId)
                ? []
                : [Issue("content.invalid_merchant_binding", RuntimeObjectKind.Merchant, definition.MerchantTemplateId, null, "Merchant references an NPC template that is not present in validated NPC content.", definition.Evidence)],
            []);
    }

    private static ValidatedContentDefinitions<TDefinition> Validate<TDefinition>(
        IEnumerable<TDefinition> definitions,
        RuntimeObjectKind runtimeType,
        Func<TDefinition, int> templateId,
        Func<TDefinition, int?> mapId,
        Func<TDefinition, bool> enabled,
        Func<TDefinition, string> source,
        Func<TDefinition, IReadOnlyList<ContentValidationIssue>> validateOne,
        IReadOnlyList<ContentValidationIssue> seedIssues)
    {
        var valid = new List<TDefinition>();
        var quarantined = new List<TDefinition>();
        var issues = new List<ContentValidationIssue>(seedIssues);

        foreach (var definition in definitions)
        {
            if (!enabled(definition))
            {
                issues.Add(Issue("content.disabled", runtimeType, templateId(definition), mapId(definition), "Content is disabled and is not registered into runtime.", source(definition)));
                quarantined.Add(definition);
                continue;
            }

            var localIssues = validateOne(definition);
            if (localIssues.Count == 0)
            {
                valid.Add(definition);
            }
            else
            {
                issues.AddRange(localIssues);
                quarantined.Add(definition);
            }
        }

        return new ValidatedContentDefinitions<TDefinition>(
            new ReadOnlyCollection<TDefinition>(valid),
            new ReadOnlyCollection<TDefinition>(quarantined),
            new ReadOnlyCollection<ContentValidationIssue>(issues));
    }

    private static ContentValidationIssue Issue(
        string code,
        RuntimeObjectKind runtimeType,
        int templateId,
        int? mapId,
        string message,
        string source) =>
        new(code, runtimeType, templateId, mapId, ContentValidationStatus.Quarantined, message, source);
}
