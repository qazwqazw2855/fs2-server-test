using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

/// <summary>
/// Production gameplay authority backed only by god2_game and god2_game_meta.
/// Evidence/ETL tables are deliberately outside every normal-startup query.
/// </summary>
public sealed class MariaDbCanonicalGameplayContentRuntime :
    MariaDbRuntimeRepository,
    IRuntimeCacheBuilder,
    IProductionGameplayContentAuthority
{
    private PromotedGameplayContentSnapshot _publishedSnapshot = PromotedGameplayContentSnapshot.Empty;

    public MariaDbCanonicalGameplayContentRuntime(DatabaseOptions options)
        : base(options)
    {
    }

    public PromotedGameplayContentSnapshot PublishedSnapshot => Volatile.Read(ref _publishedSnapshot);

    public string ActiveReleaseId => PublishedSnapshot.ReleaseId;

    public bool IsReady => !string.IsNullOrWhiteSpace(ActiveReleaseId);

    public OperationResult RequireReady() => IsReady
        ? OperationResult.Success
        : OperationResult.Failure("canonical_catalog.runtime_not_ready", "The canonical gameplay catalog is not ready.", nameof(MariaDbCanonicalGameplayContentRuntime));

    public OperationResult<ProductionQuestContent> ResolveQuest(int questId)
    {
        var snapshot = PublishedSnapshot;
        if (!IsReady)
        {
            return OperationResult<ProductionQuestContent>.Failure(
                "canonical_catalog.runtime_not_ready",
                "The canonical gameplay catalog is not ready.",
                nameof(MariaDbCanonicalGameplayContentRuntime));
        }

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
        var snapshot = PublishedSnapshot;
        if (!snapshot.EquipmentSets.TryGetValue(setId, out var definition))
        {
            return Missing<ProductionEquipmentSetContent>("equipment_set", setId);
        }

        var members = snapshot.EquipmentSetMembers.Values
            .Where(value => value.SetId == setId)
            .OrderBy(value => value.ItemId)
            .Select(value => new ProductionEquipmentSetMemberContent(value.ItemId, value.SlotName, value.EvidenceStatus))
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

    public async Task<OperationResult> BuildAsync(IReadOnlyList<StaticDataLoadCount> staticData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staticData);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var release = await LoadReleaseAsync(connection, cancellationToken);
            if (release is null)
            {
                return FailAndClear("canonical_catalog.active_missing", "No validated Active canonical catalog exists. Run Reload-God2GameplayCatalog.ps1.");
            }

            var quests = await LoadQuestsAsync(connection, release.Value.Fingerprint, cancellationToken);
            var objectives = await LoadObjectivesAsync(connection, cancellationToken);
            var equipmentSets = await LoadEquipmentSetsAsync(connection, release.Value.Fingerprint, cancellationToken);
            var equipmentMembers = await LoadEquipmentMembersAsync(connection, cancellationToken);
            var petInnates = await LoadPetInnatesAsync(connection, release.Value.Fingerprint, cancellationToken);
            var coverage = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in staticData)
            {
                coverage[entry.DataType] = entry.Count;
            }

            var snapshot = new PromotedGameplayContentSnapshot(
                release.Value.ReleaseId,
                "canonical",
                release.Value.Fingerprint,
                release.Value.RecordCount,
                new ReadOnlyDictionary<int, PromotedQuestProfile>(quests.ToDictionary(value => value.QuestId)),
                new ReadOnlyDictionary<string, PromotedQuestObjective>(objectives.ToDictionary(value => value.ObjectiveId, StringComparer.Ordinal)),
                new ReadOnlyDictionary<int, PromotedEquipmentSet>(equipmentSets.ToDictionary(value => value.SetId)),
                new ReadOnlyDictionary<string, PromotedEquipmentSetMember>(equipmentMembers.ToDictionary(value => $"{value.SetId}:{value.ItemId}", StringComparer.Ordinal)),
                new ReadOnlyDictionary<int, PromotedPetInnate>(petInnates.ToDictionary(value => value.InnateId)),
                new ReadOnlyDictionary<string, long>(coverage),
                Array.AsReadOnly(Array.Empty<string>()));
            Volatile.Write(ref _publishedSnapshot, snapshot);
            return OperationResult.Success;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or InvalidCastException or OverflowException or TimeoutException)
        {
            return OperationResult.Failure(
                "canonical_catalog.load_failed",
                $"Canonical gameplay catalog loading failed ({exception.GetType().Name}).",
                exception.Message);
        }
    }

    private static async Task<(string ReleaseId, string Fingerprint, long RecordCount)?> LoadReleaseAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `catalog_release_id`,`catalog_fingerprint`,`catalog_record_count`
            FROM `god2_game_meta`.`runtime_catalog_releases`
            WHERE `status`='Active' LIMIT 2;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var result = (
            Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!,
            reader.GetString(1),
            reader.GetInt64(2));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Multiple Active canonical catalogs are not allowed.");
        }
        return result;
    }

    private static async Task<List<PromotedQuestProfile>> LoadQuestsAsync(
        MySqlConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var result = new List<PromotedQuestProfile>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT quest_row.`quest_id`,quest_row.`name_zh_tw`,quest_row.`description_zh_tw`,'Verified',
                   EXISTS(SELECT 1 FROM `god2_game`.`quest_objectives` objective_row WHERE objective_row.`quest_id`=quest_row.`quest_id` AND objective_row.`enabled`=1),
                   EXISTS(SELECT 1 FROM `god2_game`.`quest_rewards` reward_row WHERE reward_row.`quest_id`=quest_row.`quest_id` AND reward_row.`enabled`=1)
            FROM `god2_game`.`quests` quest_row WHERE quest_row.`enabled`=1 ORDER BY quest_row.`quest_id`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var questId = reader.GetInt32(0);
            result.Add(new PromotedQuestProfile(
                $"canonical-quest-{questId}", questId, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                string.Empty, reader.GetBoolean(4) ? "Verified" : "NotApplicable", reader.GetBoolean(5) ? "Verified" : "NotApplicable",
                reader.GetString(3)));
        }
        return result;
    }

    private static async Task<List<PromotedQuestObjective>> LoadObjectivesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<PromotedQuestObjective>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `objective_id`,`quest_id`,`objective_type`,`target_id`,`required_quantity`,`description_zh_tw`,'Verified'
            FROM `god2_game`.`quest_objectives` WHERE `enabled`=1 ORDER BY `quest_id`,`objective_order`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var target = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var type = NormalizeQuestObjectiveType(reader.GetString(2));
            result.Add(new PromotedQuestObjective(
                $"canonical-objective-{reader.GetInt64(0)}", reader.GetInt32(1), type,
                type.Contains("Item", StringComparison.OrdinalIgnoreCase) ? target : null,
                type.Contains("Monster", StringComparison.OrdinalIgnoreCase) ? target : null,
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(6)));
        }
        return result;
    }

    private static string NormalizeQuestObjectiveType(string value) => value switch
    {
        "候選物品目標" => "ItemCandidate",
        "未確認" => "Unknown",
        "候選目標" => "Candidate",
        _ => value
    };

    private static async Task<List<PromotedEquipmentSet>> LoadEquipmentSetsAsync(
        MySqlConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var result = new List<PromotedEquipmentSet>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_row.`set_id`,set_row.`name_zh_tw`,set_row.`description_zh_tw`,COUNT(member_row.`item_id`)
            FROM `god2_game`.`item_sets` set_row
            LEFT JOIN `god2_game`.`item_set_members` member_row ON member_row.`set_id`=set_row.`set_id` AND member_row.`enabled`=1
            WHERE set_row.`enabled`=1
            GROUP BY set_row.`set_id`,set_row.`name_zh_tw`,set_row.`description_zh_tw`
            ORDER BY set_row.`set_id`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedEquipmentSet(
                reader.GetInt32(0), reader.GetString(1), Math.Max(1, reader.GetInt32(3)),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2), "Verified", "EvidenceBlocked", false));
        }
        return result;
    }

    private static async Task<List<PromotedEquipmentSetMember>> LoadEquipmentMembersAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<PromotedEquipmentSetMember>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT member_row.`set_id`,member_row.`item_id`,
                   CASE COALESCE(member_row.`slot_name`,'Unknown')
                       WHEN '防具' THEN 'Armor'
                       WHEN '頭盔' THEN 'Helmet'
                       WHEN '手套' THEN 'Gloves'
                       WHEN '鞋子' THEN 'Shoes'
                       WHEN '武器' THEN 'Weapon'
                       ELSE COALESCE(member_row.`slot_name`,'Unknown')
                   END
            FROM `god2_game`.`item_set_members` member_row
            JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=member_row.`set_id` AND set_row.`enabled`=1
            WHERE member_row.`enabled`=1 ORDER BY member_row.`set_id`,member_row.`item_id`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedEquipmentSetMember(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), "Verified"));
        }
        return result;
    }

    private static async Task<List<PromotedPetInnate>> LoadPetInnatesAsync(
        MySqlConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var result = new List<PromotedPetInnate>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `pet_template_id`,`name_zh_tw` FROM `god2_game`.`pet_templates` WHERE `enabled`=1 ORDER BY `pet_template_id`;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PromotedPetInnate(reader.GetInt32(0), reader.GetString(1), null, null, null, "Verified", "NotApplicable"));
        }
        return result;
    }

    private OperationResult<T> Missing<T>(string family, int identity) =>
        OperationResult<T>.Failure(
            $"canonical_catalog.{family}_missing",
            "The requested gameplay content identity is not enabled in the Active canonical catalog.",
            identity.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private OperationResult FailAndClear(string code, string message) =>
        // Reload failures must retain the last immutable, validated snapshot.
        OperationResult.Failure(code, message, nameof(MariaDbCanonicalGameplayContentRuntime));
}
