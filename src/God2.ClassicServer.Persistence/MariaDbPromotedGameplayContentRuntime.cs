using System.Collections.ObjectModel;
using System.Data;
using System.Text.RegularExpressions;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Runtime;
using MySqlConnector;
using OpenccNetLib;

namespace God2.ClassicServer.Persistence;

public sealed record PromotedQuestProfile(
    string ProfileId,
    int QuestId,
    string NameZhTw,
    string? DescriptionZhTw,
    string StepsJson,
    string ObjectiveEvidenceStatus,
    string RewardEvidenceStatus,
    string EvidenceStatus);

public sealed record PromotedQuestObjective(
    string ObjectiveId,
    int QuestId,
    string ObjectiveType,
    int? ItemId,
    int? MonsterId,
    int? RequiredQuantity,
    string? ObjectiveTextZhTw,
    string RelationshipEvidenceStatus,
    string QuantityEvidenceStatus);

public sealed record PromotedEquipmentSet(
    int SetId,
    string NameZhTw,
    int RequiredPieces,
    string EffectsZhTw,
    string RelationshipEvidenceStatus,
    string BonusEvidenceStatus,
    bool BonusEnabled);

public sealed record PromotedEquipmentSetMember(
    int SetId,
    int ItemId,
    string SlotName,
    string EvidenceStatus);

public sealed record PromotedPetInnate(
    int InnateId,
    string NameZhTw,
    string? DescriptionZhTw,
    string? EffectReference,
    int? EffectValue,
    string EvidenceStatus,
    string EffectEvidenceStatus);

public sealed record PromotedGameplayContentSnapshot(
    string ReleaseId,
    string SourceRunId,
    string FormalCatalogFingerprint,
    long FormalCatalogRecordCount,
    IReadOnlyDictionary<int, PromotedQuestProfile> QuestProfiles,
    IReadOnlyDictionary<string, PromotedQuestObjective> QuestObjectives,
    IReadOnlyDictionary<int, PromotedEquipmentSet> EquipmentSets,
    IReadOnlyDictionary<string, PromotedEquipmentSetMember> EquipmentSetMembers,
    IReadOnlyDictionary<int, PromotedPetInnate> PetInnates,
    IReadOnlyDictionary<string, long> ProductionCoverage,
    IReadOnlyList<string> EvidenceBlockedFamilies)
{
    public static PromotedGameplayContentSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        new ReadOnlyDictionary<int, PromotedQuestProfile>(new Dictionary<int, PromotedQuestProfile>()),
        new ReadOnlyDictionary<string, PromotedQuestObjective>(new Dictionary<string, PromotedQuestObjective>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<int, PromotedEquipmentSet>(new Dictionary<int, PromotedEquipmentSet>()),
        new ReadOnlyDictionary<string, PromotedEquipmentSetMember>(new Dictionary<string, PromotedEquipmentSetMember>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<int, PromotedPetInnate>(new Dictionary<int, PromotedPetInnate>()),
        new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(StringComparer.Ordinal)),
        Array.AsReadOnly(Array.Empty<string>()));
}

public sealed class MariaDbPromotedGameplayContentRuntime :
    MariaDbRuntimeRepository,
    IRuntimeCacheBuilder,
    IProductionGameplayContentAuthority
{
    private static readonly IReadOnlyDictionary<string, string> CoverageQueries =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Maps"] = "SELECT COUNT(*) FROM `maps`;",
            ["ClientMappedMaps"] = "SELECT COUNT(DISTINCT `MapId`) FROM `client_map_identities` WHERE `ProductionEnabled`=1 AND `CoordinateScaleX`>0 AND `CoordinateScaleY`>0 AND CHAR_LENGTH(`ResourceIdentity`)>0;",
            ["NpcTemplates"] = "SELECT COUNT(*) FROM `npcs`;",
            ["NpcProductionSpawns"] = "SELECT COUNT(DISTINCT `NpcId`) FROM `npc_spawns` WHERE `ProductionEnabled`=1;",
            ["MonsterTemplates"] = "SELECT COUNT(*) FROM `monsters`;",
            ["MonsterProductionSpawns"] = "SELECT COUNT(DISTINCT `monster_id`) FROM `god2_game`.`monster_spawns` WHERE `enabled`=1;",
            ["MonsterExecutionProfiles"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `ProductionEnabled`=1 AND `Level` IS NOT NULL AND `MaxHp`>0 AND `HpPolicy`<>'EvidenceBlocked' AND `MaxMp` IS NOT NULL AND `MpPolicy`<>'EvidenceBlocked' AND `PhysicalAttack` IS NOT NULL AND `PhysicalDefense` IS NOT NULL;",
            ["Items"] = "SELECT COUNT(*) FROM `items`;",
            ["Skills"] = "SELECT COUNT(*) FROM `skills`;",
            ["SkillExecutionProfiles"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ProductionEnabled`=1;",
            ["MonsterDropPolicyResolved"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `DropPolicyStatus` IN ('Verified','Derived','ExplicitNoItemDrop','NotApplicable');",
            ["DropRelationshipCandidates"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `PromotionRunId`=@run;",
            ["ResolvedDropRelationships"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `PromotionRunId`=@run AND `DropRelationshipStatus` IN ('Verified','Derived') AND `EffectiveDropChance`>=0 AND (`MinimumQuantity` IS NULL OR `MinimumQuantity`>0) AND (`MaximumQuantity` IS NULL OR `MaximumQuantity`>=COALESCE(`MinimumQuantity`,1));",
            ["EnabledDropRelationships"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `PromotionRunId`=@run AND `ProductionDropEnabled`=1;",
            ["Merchants"] = "SELECT COUNT(*) FROM `merchants`;",
            ["MerchantInventoryCandidates"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `PromotionRunId`=@run;",
            ["ResolvedMerchantInventory"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `PromotionRunId`=@run AND `RelationshipEvidenceStatus` IN ('Verified','Derived') AND `PriceEvidenceStatus` IN ('Verified','Derived','NotApplicable') AND (`ProductionSaleEnabled`=1 OR `Enabled`=0);",
            ["MerchantInventoryCovered"] = "SELECT COUNT(DISTINCT `MerchantId`) FROM `god2_research`.`merchant_inventory_candidates` WHERE `PromotionRunId`=@run AND `RelationshipEvidenceStatus` IN ('Verified','Derived');",
            ["EnabledMerchantInventory"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `PromotionRunId`=@run AND `ProductionSaleEnabled`=1;",
            ["Quests"] = "SELECT COUNT(*) FROM `quests`;",
            ["PromotedQuestProfiles"] = "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `PromotionRunId`=@run AND `ProductionProfileEnabled`=1;",
            ["QuestProfilesWithResolvedObjectives"] = "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `PromotionRunId`=@run AND `ProductionProfileEnabled`=1 AND JSON_VALID(`StepsJson`);",
            ["QuestObjectiveCandidates"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `PromotionRunId`=@run;",
            ["ResolvedQuestObjectives"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `PromotionRunId`=@run AND `ProductionObjectiveEnabled`=1 AND `RelationshipEvidenceStatus` IN ('Verified','Derived') AND `QuantityEvidenceStatus` IN ('Verified','Derived');",
            ["EnabledQuestObjectives"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `PromotionRunId`=@run AND `ProductionObjectiveEnabled`=1;",
            ["PromotedEquipmentSets"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1;",
            ["PromotedEquipmentMembers"] = "SELECT COUNT(*) FROM `equipment_set_members` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1;",
            ["ResolvedEquipmentBonuses"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1 AND (`ProductionBonusEnabled`=1 OR CHAR_LENGTH(`EffectsZhTw`)>0);",
            ["EnabledEquipmentBonuses"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `PromotionRunId`=@run AND `ProductionBonusEnabled`=1;",
            ["CombineCandidates"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `PromotionRunId`=@run;",
            ["ResolvedCombineRecipes"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `PromotionRunId`=@run AND `RelationshipStatus` IN ('Verified','Derived') AND `Quantity`>0 AND `EffectiveProbability`>=0;",
            ["EnabledCombineRecipes"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `PromotionRunId`=@run AND `ProductionRecipeEnabled`=1;",
            ["PromotedPetInnates"] = "SELECT COUNT(*) FROM `god2_game`.`pet_innate_definitions` WHERE `enabled`=1;",
            ["PetEggRelationshipCandidates"] = "SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `PromotionRunId`=@run;",
            ["ResolvedPetEggRelationships"] = "SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `PromotionRunId`=@run AND `RelationshipStatus` IN ('Verified','Derived') AND `EffectiveProbability`>=0;",
            ["EnabledPetEggHatches"] = "SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `PromotionRunId`=@run AND `ProductionHatchEnabled`=1;"
        });

    private PromotedGameplayContentSnapshot _publishedSnapshot = PromotedGameplayContentSnapshot.Empty;
    private readonly MariaDbStaticDataLoader _formalCatalog;

    public MariaDbPromotedGameplayContentRuntime(DatabaseOptions options)
        : this(options, new MariaDbStaticDataLoader(options))
    {
    }

    public MariaDbPromotedGameplayContentRuntime(
        DatabaseOptions options,
        MariaDbStaticDataLoader formalCatalog)
        : base(options)
    {
        _formalCatalog = formalCatalog ?? throw new ArgumentNullException(nameof(formalCatalog));
    }

    public PromotedGameplayContentSnapshot PublishedSnapshot => Volatile.Read(ref _publishedSnapshot);

    public string ActiveReleaseId => PublishedSnapshot.ReleaseId;

    public bool IsReady => !string.IsNullOrWhiteSpace(PublishedSnapshot.ReleaseId);

    public OperationResult RequireReady() => IsReady
        ? OperationResult.Success
        : Fail("content_release.runtime_not_ready", "The production gameplay content authority is not ready.");

    public OperationResult<ProductionQuestContent> ResolveQuest(int questId)
    {
        var ready = RequireReady();
        if (!ready.Succeeded)
        {
            return OperationResult<ProductionQuestContent>.Failure(ready.Error.Code, ready.Error.Message, ready.Error.Source);
        }

        var snapshot = PublishedSnapshot;
        return snapshot.QuestProfiles.TryGetValue(questId, out var profile)
            ? OperationResult<ProductionQuestContent>.Success(new ProductionQuestContent(
                profile.QuestId,
                profile.NameZhTw,
                profile.DescriptionZhTw,
                profile.ObjectiveEvidenceStatus,
                profile.RewardEvidenceStatus,
                snapshot.ReleaseId))
            : Missing<ProductionQuestContent>("quest", questId);
    }

    public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId)
    {
        var ready = RequireReady();
        if (!ready.Succeeded)
        {
            return OperationResult<ProductionEquipmentSetContent>.Failure(ready.Error.Code, ready.Error.Message, ready.Error.Source);
        }

        var snapshot = PublishedSnapshot;
        if (!snapshot.EquipmentSets.TryGetValue(setId, out var definition))
        {
            return Missing<ProductionEquipmentSetContent>("equipment_set", setId);
        }

        var members = snapshot.EquipmentSetMembers.Values
            .Where(member => member.SetId == setId)
            .OrderBy(member => member.ItemId)
            .Select(member => new ProductionEquipmentSetMemberContent(member.ItemId, member.SlotName, member.EvidenceStatus))
            .ToArray();
        return OperationResult<ProductionEquipmentSetContent>.Success(new ProductionEquipmentSetContent(
            definition.SetId,
            definition.NameZhTw,
            definition.RequiredPieces,
            definition.EffectsZhTw,
            definition.BonusEnabled,
            Array.AsReadOnly(members),
            snapshot.ReleaseId));
    }

    public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId)
    {
        var ready = RequireReady();
        if (!ready.Succeeded)
        {
            return OperationResult<ProductionPetInnateContent>.Failure(ready.Error.Code, ready.Error.Message, ready.Error.Source);
        }

        var snapshot = PublishedSnapshot;
        return snapshot.PetInnates.TryGetValue(innateId, out var innate)
            ? OperationResult<ProductionPetInnateContent>.Success(new ProductionPetInnateContent(
                innate.InnateId,
                innate.NameZhTw,
                innate.DescriptionZhTw,
                innate.EffectReference,
                innate.EffectValue,
                innate.EffectEvidenceStatus,
                snapshot.ReleaseId))
            : Missing<ProductionPetInnateContent>("pet_innate", innateId);
    }

    public async Task<OperationResult> BuildAsync(
        IReadOnlyList<StaticDataLoadCount> staticData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staticData);
        try
        {
            var formalManifest = FormalRuntimeCatalogManifestBuilder.Build(_formalCatalog.PublishedSnapshot);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var release = await LoadActiveReleaseAsync(connection, cancellationToken);
            if (release is null)
            {
                return FailAndClear("content_release.active_missing", "No active completed gameplay content release exists.");
            }

            release = await PinOrReloadFormalCatalogManifestAsync(
                connection,
                release,
                formalManifest,
                cancellationToken);
            if (!string.Equals(release.FormalCatalogManifestVersion, formalManifest.Version, StringComparison.Ordinal) ||
                !string.Equals(release.FormalCatalogFingerprint, formalManifest.Sha256, StringComparison.OrdinalIgnoreCase) ||
                release.FormalCatalogRecordCount != formalManifest.RecordCount)
            {
                return FailAndClear(
                    "content_release.formal_catalog_changed",
                    "The formal MariaDB runtime catalog no longer matches the active content release manifest.");
            }

            var issues = await CountIntegrityIssuesAsync(connection, release.SourceRunId, cancellationToken);
            if (issues.Values.Any(value => value != 0))
            {
                return FailAndClear(
                    "content_release.integrity_failed",
                    $"The active gameplay content release failed closed ({string.Join(',', issues.Where(entry => entry.Value != 0).Select(entry => entry.Key))}).");
            }

            var quests = await LoadQuestProfilesAsync(connection, release.SourceRunId, cancellationToken);
            var objectives = await LoadQuestObjectivesAsync(connection, release.SourceRunId, cancellationToken);
            var equipmentSets = await LoadEquipmentSetsAsync(connection, release.SourceRunId, cancellationToken);
            var equipmentMembers = await LoadEquipmentMembersAsync(connection, release.SourceRunId, cancellationToken);
            var petInnates = await LoadPetInnatesAsync(connection, release.SourceRunId, cancellationToken);
            var coverage = await LoadCoverageAsync(connection, release.SourceRunId, cancellationToken);

            RequireDistinct(quests, entry => entry.QuestId, "quest profile");
            RequireDistinct(objectives, entry => entry.ObjectiveId, "quest objective");
            RequireDistinct(equipmentSets, entry => entry.SetId, "equipment set");
            RequireDistinct(equipmentMembers, entry => $"{entry.SetId}:{entry.ItemId}", "equipment set member");
            RequireDistinct(petInnates, entry => entry.InnateId, "pet innate");
            ValidateDisplayText(quests.SelectMany(entry => new[] { entry.NameZhTw, entry.DescriptionZhTw }));
            ValidateDisplayText(objectives.Select(entry => entry.ObjectiveTextZhTw));
            ValidateDisplayText(equipmentSets.SelectMany(entry => new[] { entry.NameZhTw, entry.EffectsZhTw }));
            ValidateDisplayText(petInnates.SelectMany(entry => new[] { entry.NameZhTw, entry.DescriptionZhTw }));

            var snapshot = new PromotedGameplayContentSnapshot(
                release.ReleaseId,
                release.SourceRunId,
                formalManifest.Sha256,
                formalManifest.RecordCount,
                new ReadOnlyDictionary<int, PromotedQuestProfile>(quests.ToDictionary(entry => entry.QuestId)),
                new ReadOnlyDictionary<string, PromotedQuestObjective>(objectives.ToDictionary(entry => entry.ObjectiveId, StringComparer.Ordinal)),
                new ReadOnlyDictionary<int, PromotedEquipmentSet>(equipmentSets.ToDictionary(entry => entry.SetId)),
                new ReadOnlyDictionary<string, PromotedEquipmentSetMember>(equipmentMembers.ToDictionary(entry => $"{entry.SetId}:{entry.ItemId}", StringComparer.Ordinal)),
                new ReadOnlyDictionary<int, PromotedPetInnate>(petInnates.ToDictionary(entry => entry.InnateId)),
                new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(coverage, StringComparer.Ordinal)),
                Array.AsReadOnly(FindEvidenceBlockedFamilies(coverage)));

            Volatile.Write(ref _publishedSnapshot, snapshot);
            return OperationResult.Success;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or InvalidCastException or FormatException or OverflowException or TimeoutException)
        {
            Volatile.Write(ref _publishedSnapshot, PromotedGameplayContentSnapshot.Empty);
            return Fail(
                "content_release.load_failed",
                $"The active gameplay content release could not be loaded ({exception.GetType().Name}).");
        }
    }

    private static async Task<ActiveContentRelease?> LoadActiveReleaseAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT release_row.`catalog_release_id`,
                   (
                       SELECT recovery.`RunId`
                       FROM `content_recovery_runs` recovery
                       WHERE recovery.`Phase`='GameplayContentRecoveryPhase3'
                         AND recovery.`Status`='COMPLETED'
                       ORDER BY recovery.`CompletedAtUtc` DESC, recovery.`StartedAtUtc` DESC
                       LIMIT 1
                   ) AS `SourceRunId`,
                   @manifestVersion,
                   release_row.`catalog_fingerprint`,
                   release_row.`catalog_record_count`
            FROM `god2_game_meta`.`runtime_catalog_releases` release_row
            WHERE release_row.`status`='Active'
              AND release_row.`active_slot`=1
              AND release_row.`catalog_fingerprint` IS NOT NULL
              AND release_row.`catalog_record_count` IS NOT NULL
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("@manifestVersion", FormalRuntimeCatalogManifestBuilder.Version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (reader.IsDBNull(1))
        {
            throw new InvalidOperationException("No completed Phase3 gameplay content recovery run exists for the active formal runtime catalog.");
        }

        var result = new ActiveContentRelease(
            ReadDatabaseIdentity(reader, 0),
            ReadDatabaseIdentity(reader, 1),
            NullableString(reader, 2),
            NullableString(reader, 3),
            reader.IsDBNull(4)
                ? null
                : Convert.ToInt64(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Multiple active formal runtime catalog releases are not allowed.");
        }

        return result;
    }

    private static async Task<ActiveContentRelease> PinOrReloadFormalCatalogManifestAsync(
        MySqlConnection connection,
        ActiveContentRelease release,
        FormalRuntimeCatalogManifest manifest,
        CancellationToken cancellationToken)
    {
        if (release.FormalCatalogManifestVersion is not null ||
            release.FormalCatalogFingerprint is not null ||
            release.FormalCatalogRecordCount is not null)
        {
            return release;
        }

        throw new InvalidOperationException("The active formal runtime catalog release is missing its manifest fingerprint or record count.");
    }

    private static async Task<IReadOnlyDictionary<string, long>> CountIntegrityIssuesAsync(
        MySqlConnection connection,
        string runId,
        CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StalePromotionRows"] = """
                SELECT
                    (SELECT COUNT(*) FROM `quest_content_profiles` WHERE `ProductionProfileEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `ProductionObjectiveEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `ProductionRelationshipEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `equipment_set_members` WHERE `ProductionRelationshipEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    0 +
                    (SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `ProductionSaleEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `ProductionDropEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProductionRecipeEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run)) +
                    (SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `ProductionHatchEnabled`=1 AND (`PromotionRunId` IS NULL OR `PromotionRunId`<>@run));
                """,
            ["BrokenQuestProfiles"] = """
                SELECT COUNT(*) FROM `quest_content_profiles` profile
                LEFT JOIN `quests` quest ON quest.`Id`=profile.`QuestId`
                LEFT JOIN `god2_research`.`content_profile_source_archive` source
                    ON source.`FormalTable`='quest_content_profiles' AND source.`RecordIdentity`=profile.`ProfileId`
                WHERE profile.`PromotionRunId`=@run AND profile.`ProductionProfileEnabled`=1
                  AND (quest.`Id` IS NULL OR profile.`NameZhTw`='' OR NOT JSON_VALID(profile.`StepsJson`)
                       OR COALESCE(source.`SourceHash`,'') NOT REGEXP '^[0-9A-Fa-f]{64}$');
                """,
            ["BrokenQuestObjectives"] = """
                SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` objective
                LEFT JOIN `quests` quest ON quest.`Id`=objective.`QuestId`
                LEFT JOIN `items` item ON item.`Id`=objective.`ItemId`
                LEFT JOIN `monsters` monster ON monster.`Id`=objective.`MonsterId`
                WHERE objective.`PromotionRunId`=@run AND objective.`ProductionObjectiveEnabled`=1
                  AND (quest.`Id` IS NULL OR (objective.`ItemId` IS NOT NULL AND item.`Id` IS NULL)
                       OR (objective.`MonsterId` IS NOT NULL AND monster.`Id` IS NULL)
                       OR objective.`EvidenceStatus` NOT IN ('Verified','Derived')
                       OR objective.`RelationshipEvidenceStatus` NOT IN ('Verified','Derived')
                       OR objective.`QuantityEvidenceStatus` NOT IN ('Verified','Derived')
                       OR objective.`RequiredQuantity` IS NULL OR objective.`RequiredQuantity`<=0
                       OR ((objective.`ItemId` IS NULL)=(objective.`MonsterId` IS NULL))
                       OR objective.`ObjectiveType` IN ('Unknown','Candidate'));
                """,
            ["InvalidEquipmentDefinitions"] = """
                SELECT COUNT(*) FROM `equipment_set_definitions` definition
                LEFT JOIN `god2_research`.`content_profile_source_archive` source
                    ON source.`FormalTable`='equipment_set_definitions' AND source.`RecordIdentity`=CAST(definition.`SetId` AS char)
                WHERE definition.`PromotionRunId`=@run AND definition.`ProductionRelationshipEnabled`=1
                  AND (definition.`SetRelationshipStatus` NOT IN ('Verified','Derived') OR definition.`NameZhTw`=''
                       OR COALESCE(source.`SourceHash`,'') NOT REGEXP '^[0-9A-Fa-f]{64}$');
                """,
            ["BrokenEquipmentRelationships"] = """
                SELECT COUNT(*) FROM `equipment_set_members` member
                LEFT JOIN `equipment_set_definitions` definition ON definition.`SetId`=member.`SetId`
                LEFT JOIN `items` item ON item.`Id`=member.`ItemId`
                WHERE member.`PromotionRunId`=@run AND member.`ProductionRelationshipEnabled`=1
                  AND (definition.`SetId` IS NULL OR definition.`ProductionRelationshipEnabled`<>1 OR item.`Id` IS NULL
                       OR member.`SlotName`='');
                """,
            ["InvalidEquipmentThresholds"] = """
                SELECT COUNT(*) FROM `equipment_set_definitions` definition
                LEFT JOIN (
                    SELECT `SetId`, COUNT(*) member_count FROM `equipment_set_members`
                    WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1 GROUP BY `SetId`
                ) members ON members.`SetId`=definition.`SetId`
                WHERE definition.`PromotionRunId`=@run AND definition.`ProductionRelationshipEnabled`=1
                  AND (definition.`RequiredPieces`<1 OR definition.`RequiredPieces`>5 OR COALESCE(members.member_count,0)<definition.`RequiredPieces`);
                """,
            ["InvalidPetInnates"] = """
                SELECT COUNT(*) FROM `god2_game`.`pet_innate_definitions`
                WHERE `enabled`=1
                  AND (`name_zh_tw`=''
                       OR `effect_evidence_status`='EvidenceBlocked');
                """,
            ["DefaultDisabledZeroViolations"] = """
                SELECT
                    (SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `ProductionDropEnabled`=0 AND `DeclaredDropChance` IS NULL AND `EffectiveDropChance`<>0) +
                    (SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProductionRecipeEnabled`=0 AND `DeclaredProbability` IS NULL AND `EffectiveProbability`<>0) +
                    (SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `ProductionHatchEnabled`=0 AND `DeclaredProbability` IS NULL AND `EffectiveProbability`<>0);
                """
        };

        var results = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = query.Value;
            command.Parameters.AddWithValue("@run", runId);
            results[query.Key] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ReadOnlyDictionary<string, long>(results);
    }

    private static async Task<List<PromotedQuestProfile>> LoadQuestProfilesAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new List<PromotedQuestProfile>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ProfileId`,`QuestId`,`NameZhTw`,`DescriptionZhTw`,`StepsJson`,
                   'formal-runtime-quest-objectives','formal-runtime-quest-rewards','formal-runtime-quest-profile'
            FROM `quest_content_profiles`
            WHERE `PromotionRunId`=@run AND `ProductionProfileEnabled`=1
            ORDER BY `QuestId`,`ProfileId`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedQuestProfile(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2), NullableString(reader, 3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }

        return result;
    }

    private static async Task<List<PromotedQuestObjective>> LoadQuestObjectivesAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new List<PromotedQuestObjective>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ObjectiveId`,`QuestId`,`ObjectiveType`,`ItemId`,`MonsterId`,`RequiredQuantity`,`ObjectiveTextZhTw`,
                   `RelationshipEvidenceStatus`,`QuantityEvidenceStatus`
            FROM `god2_research`.`quest_objective_candidates`
            WHERE `PromotionRunId`=@run AND `ProductionObjectiveEnabled`=1
            ORDER BY `QuestId`,`ObjectiveId`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedQuestObjective(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2), NullableInt(reader, 3), NullableInt(reader, 4),
                NullableInt(reader, 5), NullableString(reader, 6), reader.GetString(7), reader.GetString(8)));
        }

        return result;
    }

    private static async Task<List<PromotedEquipmentSet>> LoadEquipmentSetsAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new List<PromotedEquipmentSet>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `SetId`,`NameZhTw`,`RequiredPieces`,`EffectsZhTw`,`SetRelationshipStatus`,
                   CASE WHEN `ProductionBonusEnabled`=1 THEN 'formal-runtime-enabled' ELSE 'NotApplicable' END,
                   `ProductionBonusEnabled`
            FROM `equipment_set_definitions`
            WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1
            ORDER BY `SetId`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedEquipmentSet(
                reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetBoolean(6)));
        }

        return result;
    }

    private static async Task<List<PromotedEquipmentSetMember>> LoadEquipmentMembersAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new List<PromotedEquipmentSetMember>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `SetId`,`ItemId`,`SlotName`,'formal-runtime-enabled'
            FROM `equipment_set_members`
            WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1
            ORDER BY `SetId`,`ItemId`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedEquipmentSetMember(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        return result;
    }

    private static async Task<List<PromotedPetInnate>> LoadPetInnatesAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new List<PromotedPetInnate>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `innate_id`,`name_zh_tw`,`description_zh_tw`,`effect_reference`,`effect_value`,'formal-runtime-enabled',
                   `effect_evidence_status`
            FROM `god2_game`.`pet_innate_definitions`
            WHERE `enabled`=1
            ORDER BY `innate_id`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedPetInnate(
                reader.GetInt32(0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3), NullableInt(reader, 4),
                reader.GetString(5), reader.GetString(6)));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, long>> LoadCoverageAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in CoverageQueries)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = query.Value;
            if (query.Value.Contains("@run", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@run", runId);
            }

            result[query.Key] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static void RequireDistinct<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, string label)
        where TKey : notnull
    {
        if (source.GroupBy(keySelector).Any(group => group.Skip(1).Any()))
        {
            throw new InvalidOperationException($"The active release contains duplicate {label} identities.");
        }
    }

    internal static void ValidateDisplayText(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values.Where(value => value is not null).Select(value => value!))
        {
            ProductionDisplayTextValidator.Validate(value);
        }
    }

    internal static string[] FindEvidenceBlockedFamilies(IReadOnlyDictionary<string, long> coverage)
    {
        var blocked = new List<string>();
        void RequireFull(string family, string promoted, string total)
        {
            if (coverage.GetValueOrDefault(promoted) != coverage.GetValueOrDefault(total))
            {
                blocked.Add(family);
            }
        }

        RequireFull("Map", "ClientMappedMaps", "Maps");
        RequireFull("NpcSpawn", "NpcProductionSpawns", "NpcTemplates");
        RequireFull("MonsterSpawn", "MonsterProductionSpawns", "MonsterTemplates");
        RequireFull("MonsterExecution", "MonsterExecutionProfiles", "MonsterTemplates");
        RequireFull("SkillExecution", "SkillExecutionProfiles", "Skills");
        RequireFull("QuestProfile", "PromotedQuestProfiles", "Quests");
        RequireFull("DropPolicy", "MonsterDropPolicyResolved", "MonsterTemplates");
        RequireAllNonEmpty("Drop", "ResolvedDropRelationships", "DropRelationshipCandidates");
        RequireAllNonEmpty("MerchantInventory", "ResolvedMerchantInventory", "MerchantInventoryCandidates");
        RequireFull("MerchantCoverage", "MerchantInventoryCovered", "Merchants");
        RequireFull("QuestObjective", "QuestProfilesWithResolvedObjectives", "PromotedQuestProfiles");
        RequireAllWhenPresent("QuestObjectiveCandidates", "ResolvedQuestObjectives", "QuestObjectiveCandidates");
        RequireFull("EquipmentBonus", "ResolvedEquipmentBonuses", "PromotedEquipmentSets");
        RequireAllNonEmpty("Combine", "ResolvedCombineRecipes", "CombineCandidates");
        RequireAllNonEmpty("PetEggHatch", "ResolvedPetEggRelationships", "PetEggRelationshipCandidates");
        return blocked.ToArray();

        void RequireAllNonEmpty(string family, string resolved, string total)
        {
            if (coverage.GetValueOrDefault(total) == 0 ||
                coverage.GetValueOrDefault(resolved) != coverage.GetValueOrDefault(total))
            {
                blocked.Add(family);
            }
        }

        void RequireAllWhenPresent(string family, string resolved, string total)
        {
            if (coverage.GetValueOrDefault(total) > 0 &&
                coverage.GetValueOrDefault(resolved) != coverage.GetValueOrDefault(total))
            {
                blocked.Add(family);
            }
        }
    }

    private static string? NullableString(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? NullableInt(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    internal static string ReadDatabaseIdentity(IDataRecord reader, int ordinal) => reader.GetValue(ordinal) switch
    {
        Guid value => value.ToString("D"),
        string value when !string.IsNullOrWhiteSpace(value) => value,
        _ => throw new InvalidCastException("Content release identity must be a UUID string or Guid.")
    };

    private static OperationResult Fail(string code, string message) =>
        OperationResult.Failure(code, message, nameof(MariaDbPromotedGameplayContentRuntime));

    private static OperationResult<T> Missing<T>(string family, int identity) =>
        OperationResult<T>.Failure(
            $"content_release.{family}_missing",
            "The requested gameplay content identity is not promoted in the active release.",
            identity.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private OperationResult FailAndClear(string code, string message)
    {
        Volatile.Write(ref _publishedSnapshot, PromotedGameplayContentSnapshot.Empty);
        return Fail(code, message);
    }

    private sealed record ActiveContentRelease(
        string ReleaseId,
        string SourceRunId,
        string? FormalCatalogManifestVersion,
        string? FormalCatalogFingerprint,
        long? FormalCatalogRecordCount);

    private static class ProductionDisplayTextValidator
    {
        private static readonly Opencc ToTaiwan = new(OpenccConfig.S2Twp);
        private static readonly Regex ProtectedTokenPattern = new(
            @"\{[^{}\r\n]+\}|%(?:\d+\$)?[a-zA-Z]|\\r\\n|\\[nr]|\[(?:item|skill|resource):[^\]\r\n]+\]|<[^>\r\n]+>|[A-Za-z][A-Za-z0-9]*(?:[_./\\-][A-Za-z0-9]+)+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MarkupPattern = new(
            @"<[^>\r\n]+>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void Validate(string value)
        {
            if (value.Contains('\uFFFD', StringComparison.Ordinal) || !IsValidUtf16(value) ||
                value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            {
                throw new InvalidOperationException("The active release contains invalid Unicode or control characters in display text.");
            }

            var protectedText = ProtectedTokenPattern.Replace(value, static match => new string('_', match.Length));
            if (!string.Equals(ToTaiwan.Convert(protectedText, punctuation: false), protectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active release contains convertible Simplified Chinese in display text.");
            }

            var withoutTokens = ProtectedTokenPattern.Replace(value, string.Empty);
            if (withoutTokens.Contains('{', StringComparison.Ordinal) || withoutTokens.Contains('}', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active release contains a malformed placeholder in display text.");
            }

            var withoutMarkup = MarkupPattern.Replace(value, string.Empty);
            if (withoutMarkup.Contains('<', StringComparison.Ordinal) || withoutMarkup.Contains('>', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active release contains malformed markup in display text.");
            }
        }

        private static bool IsValidUtf16(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }

                    index++;
                }
                else if (char.IsLowSurrogate(value[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}



