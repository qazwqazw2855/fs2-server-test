using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbPetLifecycleRepository : MariaDbRuntimeRepository
{
    private const string AcquireOperation = "Acquire";
    private const string LevelUpOperation = "LevelUp";
    private const string FourthSkillOperation = "FourthSkill";
    private const string DeathOperation = "Death";

    public MariaDbPetLifecycleRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<PetAcquisitionTransactionResult> AcquireAsync(
        PetAcquisitionTransactionRequest request,
        PetGrowthGradeEngine growthEngine,
        CancellationToken cancellationToken)
    {
        if (!Valid(request.TransactionId, request.IdempotencyKey, request.Authority) ||
            request.PetTemplateId <= 0 || string.IsNullOrWhiteSpace(request.PetName) ||
            request.InitialAutomaticGrowthTotal is not (4 or 5))
        {
            return FailedAcquire(request, "pet.acquire.request_invalid");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var payloadHash = PayloadHash(request);
            var replay = await ReadReplayAsync<PetAcquisitionTransactionResult>(
                connection, transaction, request.IdempotencyKey, payloadHash, AcquireOperation, cancellationToken);
            if (replay.Conflict)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, "pet.acquire.idempotency_conflict");
            }
            if (replay.Result is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result with { Replayed = true };
            }

            if (!await LockActorAsync(connection, transaction, request.Authority, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, "pet.acquire.session_or_character_invalid");
            }

            var template = await LoadTemplateAsync(connection, transaction, request.PetTemplateId, cancellationToken);
            if (template is null || template.CategoryId is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, "pet.acquire.template_growth_unavailable");
            }

            var initialLevel = request.Origin == PetAcquisitionOrigin.CapturedWild
                ? request.WildEncounterLevel.GetValueOrDefault()
                : 1;
            if (initialLevel is < 1 or > 99 ||
                (request.Origin == PetAcquisitionOrigin.LevelOnePet &&
                 (request.WildSourceMonsterId is not null || request.WildEncounterLevel is not null)))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, "pet.acquire.origin_invalid");
            }

            var classification = growthEngine.ClassifyInitial(request.InitialAutomaticGrowthTotal);
            if (!classification.Succeeded || classification.Value is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, classification.Error.Code);
            }

            var stats = growthEngine.CalculateCoreStats(template.CategoryId.Value, classification.Value.Grade, initialLevel);
            if (!stats.Succeeded || stats.Value is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedAcquire(request, stats.Error.Code);
            }

            if (request.Origin == PetAcquisitionOrigin.CapturedWild)
            {
                var wild = await LoadWildMonsterAsync(
                    connection, transaction, request.WildSourceMonsterId.GetValueOrDefault(), initialLevel, cancellationToken);
                if (wild is null || template.WildSourceMonsterId != wild.MonsterId)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return FailedAcquire(request, "pet.acquire.wild_source_mismatch");
                }

                var petAttributes = BuildAttributes(template, stats.Value);
                var capture = new PetCaptureConversionEngine().Capture(wild, new CapturablePetTemplate(template.TemplateId, petAttributes));
                if (!capture.Succeeded)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return FailedAcquire(request, capture.Error.Code);
                }
            }

            var petInstanceId = await ReservePetInstanceIdAsync(connection, transaction, cancellationToken);
            var maximumHp = template.BaseMaximumHp ?? PetVitalRules.CalculateMaximumHp(stats.Value);
            var maximumMp = template.BaseMaximumMp ?? PetVitalRules.CalculateMaximumMp(stats.Value);
            await InsertPetAsync(
                connection, transaction, request, template, petInstanceId, initialLevel,
                classification.Value, stats.Value, maximumHp, maximumMp, cancellationToken);
            await MaterializeInnateSkillsAsync(connection, transaction, petInstanceId, template.TemplateId, initialLevel, cancellationToken);

            var result = new PetAcquisitionTransactionResult(
                request.TransactionId, false, petInstanceId, template.TemplateId, initialLevel,
                classification.Value, stats.Value, maximumHp, maximumMp, template.BaseMaximumLifespan, string.Empty);
            await CompleteAsync(
                connection, transaction, request.IdempotencyKey, payloadHash, request.TransactionId,
                request.Authority.CharacterId, AcquireOperation, petInstanceId, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException or JsonException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return FailedAcquire(request, "pet.acquire.persistence_failed");
        }
    }

    public async Task<PetLevelUpTransactionResult> ApplyLevelUpAsync(
        PetLevelUpTransactionRequest request,
        PetGrowthGradeEngine growthEngine,
        CancellationToken cancellationToken)
    {
        if (!Valid(request.TransactionId, request.IdempotencyKey, request.Authority) ||
            request.PetInstanceId <= 0 || request.ReachedLevel is < 2 or > 99 ||
            request.ExperienceAfter < 0 || request.AutomaticGrowthTotal <= 0)
        {
            return FailedLevelUp(request, "pet.level_up.request_invalid");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var payloadHash = PayloadHash(request);
            var replay = await ReadReplayAsync<PetLevelUpTransactionResult>(
                connection, transaction, request.IdempotencyKey, payloadHash, LevelUpOperation, cancellationToken);
            if (replay.Conflict)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, "pet.level_up.idempotency_conflict");
            }
            if (replay.Result is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result with { Replayed = true };
            }

            if (!await LockActorAsync(connection, transaction, request.Authority, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, "pet.level_up.session_or_character_invalid");
            }

            var pet = await LockPetAsync(connection, transaction, request.Authority.CharacterId, request.PetInstanceId, cancellationToken);
            if (pet is null || pet.CategoryId is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, "pet.level_up.pet_or_growth_missing");
            }
            if (request.ReachedLevel != pet.Level + 1 || request.ExperienceAfter < pet.Experience)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, "pet.level_up.progression_invalid");
            }

            var observed = growthEngine.ObserveLevelUp(pet.Growth, request.ReachedLevel, request.AutomaticGrowthTotal);
            if (!observed.Succeeded || observed.Value is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, observed.Error.Code);
            }
            var stats = growthEngine.CalculateCoreStats(pet.CategoryId.Value, observed.Value.Grade, request.ReachedLevel);
            if (!stats.Succeeded || stats.Value is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedLevelUp(request, stats.Error.Code);
            }

            var maximumHp = pet.TemplateMaximumHp ?? PetVitalRules.CalculateMaximumHp(stats.Value);
            var maximumMp = pet.TemplateMaximumMp ?? PetVitalRules.CalculateMaximumMp(stats.Value);
            await UpdatePetLevelAsync(
                connection, transaction, pet, request, observed.Value, stats.Value, maximumHp, maximumMp, cancellationToken);
            await MaterializeInnateSkillsAsync(
                connection, transaction, pet.PetInstanceId, pet.TemplateId, request.ReachedLevel, cancellationToken);

            var result = new PetLevelUpTransactionResult(
                request.TransactionId, false, pet.PetInstanceId, pet.Level, request.ReachedLevel,
                request.ExperienceAfter, observed.Value, stats.Value, maximumHp, maximumMp, string.Empty);
            await CompleteAsync(
                connection, transaction, request.IdempotencyKey, payloadHash, request.TransactionId,
                request.Authority.CharacterId, LevelUpOperation, pet.PetInstanceId, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException or JsonException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return FailedLevelUp(request, "pet.level_up.persistence_failed");
        }
    }

    public async Task<PetFourthSkillTransactionResult> TeachFourthSkillAsync(
        PetFourthSkillTransactionRequest request,
        PetFourthSkillEngine engine,
        CancellationToken cancellationToken)
    {
        if (!Valid(request.TransactionId, request.IdempotencyKey, request.Authority) ||
            request.PetInstanceId <= 0 || request.LearningItemTemplateId <= 0)
        {
            return FailedFourthSkill(request, "pet.fourth_skill.request_invalid");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var payloadHash = PayloadHash(request);
            var replay = await ReadReplayAsync<PetFourthSkillTransactionResult>(
                connection, transaction, request.IdempotencyKey, payloadHash, FourthSkillOperation, cancellationToken);
            if (replay.Conflict)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, "pet.fourth_skill.idempotency_conflict");
            }
            if (replay.Result is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result with { Replayed = true };
            }

            if (!await LockActorAsync(connection, transaction, request.Authority, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, "pet.fourth_skill.session_or_character_invalid");
            }

            var petLevel = await LockPetLevelAsync(connection, transaction, request.Authority.CharacterId, request.PetInstanceId, cancellationToken);
            var mapping = await LoadLearningItemAsync(connection, transaction, request.LearningItemTemplateId, cancellationToken);
            var occupied = await IsFourthSlotOccupiedAsync(connection, transaction, request.PetInstanceId, cancellationToken);
            if (petLevel is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, "pet.fourth_skill.pet_missing");
            }
            if (mapping is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, "pet.fourth_skill.item_mapping_unverified");
            }

            var evaluation = engine.Evaluate(new(
                request.PetInstanceId, petLevel.Value, occupied, request.LearningItemTemplateId,
                mapping.SkillId, mapping.SkillName));
            if (!evaluation.Succeeded || evaluation.Value is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, evaluation.Error.Code);
            }

            var inventoryVersion = await LockInventoryStateAsync(connection, transaction, request.Authority.CharacterId, cancellationToken);
            var item = await LockInventoryItemAsync(
                connection, transaction, request.Authority.CharacterId, request.LearningItemTemplateId, cancellationToken);
            if (inventoryVersion is null || item is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedFourthSkill(request, "pet.fourth_skill.learning_item_missing");
            }

            var nextVersion = checked(inventoryVersion.Value + 1);
            await ConsumeInventoryItemAsync(connection, transaction, item, nextVersion, cancellationToken);
            await AdvanceInventoryVersionAsync(
                connection, transaction, request.Authority.CharacterId, inventoryVersion.Value, cancellationToken);
            await InsertFourthSkillAsync(connection, transaction, request.PetInstanceId, mapping, request.LearningItemTemplateId, cancellationToken);

            var result = new PetFourthSkillTransactionResult(
                request.TransactionId, false, request.PetInstanceId, mapping.SkillId, mapping.SkillName,
                inventoryVersion.Value, nextVersion, string.Empty);
            await CompleteAsync(
                connection, transaction, request.IdempotencyKey, payloadHash, request.TransactionId,
                request.Authority.CharacterId, FourthSkillOperation, request.PetInstanceId, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException or JsonException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return FailedFourthSkill(request, "pet.fourth_skill.persistence_failed");
        }
    }

    public async Task<PetDeathTransactionResult> ApplyDeathAsync(
        PetDeathTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Valid(request.TransactionId, request.IdempotencyKey, request.Authority) || request.PetInstanceId <= 0)
        {
            return FailedDeath(request, "pet.death.request_invalid");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var payloadHash = PayloadHash(request);
            var replay = await ReadReplayAsync<PetDeathTransactionResult>(
                connection, transaction, request.IdempotencyKey, payloadHash, DeathOperation, cancellationToken);
            if (replay.Conflict)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedDeath(request, "pet.death.idempotency_conflict");
            }
            if (replay.Result is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result with { Replayed = true };
            }
            if (!await LockActorAsync(connection, transaction, request.Authority, cancellationToken))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedDeath(request, "pet.death.session_or_character_invalid");
            }

            var lifespan = await LockPetLifespanAsync(
                connection, transaction, request.Authority.CharacterId, request.PetInstanceId, cancellationToken);
            if (lifespan is null || lifespan.Value.Current is null || lifespan.Value.Maximum is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedDeath(request, "pet.death.lifespan_unconfigured");
            }

            var after = Math.Max(0, lifespan.Value.Current.Value - 1);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = "UPDATE `god2_player`.`character_pets` SET `current_hp`=0,`current_lifespan`=@after,`is_deployed`=0,`updated_at_utc`=UTC_TIMESTAMP(6) WHERE `pet_instance_id`=@petId AND `owner_character_id`=@characterId AND `enabled`=1;";
                command.Parameters.AddWithValue("@after", after);
                command.Parameters.AddWithValue("@petId", request.PetInstanceId);
                command.Parameters.AddWithValue("@characterId", request.Authority.CharacterId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("Pet changed during death transaction.");
                }
            }

            var result = new PetDeathTransactionResult(
                request.TransactionId, false, request.PetInstanceId,
                lifespan.Value.Current, after, string.Empty);
            await CompleteAsync(
                connection, transaction, request.IdempotencyKey, payloadHash, request.TransactionId,
                request.Authority.CharacterId, DeathOperation, request.PetInstanceId, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException or JsonException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return FailedDeath(request, "pet.death.persistence_failed");
        }
    }

    private static bool Valid(Guid transactionId, string idempotencyKey, PetMutationAuthority authority) =>
        transactionId != Guid.Empty && !string.IsNullOrWhiteSpace(idempotencyKey) &&
        authority.CharacterId > 0 && authority.AccountId > 0 && !string.IsNullOrWhiteSpace(authority.SessionId);

    private static async Task<bool> LockActorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PetMutationAuthority authority,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT character_row.`character_id`
            FROM `god2_player`.`characters` character_row
            JOIN `god2_player`.`accounts` account_row ON account_row.`account_id`=character_row.`account_id`
            WHERE character_row.`character_id`=@characterId AND character_row.`account_id`=@accountId
              AND character_row.`enabled`=1 AND character_row.`deleted_at_utc` IS NULL
              AND account_row.`current_session_id`=@sessionId
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", authority.CharacterId);
        command.Parameters.AddWithValue("@accountId", authority.AccountId);
        command.Parameters.AddWithValue("@sessionId", authority.SessionId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<PetTemplateRow?> LoadTemplateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int templateId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `pet_template_id`,`name_zh_tw`,`pet_category_id`,`wild_source_monster_id`,
                   `base_max_hp`,`base_max_mp`,`base_max_lifespan`,`base_metal`,`base_wood`,`base_water`,`base_fire`,`base_earth`
            FROM `god2_game`.`pet_templates`
            WHERE `pet_template_id`=@templateId AND `enabled`=1;
            """;
        command.Parameters.AddWithValue("@templateId", templateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PetTemplateRow(
                reader.GetInt32("pet_template_id"), reader.GetString("name_zh_tw"), NullableInt32(reader, "pet_category_id"),
                NullableInt64(reader, "wild_source_monster_id"), NullableInt64(reader, "base_max_hp"),
                NullableInt64(reader, "base_max_mp"), NullableInt64(reader, "base_max_lifespan"),
                NullableInt32(reader, "base_metal") ?? 0, NullableInt32(reader, "base_wood") ?? 0,
                NullableInt32(reader, "base_water") ?? 0, NullableInt32(reader, "base_fire") ?? 0,
                NullableInt32(reader, "base_earth") ?? 0)
            : null;
    }

    private static async Task<WildMonsterCaptureSource?> LoadWildMonsterAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long monsterId,
        int encounterLevel,
        CancellationToken cancellationToken)
    {
        if (monsterId <= 0)
        {
            return null;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `monster_id`,`level`,`name_color_zh_tw`,`is_quest_monster`,`is_formation_boss`,`capture_eligibility`,
                   `max_hp`,`max_mp`,`constitution`,`strength`,`intelligence`,`speed`,`metal`,`wood`,`water`,`fire`,`earth`
            FROM `god2_game`.`monsters`
            WHERE `monster_id`=@monsterId AND `enabled`=1;
            """;
        command.Parameters.AddWithValue("@monsterId", monsterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new WildMonsterCaptureSource(
            reader.GetInt64("monster_id"), encounterLevel, ParseNameColor(NullableString(reader, "name_color_zh_tw")),
            NullableBoolean(reader, "is_quest_monster") ?? false, NullableBoolean(reader, "is_formation_boss") ?? false,
            ParseCaptureEligibility(NullableString(reader, "capture_eligibility")),
            new PetBaseAttributes(
                NullableInt64(reader, "max_hp") ?? 0, NullableInt64(reader, "max_mp") ?? 0,
                NullableInt32(reader, "constitution") ?? 0, NullableInt32(reader, "strength") ?? 0,
                NullableInt32(reader, "intelligence") ?? 0, NullableInt32(reader, "speed") ?? 0,
                NullableInt32(reader, "metal") ?? 0, NullableInt32(reader, "wood") ?? 0,
                NullableInt32(reader, "water") ?? 0, NullableInt32(reader, "fire") ?? 0,
                NullableInt32(reader, "earth") ?? 0));
    }

    private static PetBaseAttributes BuildAttributes(PetTemplateRow template, PetCoreStats stats) => new(
        template.BaseMaximumHp ?? PetVitalRules.CalculateMaximumHp(stats),
        template.BaseMaximumMp ?? PetVitalRules.CalculateMaximumMp(stats),
        stats.Constitution, stats.Strength, stats.Intelligence, stats.Speed,
        template.Metal, template.Wood, template.Water, template.Fire, template.Earth);

    private static async Task<long> ReservePetInstanceIdAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "INSERT INTO `god2_player`.`pet_instance_identity_sequence` (`reserved_at_utc`) VALUES (UTC_TIMESTAMP(6));";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    private static async Task InsertPetAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PetAcquisitionTransactionRequest request,
        PetTemplateRow template,
        long petInstanceId,
        int initialLevel,
        PetGrowthClassification growth,
        PetCoreStats stats,
        long maximumHp,
        long maximumMp,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`character_pets`
                (`pet_instance_id`,`owner_character_id`,`pet_template_id`,`name`,`initial_level`,`acquisition_origin`,
                 `wild_source_monster_id`,`wild_encounter_level`,`level`,`experience`,`current_hp`,`max_hp`,`current_mp`,`max_mp`,
                 `current_lifespan`,`max_lifespan`,`strength_base`,`strength_bonus`,`constitution_base`,`constitution_bonus`,
                 `intelligence_base`,`intelligence_bonus`,`speed_base`,`speed_bonus`,`metal_base`,`metal_bonus`,`wood_base`,`wood_bonus`,
                 `water_base`,`water_bonus`,`fire_base`,`fire_bonus`,`earth_base`,`earth_bonus`,`initial_growth_quality`,`growth_grade`,
                 `growth_grade_status`,`growth_grade_confirmed_level`,`last_automatic_growth_total`,`remaining_stat_points`,`rebirth_count`,
                 `is_deployed`,`is_active`,`enabled`,`admin_note`)
            VALUES
                (@petId,@characterId,@templateId,@name,@initialLevel,@origin,@wildMonsterId,@wildLevel,@initialLevel,0,
                 @maxHp,@maxHp,@maxMp,@maxMp,@maxLifespan,@maxLifespan,@strength,0,@constitution,0,@intelligence,0,@speed,0,
                 @metal,0,@wood,0,@water,0,@fire,0,@earth,0,@initialQuality,@grade,@gradeStatus,@confirmedLevel,@growthTotal,0,0,0,0,1,
                 'Created by authoritative pet lifecycle transaction');
            """;
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@characterId", request.Authority.CharacterId);
        command.Parameters.AddWithValue("@templateId", template.TemplateId);
        command.Parameters.AddWithValue("@name", request.PetName.Trim());
        command.Parameters.AddWithValue("@initialLevel", initialLevel);
        command.Parameters.AddWithValue("@origin", request.Origin.ToString());
        command.Parameters.AddWithValue("@wildMonsterId", DbValue(request.WildSourceMonsterId));
        command.Parameters.AddWithValue("@wildLevel", DbValue(request.WildEncounterLevel));
        command.Parameters.AddWithValue("@maxHp", maximumHp);
        command.Parameters.AddWithValue("@maxMp", maximumMp);
        command.Parameters.AddWithValue("@maxLifespan", DbValue(template.BaseMaximumLifespan));
        command.Parameters.AddWithValue("@strength", stats.Strength);
        command.Parameters.AddWithValue("@constitution", stats.Constitution);
        command.Parameters.AddWithValue("@intelligence", stats.Intelligence);
        command.Parameters.AddWithValue("@speed", stats.Speed);
        command.Parameters.AddWithValue("@metal", template.Metal);
        command.Parameters.AddWithValue("@wood", template.Wood);
        command.Parameters.AddWithValue("@water", template.Water);
        command.Parameters.AddWithValue("@fire", template.Fire);
        command.Parameters.AddWithValue("@earth", template.Earth);
        command.Parameters.AddWithValue("@initialQuality", growth.InitialQuality.ToString());
        command.Parameters.AddWithValue("@grade", growth.Grade.ToString());
        command.Parameters.AddWithValue("@gradeStatus", growth.Status.ToString());
        command.Parameters.AddWithValue("@confirmedLevel", DbValue(growth.ConfirmedAtLevel));
        command.Parameters.AddWithValue("@growthTotal", growth.LastAutomaticGrowthTotal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<LockedPet?> LockPetAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        long petInstanceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT pet_row.`pet_instance_id`,pet_row.`pet_template_id`,template_row.`pet_category_id`,pet_row.`level`,pet_row.`experience`,
                   pet_row.`current_hp`,pet_row.`max_hp`,pet_row.`current_mp`,pet_row.`max_mp`,
                   pet_row.`initial_growth_quality`,pet_row.`growth_grade`,pet_row.`growth_grade_status`,
                   pet_row.`growth_grade_confirmed_level`,pet_row.`last_automatic_growth_total`,
                   template_row.`base_max_hp`,template_row.`base_max_mp`
            FROM `god2_player`.`character_pets` pet_row
            JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_template_id`=pet_row.`pet_template_id`
            WHERE pet_row.`pet_instance_id`=@petId AND pet_row.`owner_character_id`=@characterId
              AND pet_row.`enabled`=1 AND template_row.`enabled`=1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new LockedPet(
            reader.GetInt64("pet_instance_id"), reader.GetInt32("pet_template_id"), NullableInt32(reader, "pet_category_id"),
            reader.GetInt32("level"), reader.GetInt64("experience"), NullableInt64(reader, "current_hp") ?? 0,
            NullableInt64(reader, "max_hp") ?? 0, NullableInt64(reader, "current_mp") ?? 0, NullableInt64(reader, "max_mp") ?? 0,
            new PetGrowthClassification(
                ParseEnum<PetInitialGrowthQuality>(reader.GetString("initial_growth_quality")),
                ParseEnum<PetGrowthGrade>(reader.GetString("growth_grade")),
                ParseEnum<PetGrowthGradeStatus>(reader.GetString("growth_grade_status")),
                NullableInt32(reader, "growth_grade_confirmed_level"),
                NullableInt32(reader, "last_automatic_growth_total") ?? 0),
            NullableInt64(reader, "base_max_hp"), NullableInt64(reader, "base_max_mp"));
    }

    private static async Task UpdatePetLevelAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedPet pet,
        PetLevelUpTransactionRequest request,
        PetGrowthClassification growth,
        PetCoreStats stats,
        long maximumHp,
        long maximumMp,
        CancellationToken cancellationToken)
    {
        var currentHp = Math.Min(maximumHp, checked(pet.CurrentHp + Math.Max(0, maximumHp - pet.MaximumHp)));
        var currentMp = Math.Min(maximumMp, checked(pet.CurrentMp + Math.Max(0, maximumMp - pet.MaximumMp)));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`character_pets`
            SET `level`=@level,`experience`=@experience,`current_hp`=@currentHp,`max_hp`=@maxHp,`current_mp`=@currentMp,`max_mp`=@maxMp,
                `strength_base`=@strength,`constitution_base`=@constitution,`intelligence_base`=@intelligence,`speed_base`=@speed,
                `initial_growth_quality`=@initialQuality,`growth_grade`=@grade,`growth_grade_status`=@gradeStatus,
                `growth_grade_confirmed_level`=@confirmedLevel,`last_automatic_growth_total`=@growthTotal,`updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `pet_instance_id`=@petId AND `owner_character_id`=@characterId AND `level`=@previousLevel AND `enabled`=1;
            """;
        command.Parameters.AddWithValue("@level", request.ReachedLevel);
        command.Parameters.AddWithValue("@experience", request.ExperienceAfter);
        command.Parameters.AddWithValue("@currentHp", currentHp);
        command.Parameters.AddWithValue("@maxHp", maximumHp);
        command.Parameters.AddWithValue("@currentMp", currentMp);
        command.Parameters.AddWithValue("@maxMp", maximumMp);
        command.Parameters.AddWithValue("@strength", stats.Strength);
        command.Parameters.AddWithValue("@constitution", stats.Constitution);
        command.Parameters.AddWithValue("@intelligence", stats.Intelligence);
        command.Parameters.AddWithValue("@speed", stats.Speed);
        command.Parameters.AddWithValue("@initialQuality", growth.InitialQuality.ToString());
        command.Parameters.AddWithValue("@grade", growth.Grade.ToString());
        command.Parameters.AddWithValue("@gradeStatus", growth.Status.ToString());
        command.Parameters.AddWithValue("@confirmedLevel", DbValue(growth.ConfirmedAtLevel));
        command.Parameters.AddWithValue("@growthTotal", growth.LastAutomaticGrowthTotal);
        command.Parameters.AddWithValue("@petId", pet.PetInstanceId);
        command.Parameters.AddWithValue("@characterId", request.Authority.CharacterId);
        command.Parameters.AddWithValue("@previousLevel", pet.Level);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Pet changed during level-up transaction.");
        }
    }

    private static async Task MaterializeInnateSkillsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long petInstanceId,
        int templateId,
        int level,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`character_pet_skills`
                (`pet_instance_id`,`slot_index`,`skill_id`,`skill_name_cache`,`skill_level`,`unlock_source`,
                 `is_unlocked`,`is_enabled`,`admin_note`)
            SELECT @petId,template_skill.`slot_index`,template_skill.`skill_id`,template_skill.`skill_name_cache`,
                   template_skill.`skill_level`,'InnateLevel',template_skill.`skill_level`<=@level,template_skill.`skill_level`<=@level,
                   'Materialized from verified pet template skill mapping'
            FROM `god2_game`.`pet_template_skills` template_skill
            WHERE template_skill.`pet_template_id`=@templateId AND template_skill.`enabled`=1
            ON DUPLICATE KEY UPDATE
                `skill_id`=VALUES(`skill_id`),`skill_name_cache`=VALUES(`skill_name_cache`),`skill_level`=VALUES(`skill_level`),
                `is_unlocked`=VALUES(`is_unlocked`),`is_enabled`=VALUES(`is_enabled`);
            """;
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@templateId", templateId);
        command.Parameters.AddWithValue("@level", level);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int?> LockPetLevelAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        long petInstanceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `level` FROM `god2_player`.`character_pets` WHERE `pet_instance_id`=@petId AND `owner_character_id`=@characterId AND `enabled`=1 FOR UPDATE;";
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@characterId", characterId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<LearningItem?> LoadLearningItemAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT mapping.`skill_id`,skill_row.`name_zh_tw`
            FROM `god2_game`.`pet_skill_learning_items` mapping
            JOIN `god2_game`.`skills` skill_row ON skill_row.`skill_id`=mapping.`skill_id` AND skill_row.`enabled`=1
            WHERE mapping.`item_id`=@itemId AND mapping.`enabled`=1;
            """;
        command.Parameters.AddWithValue("@itemId", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LearningItem(reader.GetInt64("skill_id"), reader.GetString("name_zh_tw"))
            : null;
    }

    private static async Task<bool> IsFourthSlotOccupiedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long petInstanceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `pet_instance_id` FROM `god2_player`.`character_pet_skills` WHERE `pet_instance_id`=@petId AND `slot_index`=4 AND `is_unlocked`=1 LIMIT 1 FOR UPDATE;";
        command.Parameters.AddWithValue("@petId", petInstanceId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<long?> LockInventoryStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `InventoryVersion` FROM `god2_player`.`player_inventory_state` WHERE `CharacterId`=@characterId FOR UPDATE;";
        command.Parameters.AddWithValue("@characterId", characterId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<InventoryItem?> LockInventoryItemAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        int itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `inventory_id`,`quantity`,`slot_version`
            FROM `god2_player`.`character_inventory`
            WHERE `character_id`=@characterId AND `item_id`=@itemId AND `enabled`=1
              AND `deleted_at_utc` IS NULL AND `quantity`>0
            ORDER BY `slot_index` LIMIT 1 FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@itemId", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new InventoryItem(reader.GetInt64("inventory_id"), reader.GetInt64("quantity"), reader.GetInt64("slot_version"))
            : null;
    }

    private static async Task ConsumeInventoryItemAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        InventoryItem item,
        long nextVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`character_inventory`
            SET `quantity`=`quantity`-1,`inventory_version`=@nextVersion,`slot_version`=`slot_version`+1,
                `enabled`=CASE WHEN `quantity`=1 THEN 0 ELSE 1 END,
                `deleted_at_utc`=CASE WHEN `quantity`=1 THEN UTC_TIMESTAMP(6) ELSE NULL END,
                `updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `inventory_id`=@inventoryId AND `quantity`=@quantity AND `slot_version`=@slotVersion;
            """;
        command.Parameters.AddWithValue("@nextVersion", nextVersion);
        command.Parameters.AddWithValue("@inventoryId", item.InventoryId);
        command.Parameters.AddWithValue("@quantity", item.Quantity);
        command.Parameters.AddWithValue("@slotVersion", item.SlotVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Learning item changed during transaction.");
        }
    }

    private static async Task AdvanceInventoryVersionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "UPDATE `god2_player`.`player_inventory_state` SET `InventoryVersion`=`InventoryVersion`+1 WHERE `CharacterId`=@characterId AND `InventoryVersion`=@expectedVersion;";
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Inventory version changed during pet skill transaction.");
        }
    }

    private static async Task InsertFourthSkillAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long petInstanceId,
        LearningItem mapping,
        int itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`character_pet_skills`
                (`pet_instance_id`,`slot_index`,`skill_id`,`skill_name_cache`,`skill_level`,`unlock_source`,`learned_from_item_id`,
                 `learned_at_utc`,`is_unlocked`,`is_enabled`,`admin_note`)
            VALUES (@petId,4,@skillId,@skillName,1,'FedItem',@itemId,UTC_TIMESTAMP(6),1,1,
                    'Learned through authoritative fourth-skill transaction');
            """;
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@skillId", mapping.SkillId);
        command.Parameters.AddWithValue("@skillName", mapping.SkillName);
        command.Parameters.AddWithValue("@itemId", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(long? Current, long? Maximum)?> LockPetLifespanAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        long petInstanceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `current_lifespan`,`max_lifespan` FROM `god2_player`.`character_pets` WHERE `pet_instance_id`=@petId AND `owner_character_id`=@characterId AND `enabled`=1 FOR UPDATE;";
        command.Parameters.AddWithValue("@petId", petInstanceId);
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (NullableInt64(reader, "current_lifespan"), NullableInt64(reader, "max_lifespan"))
            : null;
    }

    private static async Task<ReplayRead<T>> ReadReplayAsync<T>(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string idempotencyKey,
        string payloadHash,
        string operationType,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `operation_fingerprint_sha256`,`operation_type`,`result_json` FROM `god2_player`.`pet_operation_idempotency` WHERE `idempotency_key_hash`=@keyHash FOR UPDATE;";
        command.Parameters.AddWithValue("@keyHash", Hash(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ReplayRead<T>(null, false);
        }
        if (!string.Equals(reader.GetString("operation_fingerprint_sha256"), payloadHash, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString("operation_type"), operationType, StringComparison.Ordinal))
        {
            return new ReplayRead<T>(null, true);
        }
        return new ReplayRead<T>(JsonSerializer.Deserialize<T>(reader.GetString("result_json")), false);
    }

    private static async Task CompleteAsync<T>(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string idempotencyKey,
        string payloadHash,
        Guid transactionId,
        long characterId,
        string operationType,
        long? petInstanceId,
        T result,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`pet_operation_idempotency`
                    (`idempotency_key_hash`,`operation_fingerprint_sha256`,`transaction_id`,`character_id`,`operation_type`,`pet_instance_id`,`result_json`)
                VALUES (@keyHash,@payloadHash,@transactionId,@characterId,@operationType,@petId,@resultJson);
                """;
            command.Parameters.AddWithValue("@keyHash", Hash(idempotencyKey));
            command.Parameters.AddWithValue("@payloadHash", payloadHash);
            command.Parameters.AddWithValue("@transactionId", transactionId.ToString("D"));
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@operationType", operationType);
            command.Parameters.AddWithValue("@petId", DbValue(petInstanceId));
            command.Parameters.AddWithValue("@resultJson", json);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`pet_operation_audit`
                    (`transaction_id`,`character_id`,`operation_type`,`pet_instance_id`,`detail_json`)
                VALUES (@transactionId,@characterId,@operationType,@petId,@detailJson);
                """;
            command.Parameters.AddWithValue("@transactionId", transactionId.ToString("D"));
            command.Parameters.AddWithValue("@characterId", characterId);
            command.Parameters.AddWithValue("@operationType", operationType);
            command.Parameters.AddWithValue("@petId", DbValue(petInstanceId));
            command.Parameters.AddWithValue("@detailJson", json);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string PayloadHash<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static int? NullableInt32(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(name);

    private static long? NullableInt64(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(name);

    private static bool? NullableBoolean(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetBoolean(name);

    private static string? NullableString(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new InvalidOperationException($"Unknown {typeof(T).Name} value.");

    private static MonsterNameColor ParseNameColor(string? value) => value switch
    {
        "White" or "白" or "白色" or "白字" => MonsterNameColor.White,
        "Yellow" or "黃" or "黃色" or "黃字" => MonsterNameColor.Yellow,
        "Red" or "紅" or "紅色" or "紅字" => MonsterNameColor.Red,
        _ => MonsterNameColor.Unknown
    };

    private static MonsterCaptureEligibility ParseCaptureEligibility(string? value) => value switch
    {
        "可捕捉" => MonsterCaptureEligibility.Capturable,
        "Capturable" => MonsterCaptureEligibility.Capturable,
        "不可捕捉" => MonsterCaptureEligibility.Blocked,
        "Blocked" => MonsterCaptureEligibility.Blocked,
        _ => MonsterCaptureEligibility.Unknown
    };

    private static PetAcquisitionTransactionResult FailedAcquire(PetAcquisitionTransactionRequest request, string code) =>
        new(request.TransactionId, false, 0, request.PetTemplateId, 0, null, null, 0, 0, null, code);

    private static PetLevelUpTransactionResult FailedLevelUp(PetLevelUpTransactionRequest request, string code) =>
        new(request.TransactionId, false, request.PetInstanceId, 0, 0, request.ExperienceAfter, null, null, 0, 0, code);

    private static PetFourthSkillTransactionResult FailedFourthSkill(PetFourthSkillTransactionRequest request, string code) =>
        new(request.TransactionId, false, request.PetInstanceId, 0, string.Empty, 0, 0, code);

    private static PetDeathTransactionResult FailedDeath(PetDeathTransactionRequest request, string code) =>
        new(request.TransactionId, false, request.PetInstanceId, null, null, code);

    private sealed record ReplayRead<T>(T? Result, bool Conflict) where T : class;

    private sealed record PetTemplateRow(
        int TemplateId,
        string Name,
        int? CategoryId,
        long? WildSourceMonsterId,
        long? BaseMaximumHp,
        long? BaseMaximumMp,
        long? BaseMaximumLifespan,
        int Metal,
        int Wood,
        int Water,
        int Fire,
        int Earth);

    private sealed record LockedPet(
        long PetInstanceId,
        int TemplateId,
        int? CategoryId,
        int Level,
        long Experience,
        long CurrentHp,
        long MaximumHp,
        long CurrentMp,
        long MaximumMp,
        PetGrowthClassification Growth,
        long? TemplateMaximumHp,
        long? TemplateMaximumMp);

    private sealed record LearningItem(long SkillId, string SkillName);

    private sealed record InventoryItem(long InventoryId, long Quantity, long SlotVersion);
}
