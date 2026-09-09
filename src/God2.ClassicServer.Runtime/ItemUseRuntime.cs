using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum ItemEffectType
{
    RestoreHp,
    RestoreMp,
    RestoreHpPercent,
    RestoreMpPercent,
    CleansePoison,
    CleanseSleep,
    CleanseSeal,
    CleansePetrify,
    CleanseConfusion,
    ApplyStrengthBuff,
    ApplyConstitutionBuff,
    ApplyIntelligenceBuff,
    ApplySpeedBuff,
    ApplyBattleExperienceBuff,
    ApplyMetalElementBuff,
    ApplyWoodElementBuff,
    ApplyWaterElementBuff,
    ApplyFireElementBuff,
    ApplyEarthElementBuff
}

public enum ItemUsageContext
{
    World,
    Battle
}

public enum ItemTargetPolicy
{
    Self,
    OtherAllowed
}

public sealed record ItemEffectDatabaseRecord(
    int ItemTemplateId,
    int EffectIndex,
    string EffectType,
    long NumericValue,
    string UsageScope,
    string TargetPolicy,
    string EvidenceStatus,
    bool Enabled,
    string Source,
    int? DurationSeconds = null);

public sealed record ItemEffectDefinition(
    int ItemTemplateId,
    int EffectIndex,
    ItemEffectType EffectType,
    long NumericValue,
    bool WorldAllowed,
    bool BattleAllowed,
    ItemTargetPolicy TargetPolicy,
    string EvidenceStatus,
    bool Enabled,
    string Source,
    int? DurationSeconds = null);

public sealed class ItemEffectContentMapper
{
    public OperationResult<ItemEffectDefinition> Map(ItemEffectDatabaseRecord record)
    {
        if (!Enum.TryParse<ItemEffectType>(record.EffectType, ignoreCase: true, out var effectType))
        {
            return OperationResult<ItemEffectDefinition>.Failure(
                "item.effect_type_unknown",
                "Item effect type is not supported by the runtime.",
                record.Source);
        }

        if (!Enum.TryParse<ItemTargetPolicy>(record.TargetPolicy, ignoreCase: true, out var targetPolicy))
        {
            return OperationResult<ItemEffectDefinition>.Failure(
                "item.target_policy_unknown",
                "Item target policy is not supported by the runtime.",
                record.Source);
        }

        var (worldAllowed, battleAllowed) = record.UsageScope.Trim().ToUpperInvariant() switch
        {
            "WORLD" => (true, false),
            "BATTLE" => (false, true),
            "BOTH" => (true, true),
            _ => (false, false)
        };

        if (record.ItemTemplateId <= 0 || record.EffectIndex <= 0 || record.NumericValue <= 0 ||
            (!worldAllowed && !battleAllowed))
        {
            return OperationResult<ItemEffectDefinition>.Failure(
                "item.effect_invalid",
                "Item effect identity, value, or usage scope is invalid.",
                record.Source);
        }

        return OperationResult<ItemEffectDefinition>.Success(new ItemEffectDefinition(
            record.ItemTemplateId,
            record.EffectIndex,
            effectType,
            record.NumericValue,
            worldAllowed,
            battleAllowed,
            targetPolicy,
            record.EvidenceStatus,
            record.Enabled,
            record.Source,
            record.DurationSeconds));
    }
}

public sealed class ItemEffectDefinitionCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ItemEffectDefinition>> _definitions;

    public ItemEffectDefinitionCatalog(IEnumerable<ItemEffectDefinition> definitions)
    {
        _definitions = new ReadOnlyDictionary<int, IReadOnlyList<ItemEffectDefinition>>(
            definitions
                .Where(definition => definition.Enabled)
                .GroupBy(definition => definition.ItemTemplateId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ItemEffectDefinition>)group.OrderBy(definition => definition.EffectIndex).ToArray()));
    }

    public static ItemEffectDefinitionCatalog Empty { get; } = new([]);

    public int EffectCount => _definitions.Values.Sum(definitions => definitions.Count);

    public IReadOnlyList<ItemEffectDefinition> Resolve(int itemTemplateId) =>
        _definitions.TryGetValue(itemTemplateId, out var definitions) ? definitions : [];
}

public sealed record ItemUseActorState(
    long CharacterId,
    int Level,
    int RebirthCount,
    string ClassName,
    string Gender,
    long CurrentHp,
    long MaximumHp,
    long CurrentMp,
    long MaximumMp,
    IReadOnlyList<string>? ActiveStatusCodes = null);

public sealed record ItemUseRequest(
    int ItemTemplateId,
    ItemUsageContext Context,
    bool TargetsSelf = true);

public sealed record ItemUseEvaluation(
    int ItemTemplateId,
    int ConsumedQuantity,
    long RestoredHp,
    long RestoredMp,
    ItemUseActorState UpdatedActor,
    IReadOnlyList<string>? RemovedStatusCodes = null,
    IReadOnlyList<ItemUseAppliedBuff>? AppliedBuffs = null,
    IReadOnlyList<ItemUseAppliedExperienceBuff>? AppliedExperienceBuffs = null,
    IReadOnlyList<ItemUseAppliedElementBuff>? AppliedElementBuffs = null);

public sealed record ItemUseAppliedBuff(
    string StatCode,
    long Amount,
    TimeSpan? Duration);

public sealed record ItemUseAppliedExperienceBuff(
    long Percent,
    TimeSpan? Duration);

public sealed record ItemUseAppliedElementBuff(
    string ElementCode,
    long Amount,
    TimeSpan? Duration);

public sealed record ItemUseTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    long AccountId,
    string SessionId,
    int ItemTemplateId,
    ItemUsageContext Context,
    bool TargetsSelf,
    DateTimeOffset CreatedAtUtc);

public sealed record ItemUseTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    long RestoredHp,
    long RestoredMp,
    long CurrentHp,
    long CurrentMp,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public interface IItemUseCoordinator
{
    Task<ItemUseTransactionResult> UseItemAsync(
        ItemUseTransactionRequest request,
        CancellationToken cancellationToken);
}

public sealed class ItemUseEffectEngine
{
    private readonly ItemDefinitionCatalog _items;
    private readonly ItemEffectDefinitionCatalog _effects;

    public ItemUseEffectEngine(ItemDefinitionCatalog items, ItemEffectDefinitionCatalog effects)
    {
        _items = items;
        _effects = effects;
    }

    public OperationResult<ItemUseEvaluation> Evaluate(
        ItemUseRequest request,
        ItemUseActorState actor)
    {
        var resolvedItem = _items.Resolve(request.ItemTemplateId);
        if (!resolvedItem.Succeeded || resolvedItem.Value is null)
        {
            return Failure("item.use.invalid_item", "Item is missing or disabled.");
        }

        var item = resolvedItem.Value;
        var allEffects = _effects.Resolve(request.ItemTemplateId);
        if ((item.ItemCategory != ItemCategory.Consumable && allEffects.Count == 0) || item.Usable != true)
        {
            return Failure("item.use.not_consumable", "Item is not an enabled consumable or verified usable effect item.");
        }

        if ((request.Context == ItemUsageContext.World && item.NormalUse != true) ||
            (request.Context == ItemUsageContext.Battle && item.BattleUse != true))
        {
            return Failure("item.use.context_blocked", "Item cannot be used in the current context.");
        }

        if (!request.TargetsSelf && item.UseOnOther != true)
        {
            return Failure("item.use.target_blocked", "Item cannot target another actor.");
        }

        if (item.RequiredLevel is int requiredLevel && actor.Level < requiredLevel)
        {
            return Failure("item.use.level_blocked", "Character level is below the item requirement.");
        }

        if (item.MinimumRebirth is int minimumRebirth && actor.RebirthCount < minimumRebirth)
        {
            return Failure("item.use.rebirth_blocked", "Character rebirth count is below the item requirement.");
        }

        if (!MatchesRestriction(item.ClassRestriction, actor.ClassName))
        {
            return Failure("item.use.class_blocked", "Character class does not satisfy the item requirement.");
        }

        if (!MatchesRestriction(item.GenderRestriction, actor.Gender))
        {
            return Failure("item.use.gender_blocked", "Character gender does not satisfy the item requirement.");
        }

        if (actor.CurrentHp <= 0)
        {
            return Failure("item.use.dead_actor", "Recovery items cannot revive a defeated actor.");
        }

        var effects = allEffects
            .Where(effect => request.Context == ItemUsageContext.World ? effect.WorldAllowed : effect.BattleAllowed)
            .Where(effect => request.TargetsSelf || effect.TargetPolicy == ItemTargetPolicy.OtherAllowed)
            .ToArray();
        if (effects.Length == 0)
        {
            return Failure("item.use.effect_missing", "No verified effect is available for this item and context.");
        }

        var nextHp = actor.CurrentHp;
        var nextMp = actor.CurrentMp;
        var removedStatusCodes = new List<string>();
        var appliedBuffs = new List<ItemUseAppliedBuff>();
        var appliedExperienceBuffs = new List<ItemUseAppliedExperienceBuff>();
        var appliedElementBuffs = new List<ItemUseAppliedElementBuff>();
        foreach (var effect in effects)
        {
            switch (effect.EffectType)
            {
                case ItemEffectType.RestoreHp:
                    nextHp = ClampAdd(nextHp, effect.NumericValue, actor.MaximumHp);
                    break;
                case ItemEffectType.RestoreMp:
                    nextMp = ClampAdd(nextMp, effect.NumericValue, actor.MaximumMp);
                    break;
                case ItemEffectType.RestoreHpPercent:
                    nextHp = ClampAdd(nextHp, PercentOfMaximum(actor.MaximumHp, effect.NumericValue), actor.MaximumHp);
                    break;
                case ItemEffectType.RestoreMpPercent:
                    nextMp = ClampAdd(nextMp, PercentOfMaximum(actor.MaximumMp, effect.NumericValue), actor.MaximumMp);
                    break;
                case ItemEffectType.CleansePoison:
                    TryCleanse(actor, "poison", removedStatusCodes);
                    break;
                case ItemEffectType.CleanseSleep:
                    TryCleanse(actor, "sleep", removedStatusCodes);
                    break;
                case ItemEffectType.CleanseSeal:
                    TryCleanse(actor, "seal", removedStatusCodes);
                    break;
                case ItemEffectType.CleansePetrify:
                    TryCleanse(actor, "petrify", removedStatusCodes);
                    break;
                case ItemEffectType.CleanseConfusion:
                    TryCleanse(actor, "confusion", removedStatusCodes);
                    break;
                case ItemEffectType.ApplyStrengthBuff:
                    appliedBuffs.Add(TimedBuff("strength", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyConstitutionBuff:
                    appliedBuffs.Add(TimedBuff("constitution", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyIntelligenceBuff:
                    appliedBuffs.Add(TimedBuff("intelligence", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplySpeedBuff:
                    appliedBuffs.Add(TimedBuff("speed", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyBattleExperienceBuff:
                    appliedExperienceBuffs.Add(TimedExperienceBuff(effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyMetalElementBuff:
                    appliedElementBuffs.Add(TimedElementBuff("metal", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyWoodElementBuff:
                    appliedElementBuffs.Add(TimedElementBuff("wood", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyWaterElementBuff:
                    appliedElementBuffs.Add(TimedElementBuff("water", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyFireElementBuff:
                    appliedElementBuffs.Add(TimedElementBuff("fire", effect.NumericValue, effect.DurationSeconds));
                    break;
                case ItemEffectType.ApplyEarthElementBuff:
                    appliedElementBuffs.Add(TimedElementBuff("earth", effect.NumericValue, effect.DurationSeconds));
                    break;
            }
        }

        var restoredHp = nextHp - actor.CurrentHp;
        var restoredMp = nextMp - actor.CurrentMp;
        if (restoredHp == 0 && restoredMp == 0 && removedStatusCodes.Count == 0 &&
            appliedBuffs.Count == 0 && appliedExperienceBuffs.Count == 0 && appliedElementBuffs.Count == 0)
        {
            return Failure("item.use.no_effect", "Item would not change the target state.");
        }

        return OperationResult<ItemUseEvaluation>.Success(new ItemUseEvaluation(
            request.ItemTemplateId,
            ConsumedQuantity: 1,
            restoredHp,
            restoredMp,
            actor with { CurrentHp = nextHp, CurrentMp = nextMp },
            removedStatusCodes,
            appliedBuffs,
            appliedExperienceBuffs,
            appliedElementBuffs));

        OperationResult<ItemUseEvaluation> Failure(string code, string message) =>
            OperationResult<ItemUseEvaluation>.Failure(code, message, request.ItemTemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool MatchesRestriction(string? restriction, string actual)
    {
        if (string.IsNullOrWhiteSpace(restriction))
        {
            return true;
        }

        return restriction
            .Split(['、', ',', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(actual, StringComparer.OrdinalIgnoreCase);
    }

    private static long ClampAdd(long current, long amount, long maximum)
    {
        if (maximum <= current)
        {
            return current;
        }

        return amount >= maximum - current ? maximum : current + amount;
    }

    private static long PercentOfMaximum(long maximum, long percent)
    {
        if (maximum <= 0 || percent <= 0)
        {
            return 0;
        }

        var recovered = (long)Math.Floor(maximum * (decimal)percent / 100m);
        return recovered > 0 ? recovered : 1;
    }

    private static void TryCleanse(ItemUseActorState actor, string statusCode, List<string> removedStatusCodes)
    {
        if (actor.ActiveStatusCodes is null ||
            !actor.ActiveStatusCodes.Contains(statusCode, StringComparer.OrdinalIgnoreCase) ||
            removedStatusCodes.Contains(statusCode, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        removedStatusCodes.Add(statusCode);
    }

    private static ItemUseAppliedBuff TimedBuff(string statCode, long amount, int? durationSeconds) =>
        new(statCode, amount, durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null);

    private static ItemUseAppliedExperienceBuff TimedExperienceBuff(long percent, int? durationSeconds) =>
        new(percent, durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null);

    private static ItemUseAppliedElementBuff TimedElementBuff(string elementCode, long amount, int? durationSeconds) =>
        new(elementCode, amount, durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null);
}

