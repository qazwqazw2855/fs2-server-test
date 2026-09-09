using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbGameplayContentCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbGameplayContentCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<GameplayCoreContentCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var itemRecords = await LoadItemRecordsAsync(connection, cancellationToken);
        var itemEffects = await LoadItemEffectsAsync(connection, cancellationToken);
        var merchantItems = await LoadMerchantItemsAsync(connection, cancellationToken);

        var mapper = new ItemContentMapper();
        var validation = new ItemContentValidator().Validate(
            itemRecords.Select(mapper.Map),
            merchantItems.Select(record => record.Definition));
        var merchantMappings = await LoadMerchantMappingsAsync(connection, merchantItems, validation.Catalog, cancellationToken);
        var effectMapper = new ItemEffectContentMapper();
        var mappedEffects = itemEffects
            .Select(effectMapper.Map)
            .Where(result => result.Succeeded && result.Value is not null)
            .Select(result => result.Value!);

        return new GameplayCoreContentCatalog(
            validation.Catalog,
            new MerchantDefinitionCatalog(merchantMappings),
            validation.Issues,
            validation.Quarantined,
            new ItemEffectDefinitionCatalog(mappedEffects));
    }

    private static async Task<IReadOnlyList<ItemDatabaseRecord>> LoadItemRecordsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var records = new List<ItemDatabaseRecord>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT item_row.`item_id` AS `Id`, COALESCE(item_row.`code`,CAST(item_row.`item_id` AS CHAR)) AS `Code`,
                   item_row.`name_zh_tw` AS `RuntimeDisplayName`, NULL AS `LocalizationKey`,
                   CASE item_row.`item_category`
                       WHEN '一般' THEN 'Generic'
                       WHEN '裝備' THEN 'Equipment'
                       WHEN '任務' THEN 'Quest'
                       WHEN '材料' THEN 'Material'
                       WHEN '消耗品' THEN 'Consumable'
                       WHEN '類貨幣' THEN 'CurrencyLike'
                       ELSE item_row.`item_category`
                   END AS `ItemCategory`,
                   CASE WHEN item_row.`maximum_stack` IS NULL THEN 'Unknown' WHEN item_row.`maximum_stack`=1 THEN 'Single' ELSE 'Stackable' END AS `StackPolicy`,
                   item_row.`maximum_stack` AS `MaxStack`, NULL AS `BindPolicy`,
                   CASE item_row.`tradable` WHEN 1 THEN 'Tradable' WHEN 0 THEN 'NotTradable' ELSE 'Unknown' END AS `TradePolicy`,
                   CASE WHEN item_row.`item_category` IN ('Quest','任務') OR item_row.`sell_price` IS NULL THEN 'NotSellable' ELSE 'Sellable' END AS `SellPolicy`,
                   item_row.`buy_price` AS `BaseBuyPrice`, item_row.`sell_price` AS `BaseSellPrice`,
                   CASE WHEN item_row.`buy_price` IS NOT NULL OR item_row.`sell_price` IS NOT NULL THEN 'Gold' ELSE 'Unknown' END AS `CurrencyType`,
                   CASE
                       WHEN item_row.`item_category` NOT IN ('武器','裝備','法寶') THEN 'Unknown'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('武器','Weapon') THEN 'Weapon'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('防具','Armor') THEN 'Armor'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('飾品','Accessory') THEN 'Accessory'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('頭盔','Helmet') THEN 'Helmet'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('法寶','MagicTreasure') THEN 'MagicTreasure'
                       WHEN COALESCE(item_row.`item_family`,item_row.`item_category`) IN ('裝備','Equipment') THEN 'Equipment'
                       ELSE 'Unknown'
                   END AS `EquipmentCategory`,
                   CASE WHEN item_row.`item_category` IN ('Consumable','消耗品') THEN 'Consumable' ELSE 'Unknown' END AS `ConsumableCategory`,
                   item_row.`item_category` IN ('Quest','任務') AS `QuestItemFlag`, item_row.`enabled` AS `Enabled`,
                   '{}' AS `RawMetadata`, 'canonical-item-catalog' AS `ContentVersion`,
                   item_row.`usable` AS `Usable`,
                   CASE WHEN rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`normal_use` ELSE NULL END AS `NormalUse`,
                   CASE WHEN rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`battle_use` ELSE NULL END AS `BattleUse`,
                   item_row.`equippable` AS `Equippable`, item_row.`use_on_other` AS `UseOnOther`,
                   item_row.`required_level` AS `RequiredLevel`,
                   CASE WHEN rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`class_restriction_zh_tw` ELSE NULL END AS `ClassRestriction`,
                   CASE WHEN rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`minimum_rebirth` ELSE NULL END AS `MinimumRebirth`,
                   CASE WHEN rule_row.`enabled`=1 AND rule_row.`runtime_eligible`=1 THEN rule_row.`gender_restriction_zh_tw` ELSE NULL END AS `GenderRestriction`
            FROM `god2_game`.`items` item_row
            LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
            WHERE item_row.`enabled`=1
            ORDER BY item_row.`item_id`;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ItemDatabaseRecord(
                reader.GetInt32("Id"),
                reader.GetString("Code"),
                reader.GetString("RuntimeDisplayName"),
                GetNullableString(reader, "LocalizationKey"),
                GetNullableString(reader, "ItemCategory"),
                GetNullableString(reader, "StackPolicy"),
                GetNullableInt32(reader, "MaxStack"),
                GetNullableString(reader, "BindPolicy"),
                GetNullableString(reader, "TradePolicy"),
                GetNullableString(reader, "SellPolicy"),
                GetNullableInt64(reader, "BaseBuyPrice"),
                GetNullableInt64(reader, "BaseSellPrice"),
                GetNullableString(reader, "CurrencyType"),
                GetNullableString(reader, "EquipmentCategory"),
                GetNullableString(reader, "ConsumableCategory"),
                GetBooleanOrDefault(reader, "QuestItemFlag", defaultValue: false),
                GetBooleanOrDefault(reader, "Enabled", defaultValue: true),
                GetNullableString(reader, "RawMetadata") ?? "{}",
                GetNullableString(reader, "ContentVersion") ?? "database-items-v1",
                "MariaDB:items",
                GetNullableBoolean(reader, "Usable"),
                GetNullableBoolean(reader, "NormalUse"),
                GetNullableBoolean(reader, "BattleUse"),
                GetNullableBoolean(reader, "Equippable"),
                GetNullableBoolean(reader, "UseOnOther"),
                GetNullableInt32(reader, "RequiredLevel"),
                GetNullableString(reader, "ClassRestriction"),
                GetNullableInt32(reader, "MinimumRebirth"),
                GetNullableString(reader, "GenderRestriction")));
        }

        return records;
    }

    private static async Task<IReadOnlyList<ItemEffectDatabaseRecord>> LoadItemEffectsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var records = new List<ItemEffectDatabaseRecord>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `item_id` AS `ItemId`,`effect_index` AS `EffectIndex`,
                   CASE `effect_type`
                       WHEN '生命恢復' THEN 'RestoreHp'
                       WHEN '法力恢復' THEN 'RestoreMp'
                       WHEN '生命百分比恢復' THEN 'RestoreHpPercent'
                       WHEN '法力百分比恢復' THEN 'RestoreMpPercent'
                       WHEN '解除中毒' THEN 'CleansePoison'
                       WHEN '解除睡眠' THEN 'CleanseSleep'
                       WHEN '解除神仙封' THEN 'CleanseSeal'
                       WHEN '解除石化' THEN 'CleansePetrify'
                       WHEN '解除混亂' THEN 'CleanseConfusion'
                       WHEN '提升腕力' THEN 'ApplyStrengthBuff'
                       WHEN '提升體力' THEN 'ApplyConstitutionBuff'
                       WHEN '提升智力' THEN 'ApplyIntelligenceBuff'
                       WHEN '提升速度' THEN 'ApplySpeedBuff'
                       WHEN '戰鬥經驗加成' THEN 'ApplyBattleExperienceBuff'
                       WHEN '提升金屬性' THEN 'ApplyMetalElementBuff'
                       WHEN '提升木屬性' THEN 'ApplyWoodElementBuff'
                       WHEN '提升水屬性' THEN 'ApplyWaterElementBuff'
                       WHEN '提升火屬性' THEN 'ApplyFireElementBuff'
                       WHEN '提升土屬性' THEN 'ApplyEarthElementBuff'
                       ELSE `effect_type`
                   END AS `EffectType`,
                   `numeric_value` AS `NumericValue`,
                   `duration_seconds` AS `DurationSeconds`,
                   CASE `usage_scope`
                       WHEN '世界' THEN 'World'
                       WHEN '戰鬥' THEN 'Battle'
                       WHEN '世界與戰鬥' THEN 'Both'
                       ELSE `usage_scope`
                   END AS `UsageScope`,
                   CASE `target_policy`
                       WHEN '自身' THEN 'Self'
                       WHEN '可對他人' THEN 'OtherAllowed'
                       ELSE `target_policy`
                   END AS `TargetPolicy`,
                   'runtime-item-effect' AS `EvidenceStatus`,`enabled` AS `Enabled`,'god2_game.item_effects' AS `Source`
            FROM `god2_game`.`item_effects`
            WHERE `enabled`=1 AND `runtime_eligible`=1
            ORDER BY `item_id`,`effect_index`;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ItemEffectDatabaseRecord(
                reader.GetInt32("ItemId"),
                reader.GetInt32("EffectIndex"),
                reader.GetString("EffectType"),
                reader.GetInt64("NumericValue"),
                reader.GetString("UsageScope"),
                reader.GetString("TargetPolicy"),
                reader.GetString("EvidenceStatus"),
                reader.GetBoolean("Enabled"),
                reader.GetString("Source"),
                GetNullableInt32(reader, "DurationSeconds")));
        }

        return records;
    }

    private static async Task<IReadOnlyList<MerchantItemCatalogRecord>> LoadMerchantItemsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        var records = new List<MerchantItemCatalogRecord>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `merchant_id` AS `MerchantId`, `item_id` AS `ItemId`,
                   `selling_price` AS `Price`, `purchasing_price` AS `PurchasingPrice`
            FROM `god2_game`.`merchant_inventory`
            WHERE `enabled`=1 AND `selling_price` IS NOT NULL
            ORDER BY `merchant_id`, `item_id`;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MerchantItemCatalogRecord(
                reader.GetInt32("MerchantId"),
                new MerchantItemDefinition(
                    reader.GetInt32("ItemId"),
                    reader.GetInt64("Price"),
                    "{}",
                    reader.IsDBNull("PurchasingPrice")
                        ? null
                        : reader.GetInt64("PurchasingPrice"))));
        }

        return records;
    }

    private static async Task<IReadOnlyList<MerchantMapping>> LoadMerchantMappingsAsync(
        MySqlConnection connection,
        IReadOnlyList<MerchantItemCatalogRecord> merchantItems,
        ItemDefinitionCatalog itemCatalog,
        CancellationToken cancellationToken)
    {
        var itemsByMerchant = merchantItems
            .GroupBy(entry => entry.MerchantId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MerchantItemDefinition>)group.Select(entry => entry.Definition).ToArray());

        var records = new List<MerchantMapping>();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `merchant_id` AS `Id`, `npc_id` AS `NpcId`, `name_zh_tw` AS `Name`
            FROM `god2_game`.`merchants`
            WHERE `enabled`=1
            ORDER BY `merchant_id`;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var merchantId = reader.GetInt32("Id");
            itemsByMerchant.TryGetValue(merchantId, out var items);
            records.Add(new MerchantMapping(
                merchantId,
                GetNullableInt32(reader, "NpcId") ?? 0,
                PlacementId: 0,
                Evidence: "MariaDB:merchants",
                Name: reader.GetString("Name"),
                MerchantGroupId: "Default",
                CurrencyType: ResolveMerchantCurrency(items ?? [], itemCatalog),
                Items: items ?? [],
                Enabled: true,
                RawMetadata: "{}",
                ContentVersion: "database-merchants-v1"));
        }

        return records;
    }

    private static string ResolveMerchantCurrency(IReadOnlyList<MerchantItemDefinition> items, ItemDefinitionCatalog itemCatalog)
    {
        var currencies = items
            .Select(item => itemCatalog.Resolve(item.ItemTemplateId).Value?.CurrencyType)
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return currencies.Length == 1 ? currencies[0]! : "Unknown";
    }

    private static int? GetNullableInt32(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(name);

    private static long? GetNullableInt64(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(name);

    private static string? GetNullableString(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);

    private static bool? GetNullableBoolean(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetBoolean(name);

    private static bool GetBooleanOrDefault(MySqlDataReader reader, string name, bool defaultValue) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? defaultValue : reader.GetBoolean(name);

    private sealed record MerchantItemCatalogRecord(
        int MerchantId,
        MerchantItemDefinition Definition);
}

public sealed class MariaDbGameplayInventoryRepository : MariaDbRuntimeRepository, IInventoryPersistenceStore
{
    private const int DefaultCapacity = 32;
    public const string ExpectedInventoryStateLockCommandText = """
        SELECT `InventoryId`, `InventoryVersion`
        FROM `god2_player`.`player_inventory_state`
        WHERE `CharacterId` = @characterId
        FOR UPDATE;
        """;

    public MariaDbGameplayInventoryRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<OperationResult> ValidateAuthorityAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var shape = InventoryAuthorityPolicy.ValidateShape(request);
        if (!shape.Succeeded)
        {
            return shape;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession
            ? """
                SELECT `c`.`character_id`
                FROM `god2_player`.`characters` AS `c`
                INNER JOIN `god2_player`.`accounts` AS `a` ON `a`.`account_id` = `c`.`account_id`
                WHERE `c`.`character_id` = @characterId
                  AND `c`.`account_id` = @accountId
                  AND `c`.`deleted_at_utc` IS NULL
                  AND `a`.`status` IN ('Active','鍟熺敤')
                  AND `a`.`current_session_id` = @sessionId
                LIMIT 1;
                """
            : """
                SELECT `character_id`
                FROM `god2_player`.`characters`
                WHERE `character_id` = @characterId AND `deleted_at_utc` IS NULL
                LIMIT 1;
                """;
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        if (request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession)
        {
            command.Parameters.AddWithValue("@accountId", request.AccountId!.Value);
            command.Parameters.AddWithValue("@sessionId", request.SessionId);
        }

        return await command.ExecuteScalarAsync(cancellationToken) is null
            ? OperationResult.Failure(
                request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession
                    ? "inventory.session_authority_rejected"
                    : "inventory.character_missing",
                "The inventory mutation authority could not be verified.")
            : OperationResult.Success;
    }

    public async Task<InventoryPersistenceBundle> LoadAsync(long characterId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var inventory = await LoadInventoryAsync(connection, transaction, characterId, cancellationToken);
            var currency = await LoadCurrencyAsync(connection, transaction, characterId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new InventoryPersistenceBundle(inventory, currency);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<OperationResult<IReadOnlyList<long>>> ReservePersistentItemIdsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            return OperationResult<IReadOnlyList<long>>.Failure(
                "inventory.identity_count_invalid",
                "Persistent inventory identity reservation count cannot be negative.");
        }

        if (count == 0)
        {
            return OperationResult<IReadOnlyList<long>>.Success([]);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var identities = new List<long>(count);
            for (var index = 0; index < count; index++)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = "INSERT INTO `god2_player`.`inventory_item_identity_sequence` (`ReservedAtUtc`) VALUES (UTC_TIMESTAMP(6));";
                await command.ExecuteNonQueryAsync(cancellationToken);
                identities.Add(command.LastInsertedId);
            }

            await transaction.CommitAsync(cancellationToken);
            return OperationResult<IReadOnlyList<long>>.Success(identities);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            return OperationResult<IReadOnlyList<long>>.Failure(
                "inventory.identity_reservation_failed",
                "MariaDB could not reserve persistent inventory identities.");
        }
    }

    public async Task<InventoryReplayLookup> FindCompletedAsync(string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `TransactionFingerprintSha256`, `TransactionId`, `CharacterId`, `Result`, `FailureCode`, `InventoryVersionBefore`, `InventoryVersionAfter`,
                   `CurrencyBefore`, `CurrencyAfter`
            FROM `god2_player`.`inventory_transaction_idempotency`
            WHERE `IdempotencyKeyHash` = @idempotencyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyHash", Hash(idempotencyKey));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new InventoryReplayLookup(false, true, null);
        }

        var storedPayloadHash = ReadString(reader, "TransactionFingerprintSha256");
        if (!string.Equals(storedPayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return new InventoryReplayLookup(true, false, null);
        }

        var parsed = Enum.TryParse<InventoryTransactionResultCode>(NormalizeInventoryResult(ReadString(reader, "Result")), out var code)
            ? code
            : InventoryTransactionResultCode.PersistenceFailure;
        var characterId = ReadInt64(reader, "CharacterId");
        var transactionId = Guid.Parse(ReadString(reader, "TransactionId"));
        var inventoryVersionBefore = ReadInt64(reader, "InventoryVersionBefore");
        var currencyBefore = ReadInt64(reader, "CurrencyBefore");
        var failureCode = ReadString(reader, "FailureCode");
        await reader.DisposeAsync();

        var authoritative = await LoadAsync(characterId, cancellationToken);
        var currentGold = authoritative.Currency.Balances
            .FirstOrDefault(balance => string.Equals(balance.CurrencyType, "Gold", StringComparison.OrdinalIgnoreCase))?.Balance ?? 0;

        return new InventoryReplayLookup(
            true,
            true,
            new InventoryTransactionResult(
                parsed,
                transactionId,
                SafeId(idempotencyKey),
                inventoryVersionBefore,
                authoritative.Inventory.Version,
                currencyBefore,
                currentGold,
                failureCode,
                authoritative.Inventory,
                authoritative.Currency));
    }

    public async Task<OperationResult> CommitAsync(InventoryPersistenceCommit commit, CancellationToken cancellationToken)
    {
        var authorityShape = InventoryAuthorityPolicy.ValidateShape(commit.Request);
        if (!authorityShape.Succeeded)
        {
            return authorityShape;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var expectedState = await LockAndValidateExpectedStateAsync(connection, transaction, commit, cancellationToken);
            if (!expectedState.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ResolveReplayOrFailureAsync(commit, expectedState, cancellationToken);
            }

            await UpsertInventoryStateAsync(connection, transaction, commit.InventoryAfter, cancellationToken);
            await ReplaceInventorySlotsAsync(connection, transaction, commit.InventoryAfter, cancellationToken);
            await UpsertCurrencyAsync(connection, transaction, commit.CurrencyAfter, cancellationToken);
            await InsertIdempotencyAsync(connection, transaction, commit, cancellationToken);
            await InsertAuditAsync(connection, transaction, commit.AuditRecord, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ResolveReplayOrFailureAsync(
                commit,
                OperationResult.Failure("inventory.persistence_duplicate", "MariaDB rejected a duplicate inventory persistence identity."),
                cancellationToken);
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure(
                "inventory.persistence_failure",
                $"MariaDB inventory transaction failed closed ({exception.GetType().Name}).");
        }
    }

    private async Task<OperationResult> ResolveReplayOrFailureAsync(
        InventoryPersistenceCommit commit,
        OperationResult fallback,
        CancellationToken cancellationToken)
    {
        var replay = await FindCompletedAsync(commit.Request.IdempotencyKey, commit.PayloadHash, cancellationToken);
        return replay is { Found: true, PayloadMatches: true, Result: not null }
            ? OperationResult.Failure("inventory.replay_completed", "The inventory transaction was already committed.")
            : replay is { Found: true, PayloadMatches: false }
                ? OperationResult.Failure("inventory.replay_conflict", "The idempotency key was reused with a different payload.")
                : fallback;
    }

    private static async Task<OperationResult> LockAndValidateExpectedStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        InventoryPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        await using (var character = connection.CreateCommand())
        {
            character.Transaction = transaction;
            character.CommandTimeout = CommandTimeoutSeconds;
            character.CommandText = commit.Request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession
                ? """
                    SELECT `c`.`character_id`
                    FROM `god2_player`.`characters` AS `c`
                    INNER JOIN `god2_player`.`accounts` AS `a` ON `a`.`account_id` = `c`.`account_id`
                    WHERE `c`.`character_id` = @characterId
                      AND `c`.`account_id` = @accountId
                      AND `c`.`deleted_at_utc` IS NULL
                      AND `a`.`status` IN ('Active','鍟熺敤')
                      AND `a`.`current_session_id` = @sessionId
                    FOR UPDATE;
                    """
                : """
                    SELECT `character_id`
                    FROM `god2_player`.`characters`
                    WHERE `character_id` = @characterId AND `deleted_at_utc` IS NULL
                    FOR UPDATE;
                    """;
            character.Parameters.AddWithValue("@characterId", commit.Request.CharacterId);
            if (commit.Request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession)
            {
                character.Parameters.AddWithValue("@accountId", commit.Request.AccountId!.Value);
                character.Parameters.AddWithValue("@sessionId", commit.Request.SessionId);
            }
            if (await character.ExecuteScalarAsync(cancellationToken) is null)
            {
                return OperationResult.Failure(
                    commit.Request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession
                        ? "inventory.session_authority_rejected"
                        : "inventory.character_missing",
                    "The inventory mutation authority changed before commit.");
            }
        }

        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandTimeout = CommandTimeoutSeconds;
            state.CommandText = ExpectedInventoryStateLockCommandText;
            state.Parameters.AddWithValue("@characterId", commit.Request.CharacterId);
            await using var reader = await state.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var inventoryId = Guid.Parse(ReadString(reader, "InventoryId"));
                var version = reader.GetInt64("InventoryVersion");
                if (inventoryId != commit.InventoryBefore.InventoryId || version != commit.InventoryBefore.Version)
                {
                    return OperationResult.Failure("inventory.version_conflict", "The persisted inventory changed before commit.");
                }
            }
            else if (commit.InventoryBefore.Version != 0)
            {
                return OperationResult.Failure("inventory.version_conflict", "The expected persisted inventory state is missing.");
            }
        }

        var persistedBalances = new List<CurrencyBalance>();
        await using (var currency = connection.CreateCommand())
        {
            currency.Transaction = transaction;
            currency.CommandTimeout = CommandTimeoutSeconds;
            currency.CommandText = """
                SELECT `CurrencyType`, `Balance`, `Version`, `DirtyState`
                FROM `god2_player`.`player_currency_balances`
                WHERE `CharacterId` = @characterId
                ORDER BY `CurrencyType`
                FOR UPDATE;
                """;
            currency.Parameters.AddWithValue("@characterId", commit.Request.CharacterId);
            await using var reader = await currency.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                persistedBalances.Add(new CurrencyBalance(
                    NormalizeCurrencyType(ReadString(reader, "CurrencyType")),
                    reader.GetInt64("Balance"),
                    reader.GetInt64("Version"),
                    NormalizeDirtyState(ReadString(reader, "DirtyState"))));
            }
        }

        if (persistedBalances.Count == 0)
        {
            persistedBalances.Add(new CurrencyBalance("Gold", 0, 0, "Clean"));
        }

        var expectedBalances = commit.CurrencyBefore.Balances
            .OrderBy(value => value.CurrencyType, StringComparer.OrdinalIgnoreCase);
        if (!persistedBalances
                .OrderBy(value => value.CurrencyType, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(expectedBalances))
        {
            return OperationResult.Failure("inventory.version_conflict", "The persisted currency wallet changed before commit.");
        }

        return OperationResult.Success;
    }

    private static async Task<PlayerInventorySnapshot> LoadInventoryAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        Guid inventoryId;
        var capacity = DefaultCapacity;
        long version = 0;
        long mutationSequence = 0;
        var dirtyState = "Clean";

        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandTimeout = CommandTimeoutSeconds;
            state.CommandText = """
                SELECT `InventoryId`, `Capacity`, `InventoryVersion`, `MutationSequence`, `DirtyState`
                FROM `god2_player`.`player_inventory_state`
                WHERE `CharacterId` = @characterId
                LIMIT 1;
                """;
            state.Parameters.AddWithValue("@characterId", characterId);
            await using var reader = await state.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                inventoryId = Guid.Parse(ReadString(reader, "InventoryId"));
                capacity = reader.GetInt32("Capacity");
                version = reader.GetInt64("InventoryVersion");
                mutationSequence = reader.GetInt64("MutationSequence");
                dirtyState = NormalizeDirtyState(ReadString(reader, "DirtyState"));
            }
            else
            {
                inventoryId = DeterministicInventoryId(characterId);
            }
        }

        var slots = new List<InventorySlot>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `slot_index` AS `SlotIndex`, `inventory_id` AS `PersistentInventoryItemId`,
                       `protocol_visible_item_id` AS `ProtocolVisibleItemId`, `item_id` AS `ItemId`, `quantity` AS `Quantity`,
                       `bind_state` AS `BindState`, `item_instance_metadata` AS `ItemInstanceMetadata`,
                       `created_at_utc` AS `CreatedAtUtc`, `updated_at_utc` AS `UpdatedAtUtc`, `slot_version` AS `SlotVersion`
                FROM `god2_player`.`character_inventory`
                WHERE `character_id` = @characterId AND `deleted_at_utc` IS NULL AND `enabled`=1
                ORDER BY `slot_index`;
                """;
            command.Parameters.AddWithValue("@characterId", characterId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var persistentItemId = reader.IsDBNull(reader.GetOrdinal("PersistentInventoryItemId"))
                    ? checked((characterId * 1_000_000) + reader.GetInt32("SlotIndex") + 1)
                    : reader.GetInt64("PersistentInventoryItemId");
                slots.Add(new InventorySlot(
                    reader.GetInt32("SlotIndex"),
                    persistentItemId,
                    RuntimeObjectIds.Static(RuntimeObjectKind.Item, checked((int)(persistentItemId & 0x7FFFFFFF)), "MariaDBInventory").RuntimeObjectId,
                    reader.GetInt64("ProtocolVisibleItemId"),
                    reader.GetInt32("ItemId"),
                    reader.GetInt32("Quantity"),
                    ReadString(reader, "BindState"),
                    reader.IsDBNull(reader.GetOrdinal("ItemInstanceMetadata")) ? "{}" : ReadString(reader, "ItemInstanceMetadata"),
                    ReadUtc(reader, "CreatedAtUtc"),
                    ReadUtc(reader, "UpdatedAtUtc"),
                    reader.GetInt64("SlotVersion")));
            }
        }

        return new PlayerInventorySnapshot(inventoryId, characterId, capacity, version, mutationSequence, dirtyState, slots);
    }

    private static async Task<CurrencyWalletSnapshot> LoadCurrencyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        var balances = new List<CurrencyBalance>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `CurrencyType`, `Balance`, `Version`, `DirtyState`
            FROM `god2_player`.`player_currency_balances`
            WHERE `CharacterId` = @characterId
            ORDER BY `CurrencyType`;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            balances.Add(new CurrencyBalance(
                NormalizeCurrencyType(ReadString(reader, "CurrencyType")),
                reader.GetInt64("Balance"),
                reader.GetInt64("Version"),
                NormalizeDirtyState(ReadString(reader, "DirtyState"))));
        }

        if (balances.Count == 0)
        {
            balances.Add(new CurrencyBalance("Gold", 0, 0, "Clean"));
        }

        return new CurrencyWalletSnapshot(characterId, balances);
    }

    private static async Task UpsertInventoryStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerInventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`player_inventory_state`
                (`CharacterId`, `InventoryId`, `Capacity`, `InventoryVersion`, `MutationSequence`, `DirtyState`, `UpdatedAtUtc`)
            VALUES
                (@characterId, @inventoryId, @capacity, @version, @mutationSequence, @dirtyState, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                `Capacity` = VALUES(`Capacity`),
                `InventoryVersion` = VALUES(`InventoryVersion`),
                `MutationSequence` = VALUES(`MutationSequence`),
                `DirtyState` = VALUES(`DirtyState`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
            """;
        command.Parameters.AddWithValue("@characterId", snapshot.CharacterId);
        command.Parameters.AddWithValue("@inventoryId", snapshot.InventoryId.ToString());
        command.Parameters.AddWithValue("@capacity", snapshot.Capacity);
        command.Parameters.AddWithValue("@version", snapshot.Version);
        command.Parameters.AddWithValue("@mutationSequence", snapshot.MutationSequence);
        command.Parameters.AddWithValue("@dirtyState", FormatDirtyState(snapshot.DirtyState));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceInventorySlotsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerInventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandTimeout = CommandTimeoutSeconds;
            delete.CommandText = "DELETE FROM `god2_player`.`character_inventory` WHERE `character_id` = @characterId;";
            delete.Parameters.AddWithValue("@characterId", snapshot.CharacterId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var slot in snapshot.Slots)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandTimeout = CommandTimeoutSeconds;
            insert.CommandText = """
                INSERT INTO `god2_player`.`character_inventory`
                    (`character_id`, `inventory_id`, `protocol_visible_item_id`, `slot_index`, `item_id`,
                     `quantity`, `inventory_version`, `slot_version`, `bind_state`, `item_instance_metadata`,
                     `bound`, `enabled`, `created_at_utc`, `updated_at_utc`, `deleted_at_utc`)
                VALUES
                    (@characterId, @persistentItemId, @protocolVisibleItemId, @slotIndex, @itemTemplateId,
                     @quantity, @inventoryVersion, @slotVersion, @bindState, @metadata,
                     0, 1, @createdAtUtc, @updatedAtUtc, NULL);
                """;
            insert.Parameters.AddWithValue("@characterId", snapshot.CharacterId);
            insert.Parameters.AddWithValue("@persistentItemId", slot.PersistentInventoryItemId);
            insert.Parameters.AddWithValue("@protocolVisibleItemId", slot.ProtocolVisibleItemId);
            insert.Parameters.AddWithValue("@slotIndex", slot.SlotIndex);
            insert.Parameters.AddWithValue("@itemTemplateId", slot.ItemTemplateId);
            insert.Parameters.AddWithValue("@quantity", slot.Quantity);
            insert.Parameters.AddWithValue("@inventoryVersion", snapshot.Version);
            insert.Parameters.AddWithValue("@slotVersion", slot.Version);
            insert.Parameters.AddWithValue("@bindState", slot.BindState);
            insert.Parameters.AddWithValue("@metadata", slot.ItemInstanceMetadata);
            insert.Parameters.AddWithValue("@createdAtUtc", slot.CreatedAtUtc.UtcDateTime);
            insert.Parameters.AddWithValue("@updatedAtUtc", slot.UpdatedAtUtc.UtcDateTime);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertCurrencyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CurrencyWalletSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var balance in snapshot.Balances)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`player_currency_balances`
                    (`CharacterId`, `CurrencyType`, `Balance`, `Version`, `DirtyState`, `UpdatedAtUtc`)
                VALUES
                    (@characterId, @currencyType, @balance, @version, @dirtyState, UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    `Balance` = VALUES(`Balance`),
                    `Version` = VALUES(`Version`),
                    `DirtyState` = VALUES(`DirtyState`),
                    `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
                """;
            command.Parameters.AddWithValue("@characterId", snapshot.CharacterId);
            command.Parameters.AddWithValue("@currencyType", balance.CurrencyType);
            command.Parameters.AddWithValue("@balance", balance.Balance);
            command.Parameters.AddWithValue("@version", balance.Version);
            command.Parameters.AddWithValue("@dirtyState", FormatDirtyState(balance.DirtyState));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertIdempotencyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        InventoryPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`inventory_transaction_idempotency`
                (`IdempotencyKeyHash`, `TransactionFingerprintSha256`, `TransactionId`, `CharacterId`, `OperationType`, `Result`,
                 `FailureCode`, `InventoryVersionBefore`, `InventoryVersionAfter`, `CurrencyBefore`, `CurrencyAfter`,
                 `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@idempotencyHash, @payloadHash, @transactionId, @characterId, @operationType, @result,
                 @failureCode, @inventoryVersionBefore, @inventoryVersionAfter, @currencyBefore, @currencyAfter,
                 @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@idempotencyHash", Hash(commit.Request.IdempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", commit.PayloadHash);
        command.Parameters.AddWithValue("@transactionId", commit.Request.TransactionId.ToString());
        command.Parameters.AddWithValue("@characterId", commit.Request.CharacterId);
        command.Parameters.AddWithValue("@operationType", FormatInventoryOperation(commit.Request.OperationType.ToString()));
        command.Parameters.AddWithValue("@result", FormatInventoryResult(commit.AuditRecord.Result.ToString()));
        command.Parameters.AddWithValue("@failureCode", commit.AuditRecord.FailureCode);
        command.Parameters.AddWithValue("@inventoryVersionBefore", commit.InventoryBefore.Version);
        command.Parameters.AddWithValue("@inventoryVersionAfter", commit.InventoryAfter.Version);
        command.Parameters.AddWithValue("@currencyBefore", commit.AuditRecord.CurrencyBefore);
        command.Parameters.AddWithValue("@currencyAfter", commit.AuditRecord.CurrencyAfter);
        command.Parameters.AddWithValue("@createdAtUtc", commit.Request.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", commit.AuditRecord.CompletedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        InventoryAuditRecord audit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`inventory_audit_ledger`
                (`AuditId`, `TransactionId`, `IdempotencySafeId`, `CharacterId`, `SessionId`, `OperationType`, `Source`,
                 `MerchantTemplateId`, `ItemTemplateId`, `InventoryItemId`, `QuantityBefore`, `QuantityAfter`,
                 `CurrencyType`, `CurrencyBefore`, `CurrencyAfter`, `InventoryVersionBefore`, `InventoryVersionAfter`,
                 `Result`, `FailureCode`, `CreatedAtUtc`, `CompletedAtUtc`, `CorrelationId`)
            VALUES
                (@auditId, @transactionId, @idempotencySafeId, @characterId, @sessionId, @operationType, @source,
                 @merchantTemplateId, @itemTemplateId, @inventoryItemId, @quantityBefore, @quantityAfter,
                 @currencyType, @currencyBefore, @currencyAfter, @inventoryVersionBefore, @inventoryVersionAfter,
                 @result, @failureCode, @createdAtUtc, @completedAtUtc, @correlationId);
            """;
        command.Parameters.AddWithValue("@auditId", audit.AuditId.ToString());
        command.Parameters.AddWithValue("@transactionId", audit.TransactionId.ToString());
        command.Parameters.AddWithValue("@idempotencySafeId", audit.IdempotencySafeId);
        command.Parameters.AddWithValue("@characterId", audit.CharacterId);
        command.Parameters.AddWithValue("@sessionId", audit.SessionId);
        command.Parameters.AddWithValue("@operationType", FormatInventoryOperation(audit.OperationType.ToString()));
        command.Parameters.AddWithValue("@source", FormatInventorySource(audit.Source));
        command.Parameters.AddWithValue("@merchantTemplateId", audit.MerchantTemplateId);
        command.Parameters.AddWithValue("@itemTemplateId", audit.ItemTemplateId);
        command.Parameters.AddWithValue("@inventoryItemId", audit.InventoryItemId);
        command.Parameters.AddWithValue("@quantityBefore", audit.QuantityBefore);
        command.Parameters.AddWithValue("@quantityAfter", audit.QuantityAfter);
        command.Parameters.AddWithValue("@currencyType", FormatCurrencyType(audit.CurrencyType));
        command.Parameters.AddWithValue("@currencyBefore", audit.CurrencyBefore);
        command.Parameters.AddWithValue("@currencyAfter", audit.CurrencyAfter);
        command.Parameters.AddWithValue("@inventoryVersionBefore", audit.InventoryVersionBefore);
        command.Parameters.AddWithValue("@inventoryVersionAfter", audit.InventoryVersionAfter);
        command.Parameters.AddWithValue("@result", FormatInventoryResult(audit.Result.ToString()));
        command.Parameters.AddWithValue("@failureCode", audit.FailureCode);
        command.Parameters.AddWithValue("@createdAtUtc", audit.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", audit.CompletedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@correlationId", audit.CorrelationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static long ReadInt64(MySqlDataReader reader, string name) =>
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static string ReadString(MySqlDataReader reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            string text => text,
            Guid guid => guid.ToString(),
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string NormalizeCurrencyType(string value) => value switch
    {
        "金幣" => "Gold",
        "無" => "None",
        _ => value
    };

    private static string FormatCurrencyType(string value) => value switch
    {
        "Gold" => "金幣",
        "None" => "無",
        _ => value
    };

    private static string NormalizeDirtyState(string value) => value switch
    {
        "乾淨" => "Clean",
        "已變更" => "Dirty",
        _ => value
    };

    private static string FormatDirtyState(string value) => value switch
    {
        "Clean" => "乾淨",
        "Dirty" => "已變更",
        _ => value
    };
    private static string FormatInventoryOperation(string value) => value switch
    {
        "MerchantBuy" => "商店購買",
        "MerchantSell" => "商店販售",
        "EnhanceEquipment" => "裝備強化",
        "UseItem" => "使用物品",
        _ => value
    };

    private static string FormatInventorySource(string value) => value switch
    {
        "Merchant" => "商店",
        "WorldInteraction:Merchant" => "世界互動：商店",
        "EquipmentEnhancement" => "裝備強化",
        "OfficialItemUse" => "官方物品使用",
        "LiveRecovery/Stages4-7-attempt-759-trace" => "官方實機商店買賣封包證據",
        _ => value
    };

    private static string FormatInventoryResult(string value) => value switch
    {
        "Success" => "成功",
        _ => value
    };

    private static string NormalizeInventoryResult(string value) => value switch
    {
        "成功" => "Success",
        _ => value
    };

    private static Guid DeterministicInventoryId(long characterId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"god2:inventory:{characterId}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string SafeId(string value) => Hash(value)[..16];
}


