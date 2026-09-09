using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum GameplayContentSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}

public enum ItemCategory
{
    Generic,
    Equipment,
    Consumable,
    Quest,
    Material,
    CurrencyLike,
    Unknown
}

public enum StackPolicy
{
    Single,
    Stackable,
    Unknown
}

public enum BindPolicy
{
    None,
    Bound,
    Unknown
}

public enum TradePolicy
{
    Tradable,
    NotTradable,
    Unknown
}

public enum SellPolicy
{
    Sellable,
    NotSellable,
    Unknown
}

public enum MerchantStockPolicy
{
    Unlimited,
    FixedStock,
    PerPlayer,
    TimedRestock,
    Unknown
}

public enum InventoryOperationType
{
    AddItem,
    RemoveItem,
    MoveItem,
    SwapItems,
    SplitStack,
    MergeStacks,
    MerchantBuy,
    MerchantSell,
    SystemGrant,
    SystemRemove
}

public enum InventoryMutationAuthorityKind
{
    PlayerSession,
    TrustedServer
}

public enum InventoryTransactionResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    Conflict,
    InsufficientCapacity,
    InsufficientQuantity,
    InsufficientCurrency,
    InvalidItem,
    InvalidMerchant,
    InvalidPrice,
    VersionConflict,
    PersistenceFailure,
    InternalFailure
}

public sealed record GameplayContentIssue(
    GameplayContentSeverity Severity,
    string Code,
    int? TemplateId,
    string Message,
    string Source);

public sealed record ItemDatabaseRecord(
    int ItemTemplateId,
    string InternalName,
    string DisplayName,
    string? LocalizationKey,
    string? ItemCategory,
    string? StackPolicy,
    int? MaximumStack,
    string? BindPolicy,
    string? TradePolicy,
    string? SellPolicy,
    long? BaseBuyPrice,
    long? BaseSellPrice,
    string? CurrencyType,
    string? EquipmentCategory,
    string? ConsumableCategory,
    bool QuestItemFlag,
    bool Enabled,
    string RawMetadata,
    string ContentVersion,
    string Source,
    bool? Usable = null,
    bool? NormalUse = null,
    bool? BattleUse = null,
    bool? Equippable = null,
    bool? UseOnOther = null,
    int? RequiredLevel = null,
    string? ClassRestriction = null,
    int? MinimumRebirth = null,
    string? GenderRestriction = null);

public sealed record ItemDefinition(
    int ItemTemplateId,
    string InternalName,
    string DisplayName,
    string LocalizationKey,
    ItemCategory ItemCategory,
    StackPolicy StackPolicy,
    int MaximumStack,
    BindPolicy BindPolicy,
    TradePolicy TradePolicy,
    SellPolicy SellPolicy,
    long BaseBuyPrice,
    long BaseSellPrice,
    string CurrencyType,
    string EquipmentCategory,
    string ConsumableCategory,
    bool QuestItemFlag,
    bool Enabled,
    string ContentVersion,
    string RawMetadata,
    string Source,
    bool? Usable = null,
    bool? NormalUse = null,
    bool? BattleUse = null,
    bool? Equippable = null,
    bool? UseOnOther = null,
    int? RequiredLevel = null,
    string? ClassRestriction = null,
    int? MinimumRebirth = null,
    string? GenderRestriction = null)
{
    public bool IsStackable => StackPolicy == StackPolicy.Stackable && MaximumStack > 1;
}

public sealed record ValidatedItemCatalog(
    ItemDefinitionCatalog Catalog,
    IReadOnlyList<ItemDefinition> Quarantined,
    IReadOnlyList<GameplayContentIssue> Issues);

public sealed record GameplayCoreContentCatalog(
    ItemDefinitionCatalog ItemCatalog,
    MerchantDefinitionCatalog MerchantCatalog,
    IReadOnlyList<GameplayContentIssue> Issues,
    IReadOnlyList<ItemDefinition> QuarantinedItems,
    ItemEffectDefinitionCatalog ItemEffectCatalog);

public sealed class ItemContentMapper
{
    public ItemDefinition Map(ItemDatabaseRecord record)
    {
        var maximumStack = record.MaximumStack ?? 1;
        var stackPolicy = ParseEnum(record.StackPolicy, StackPolicy.Unknown);
        if (stackPolicy == StackPolicy.Unknown && record.MaximumStack is not null)
        {
            stackPolicy = maximumStack > 1 ? StackPolicy.Stackable : StackPolicy.Single;
        }

        return new ItemDefinition(
            record.ItemTemplateId,
            record.InternalName.Trim(),
            record.DisplayName.Trim(),
            string.IsNullOrWhiteSpace(record.LocalizationKey) ? record.InternalName.Trim() : record.LocalizationKey.Trim(),
            ParseEnum(record.ItemCategory, ItemCategory.Unknown),
            stackPolicy,
            maximumStack,
            ParseEnum(record.BindPolicy, BindPolicy.Unknown),
            ParseEnum(record.TradePolicy, TradePolicy.Unknown),
            ParseEnum(record.SellPolicy, record.QuestItemFlag ? SellPolicy.NotSellable : SellPolicy.Unknown),
            record.BaseBuyPrice ?? 0,
            record.BaseSellPrice ?? 0,
            string.IsNullOrWhiteSpace(record.CurrencyType) ? "Unknown" : record.CurrencyType.Trim(),
            string.IsNullOrWhiteSpace(record.EquipmentCategory) ? "Unknown" : record.EquipmentCategory.Trim(),
            string.IsNullOrWhiteSpace(record.ConsumableCategory) ? "Unknown" : record.ConsumableCategory.Trim(),
            record.QuestItemFlag,
            record.Enabled,
            string.IsNullOrWhiteSpace(record.ContentVersion) ? "runtime-item-content-v1" : record.ContentVersion.Trim(),
            string.IsNullOrWhiteSpace(record.RawMetadata) ? "{}" : record.RawMetadata,
            string.IsNullOrWhiteSpace(record.Source) ? "Unknown" : record.Source,
            record.Usable,
            record.NormalUse,
            record.BattleUse,
            record.Equippable,
            record.UseOnOther,
            record.RequiredLevel,
            string.IsNullOrWhiteSpace(record.ClassRestriction) ? null : record.ClassRestriction.Trim(),
            record.MinimumRebirth,
            string.IsNullOrWhiteSpace(record.GenderRestriction) ? null : record.GenderRestriction.Trim());
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

public sealed class ItemContentValidator
{
    public ValidatedItemCatalog Validate(
        IEnumerable<ItemDefinition> definitions,
        IEnumerable<MerchantItemDefinition>? merchantItems = null)
    {
        var materialized = definitions.ToArray();
        var duplicateIds = materialized
            .GroupBy(definition => definition.ItemTemplateId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var valid = new List<ItemDefinition>();
        var quarantined = new List<ItemDefinition>();
        var issues = new List<GameplayContentIssue>();

        foreach (var definition in materialized)
        {
            var before = issues.Count;
            if (duplicateIds.Contains(definition.ItemTemplateId))
            {
                issues.Add(Issue(GameplayContentSeverity.Fatal, "item.duplicate_template_id", definition, "Duplicate ItemTemplateId is not allowed."));
            }

            if (definition.ItemTemplateId <= 0 || string.IsNullOrWhiteSpace(definition.InternalName) || string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                issues.Add(Issue(GameplayContentSeverity.Fatal, "item.missing_required_field", definition, "Item template id, internal name, and display name are required."));
            }

            if (definition.MaximumStack <= 0)
            {
                issues.Add(Issue(GameplayContentSeverity.Fatal, "item.invalid_maximum_stack", definition, "MaximumStack must be greater than zero."));
            }

            if (definition.StackPolicy == StackPolicy.Single && definition.MaximumStack != 1)
            {
                issues.Add(Issue(GameplayContentSeverity.Error, "item.single_stack_policy_requires_one", definition, "Single stack policy requires MaximumStack = 1."));
            }

            if (definition.BaseBuyPrice < 0 || definition.BaseSellPrice < 0)
            {
                issues.Add(Issue(GameplayContentSeverity.Fatal, "item.negative_price", definition, "Prices cannot be negative."));
            }

            if (string.IsNullOrWhiteSpace(definition.CurrencyType))
            {
                issues.Add(Issue(GameplayContentSeverity.Error, "item.invalid_currency_type", definition, "CurrencyType cannot be blank."));
            }

            if (!Enum.IsDefined(definition.ItemCategory))
            {
                issues.Add(Issue(GameplayContentSeverity.Error, "item.invalid_category", definition, "ItemCategory must be a known boundary enum value."));
            }

            if (!definition.Enabled)
            {
                issues.Add(Issue(GameplayContentSeverity.Warning, "item.disabled", definition, "Disabled item is quarantined from the runtime catalog."));
            }

            try
            {
                checked
                {
                    _ = definition.MaximumStack + definition.BaseBuyPrice + definition.BaseSellPrice;
                }
            }
            catch (OverflowException)
            {
                issues.Add(Issue(GameplayContentSeverity.Fatal, "item.arithmetic_overflow_risk", definition, "Item numeric fields overflow checked arithmetic."));
            }

            if (issues.Count == before)
            {
                valid.Add(definition);
            }
            else
            {
                quarantined.Add(definition);
            }
        }

        var validIds = valid.Select(definition => definition.ItemTemplateId).ToHashSet();
        foreach (var merchantItem in merchantItems ?? Array.Empty<MerchantItemDefinition>())
        {
            if (!validIds.Contains(merchantItem.ItemTemplateId))
            {
                issues.Add(new GameplayContentIssue(
                    GameplayContentSeverity.Error,
                    "merchant_item.missing_item_definition",
                    merchantItem.ItemTemplateId,
                    "Merchant references an item that is absent from the validated item catalog.",
                    "merchant_items"));
            }

            if (merchantItem.Price < 0)
            {
                issues.Add(new GameplayContentIssue(
                    GameplayContentSeverity.Error,
                    "merchant_item.negative_price",
                    merchantItem.ItemTemplateId,
                    "Merchant item price cannot be negative.",
                    "merchant_items"));
            }
        }

        return new ValidatedItemCatalog(
            new ItemDefinitionCatalog(valid),
            new ReadOnlyCollection<ItemDefinition>(quarantined),
            new ReadOnlyCollection<GameplayContentIssue>(issues));
    }

    private static GameplayContentIssue Issue(GameplayContentSeverity severity, string code, ItemDefinition definition, string message) =>
        new(severity, code, definition.ItemTemplateId, message, definition.Source);
}

public sealed class ItemDefinitionCatalog
{
    private readonly IReadOnlyDictionary<int, ItemDefinition> _definitions;

    public ItemDefinitionCatalog(IEnumerable<ItemDefinition> definitions)
    {
        _definitions = new ReadOnlyDictionary<int, ItemDefinition>(
            definitions.ToDictionary(definition => definition.ItemTemplateId));
    }

    public IReadOnlyDictionary<int, ItemDefinition> Definitions => _definitions;

    public OperationResult<ItemDefinition> Resolve(int itemTemplateId) =>
        _definitions.TryGetValue(itemTemplateId, out var definition) && definition.Enabled
            ? OperationResult<ItemDefinition>.Success(definition)
            : OperationResult<ItemDefinition>.Failure("inventory.item_invalid", "Item definition is missing or disabled.", itemTemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public sealed record InventorySlot(
    int SlotIndex,
    long PersistentInventoryItemId,
    long RuntimeInventoryObjectId,
    long ProtocolVisibleItemId,
    int ItemTemplateId,
    int Quantity,
    string BindState,
    string ItemInstanceMetadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record PlayerInventorySnapshot(
    Guid InventoryId,
    long CharacterId,
    int Capacity,
    long Version,
    long MutationSequence,
    string DirtyState,
    IReadOnlyList<InventorySlot> Slots);

public sealed class PlayerInventoryRuntime
{
    private readonly Dictionary<int, InventorySlot> _slots;
    private readonly ItemDefinitionCatalog _itemCatalog;

    public PlayerInventoryRuntime(
        Guid inventoryId,
        long characterId,
        int capacity,
        IEnumerable<InventorySlot>? slots = null,
        long version = 0,
        long mutationSequence = 0,
        string dirtyState = "Clean",
        ItemDefinitionCatalog? itemCatalog = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Inventory capacity must be positive.");
        }

        InventoryId = inventoryId;
        CharacterId = characterId;
        Capacity = capacity;
        Version = version;
        MutationSequence = mutationSequence;
        DirtyState = dirtyState;
        _itemCatalog = itemCatalog ?? new ItemDefinitionCatalog([]);
        _slots = (slots ?? Array.Empty<InventorySlot>()).ToDictionary(slot => slot.SlotIndex);
        InventoryInvariantValidator.ThrowIfInvalid(this, _itemCatalog);
    }

    public Guid InventoryId { get; }

    public long CharacterId { get; }

    public int Capacity { get; }

    public long Version { get; private set; }

    public long MutationSequence { get; private set; }

    public string DirtyState { get; private set; }

    public IReadOnlyDictionary<int, InventorySlot> Slots => new ReadOnlyDictionary<int, InventorySlot>(_slots);

    public int UsedSlots => _slots.Count;

    public int FreeSlots => Capacity - UsedSlots;

    public PlayerInventorySnapshot Snapshot() =>
        new(
            InventoryId,
            CharacterId,
            Capacity,
            Version,
            MutationSequence,
            DirtyState,
            _slots.Values.OrderBy(slot => slot.SlotIndex).ToArray());

    public PlayerInventoryRuntime Clone() =>
        new(InventoryId, CharacterId, Capacity, _slots.Values, Version, MutationSequence, DirtyState, _itemCatalog);

    public void ReplaceWith(PlayerInventorySnapshot snapshot)
    {
        if (snapshot.InventoryId != InventoryId || snapshot.CharacterId != CharacterId || snapshot.Capacity != Capacity)
        {
            throw new InvalidOperationException("Inventory snapshot identity does not match the runtime instance.");
        }

        _slots.Clear();
        foreach (var slot in snapshot.Slots)
        {
            _slots[slot.SlotIndex] = slot;
        }

        Version = snapshot.Version;
        MutationSequence = snapshot.MutationSequence;
        DirtyState = snapshot.DirtyState;
    }

    public int? FindAvailableSlot() =>
        Enumerable.Range(0, Capacity).FirstOrDefault(index => !_slots.ContainsKey(index), -1) is var index && index >= 0 ? index : null;

    public int? FindStackCandidate(ItemDefinition definition) =>
        definition.IsStackable
            ? _slots.Values
                .Where(slot => slot.ItemTemplateId == definition.ItemTemplateId && slot.Quantity < definition.MaximumStack)
                .OrderBy(slot => slot.SlotIndex)
                .Select(slot => (int?)slot.SlotIndex)
                .FirstOrDefault()
            : null;

    public OperationResult AddItem(ItemDefinition definition, int quantity, Func<long> inventoryItemIdFactory, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return OperationResult.Failure("inventory.quantity_invalid", "Quantity must be greater than zero.");
        }

        if (!definition.Enabled)
        {
            return OperationResult.Failure("inventory.item_invalid", "Cannot add a disabled item.");
        }

        try
        {
            checked
            {
                var remaining = quantity;
                if (definition.IsStackable)
                {
                    foreach (var slot in _slots.Values
                        .Where(slot => slot.ItemTemplateId == definition.ItemTemplateId && slot.Quantity < definition.MaximumStack)
                        .OrderBy(slot => slot.SlotIndex)
                        .ToArray())
                    {
                        var delta = Math.Min(remaining, definition.MaximumStack - slot.Quantity);
                        UpdateSlot(slot.SlotIndex, slot with
                        {
                            Quantity = slot.Quantity + delta,
                            UpdatedAtUtc = now,
                            Version = slot.Version + 1
                        });
                        remaining -= delta;
                        if (remaining == 0)
                        {
                            MarkMutated();
                            return OperationResult.Success;
                        }
                    }
                }

                while (remaining > 0)
                {
                    var slotIndex = FindAvailableSlot();
                    if (slotIndex is null)
                    {
                        return OperationResult.Failure("inventory.capacity_insufficient", "Inventory does not have enough free slots.");
                    }

                    var quantityForSlot = definition.IsStackable ? Math.Min(remaining, definition.MaximumStack) : 1;
                    var inventoryItemId = inventoryItemIdFactory();
                    _slots[slotIndex.Value] = new InventorySlot(
                        slotIndex.Value,
                        inventoryItemId,
                        RuntimeObjectIds.Static(RuntimeObjectKind.Item, checked((int)(inventoryItemId & 0x7FFFFFFF)), "InventoryRuntime").RuntimeObjectId,
                        0,
                        definition.ItemTemplateId,
                        quantityForSlot,
                        definition.BindPolicy.ToString(),
                        "{}",
                        now,
                        now,
                        1);
                    remaining -= quantityForSlot;
                }

                MarkMutated();
                return OperationResult.Success;
            }
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("inventory.overflow", "Inventory add would overflow checked arithmetic.");
        }
    }

    public OperationResult RemoveItem(int itemTemplateId, int quantity, ItemDefinitionCatalog catalog, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return OperationResult.Failure("inventory.quantity_invalid", "Quantity must be greater than zero.");
        }

        var definition = catalog.Resolve(itemTemplateId);
        if (!definition.Succeeded)
        {
            return OperationResult.Failure(definition.Error.Code, definition.Error.Message, definition.Error.Source);
        }

        var available = _slots.Values
            .Where(slot => slot.ItemTemplateId == itemTemplateId)
            .Sum(slot => slot.Quantity);
        if (available < quantity)
        {
            return OperationResult.Failure("inventory.quantity_insufficient", "Inventory does not contain enough quantity.");
        }

        var remaining = quantity;
        foreach (var slot in _slots.Values
            .Where(slot => slot.ItemTemplateId == itemTemplateId)
            .OrderByDescending(slot => slot.SlotIndex)
            .ToArray())
        {
            var delta = Math.Min(remaining, slot.Quantity);
            var nextQuantity = slot.Quantity - delta;
            if (nextQuantity == 0)
            {
                _slots.Remove(slot.SlotIndex);
            }
            else
            {
                UpdateSlot(slot.SlotIndex, slot with
                {
                    Quantity = nextQuantity,
                    UpdatedAtUtc = now,
                    Version = slot.Version + 1
                });
            }

            remaining -= delta;
            if (remaining == 0)
            {
                MarkMutated();
                return OperationResult.Success;
            }
        }

        return OperationResult.Failure("inventory.internal_remove_failed", "Inventory remove did not consume the requested quantity.");
    }

    public OperationResult Move(int sourceSlotIndex, int targetSlotIndex, DateTimeOffset now)
    {
        if (!IsValidSlot(sourceSlotIndex) || !IsValidSlot(targetSlotIndex))
        {
            return OperationResult.Failure("inventory.slot_invalid", "Source and target slots must be inside inventory capacity.");
        }

        if (!_slots.TryGetValue(sourceSlotIndex, out var source))
        {
            return OperationResult.Failure("inventory.source_slot_empty", "Source slot is empty.");
        }

        if (_slots.ContainsKey(targetSlotIndex))
        {
            return OperationResult.Failure("inventory.target_slot_occupied", "Target slot is occupied; use swap or merge.");
        }

        _slots.Remove(sourceSlotIndex);
        _slots[targetSlotIndex] = source with { SlotIndex = targetSlotIndex, UpdatedAtUtc = now, Version = source.Version + 1 };
        MarkMutated();
        return OperationResult.Success;
    }

    public OperationResult Swap(int firstSlotIndex, int secondSlotIndex, DateTimeOffset now)
    {
        if (!IsValidSlot(firstSlotIndex) || !IsValidSlot(secondSlotIndex))
        {
            return OperationResult.Failure("inventory.slot_invalid", "Swap slots must be inside inventory capacity.");
        }

        if (!_slots.TryGetValue(firstSlotIndex, out var first) || !_slots.TryGetValue(secondSlotIndex, out var second))
        {
            return OperationResult.Failure("inventory.swap_slot_empty", "Both slots must contain items for swap.");
        }

        _slots[firstSlotIndex] = second with { SlotIndex = firstSlotIndex, UpdatedAtUtc = now, Version = second.Version + 1 };
        _slots[secondSlotIndex] = first with { SlotIndex = secondSlotIndex, UpdatedAtUtc = now, Version = first.Version + 1 };
        MarkMutated();
        return OperationResult.Success;
    }

    public OperationResult Split(
        int sourceSlotIndex,
        int targetSlotIndex,
        int quantity,
        ItemDefinitionCatalog catalog,
        Func<long> inventoryItemIdFactory,
        DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return OperationResult.Failure("inventory.quantity_invalid", "Split quantity must be positive.");
        }

        if (!IsValidSlot(sourceSlotIndex) || !IsValidSlot(targetSlotIndex))
        {
            return OperationResult.Failure("inventory.slot_invalid", "Split slots must be inside inventory capacity.");
        }

        if (_slots.ContainsKey(targetSlotIndex))
        {
            return OperationResult.Failure("inventory.target_slot_occupied", "Split target slot must be empty.");
        }

        if (!_slots.TryGetValue(sourceSlotIndex, out var source))
        {
            return OperationResult.Failure("inventory.source_slot_empty", "Split source slot is empty.");
        }

        var definition = catalog.Resolve(source.ItemTemplateId);
        if (!definition.Succeeded || !definition.Value!.IsStackable)
        {
            return OperationResult.Failure("inventory.split_not_stackable", "Only stackable items can be split.");
        }

        if (source.Quantity <= quantity)
        {
            return OperationResult.Failure("inventory.quantity_insufficient", "Split quantity must be less than source quantity.");
        }

        var persistentInventoryItemId = inventoryItemIdFactory();
        _slots[sourceSlotIndex] = source with { Quantity = source.Quantity - quantity, UpdatedAtUtc = now, Version = source.Version + 1 };
        _slots[targetSlotIndex] = source with
        {
            SlotIndex = targetSlotIndex,
            PersistentInventoryItemId = persistentInventoryItemId,
            RuntimeInventoryObjectId = RuntimeObjectIds.Static(
                RuntimeObjectKind.Item,
                checked((int)(persistentInventoryItemId & 0x7FFFFFFF)),
                "InventoryRuntime").RuntimeObjectId,
            Quantity = quantity,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        MarkMutated();
        return OperationResult.Success;
    }

    public OperationResult Merge(int sourceSlotIndex, int targetSlotIndex, ItemDefinitionCatalog catalog, DateTimeOffset now)
    {
        if (!IsValidSlot(sourceSlotIndex) || !IsValidSlot(targetSlotIndex))
        {
            return OperationResult.Failure("inventory.slot_invalid", "Merge slots must be inside inventory capacity.");
        }

        if (!_slots.TryGetValue(sourceSlotIndex, out var source) || !_slots.TryGetValue(targetSlotIndex, out var target))
        {
            return OperationResult.Failure("inventory.merge_slot_empty", "Both merge slots must contain items.");
        }

        if (source.ItemTemplateId != target.ItemTemplateId)
        {
            return OperationResult.Failure("inventory.merge_item_mismatch", "Merge requires matching item templates.");
        }

        var definition = catalog.Resolve(target.ItemTemplateId);
        if (!definition.Succeeded || !definition.Value!.IsStackable)
        {
            return OperationResult.Failure("inventory.merge_not_stackable", "Only stackable items can be merged.");
        }

        try
        {
            checked
            {
                var total = source.Quantity + target.Quantity;
                if (total > definition.Value.MaximumStack)
                {
                    return OperationResult.Failure("inventory.stack_overflow", "Merge would exceed MaximumStack.");
                }

                _slots[targetSlotIndex] = target with { Quantity = total, UpdatedAtUtc = now, Version = target.Version + 1 };
                _slots.Remove(sourceSlotIndex);
                MarkMutated();
                return OperationResult.Success;
            }
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("inventory.overflow", "Merge would overflow checked arithmetic.");
        }
    }

    public OperationResult<InventorySlot> ExtractInstance(long persistentInventoryItemId, DateTimeOffset now)
    {
        var match = _slots.Values.SingleOrDefault(slot => slot.PersistentInventoryItemId == persistentInventoryItemId);
        if (match is null)
        {
            return OperationResult<InventorySlot>.Failure("inventory.instance_missing", "Inventory item instance was not found.");
        }

        if (match.Quantity != 1)
        {
            return OperationResult<InventorySlot>.Failure("inventory.instance_not_singular", "Only a singular item instance can be extracted.");
        }

        _slots.Remove(match.SlotIndex);
        MarkMutated();
        return OperationResult<InventorySlot>.Success(match with { UpdatedAtUtc = now, Version = match.Version + 1 });
    }

    public OperationResult InsertInstance(InventorySlot item, DateTimeOffset now)
    {
        if (item.Quantity != 1)
        {
            return OperationResult.Failure("inventory.instance_not_singular", "Only a singular item instance can be inserted.");
        }

        if (_slots.Values.Any(slot => slot.PersistentInventoryItemId == item.PersistentInventoryItemId))
        {
            return OperationResult.Failure("inventory.instance_duplicate", "Inventory item instance already exists.");
        }

        var slotIndex = FindAvailableSlot();
        if (slotIndex is null)
        {
            return OperationResult.Failure("inventory.capacity_insufficient", "Inventory does not have a free slot.");
        }

        _slots[slotIndex.Value] = item with
        {
            SlotIndex = slotIndex.Value,
            UpdatedAtUtc = now,
            Version = item.Version + 1
        };
        MarkMutated();
        return OperationResult.Success;
    }

    private bool IsValidSlot(int slotIndex) => slotIndex >= 0 && slotIndex < Capacity;

    private void UpdateSlot(int slotIndex, InventorySlot slot) => _slots[slotIndex] = slot;

    private void MarkMutated()
    {
        Version++;
        MutationSequence++;
        DirtyState = "Dirty";
    }

}

public static class InventoryInvariantValidator
{
    public static OperationResult Validate(PlayerInventoryRuntime runtime, ItemDefinitionCatalog catalog)
    {
        var seenSlots = new HashSet<int>();
        var seenItems = new HashSet<long>();
        foreach (var slot in runtime.Slots.Values)
        {
            if (!seenSlots.Add(slot.SlotIndex))
            {
                return OperationResult.Failure("inventory.invariant.duplicate_slot", "SlotIndex must not be duplicated.");
            }

            if (slot.SlotIndex < 0 || slot.SlotIndex >= runtime.Capacity)
            {
                return OperationResult.Failure("inventory.invariant.slot_out_of_range", "SlotIndex is outside inventory capacity.");
            }

            if (slot.Quantity <= 0)
            {
                return OperationResult.Failure("inventory.invariant.quantity_non_positive", "Quantity must be greater than zero.");
            }

            if (!seenItems.Add(slot.PersistentInventoryItemId))
            {
                return OperationResult.Failure("inventory.invariant.duplicate_persistent_item", "Persistent inventory item id appears in multiple slots.");
            }

            var definition = catalog.Resolve(slot.ItemTemplateId);
            if (!definition.Succeeded)
            {
                return OperationResult.Failure("inventory.invariant.item_definition_missing", "ItemDefinition must exist and be enabled.");
            }

            if (slot.Quantity > definition.Value!.MaximumStack)
            {
                return OperationResult.Failure("inventory.invariant.stack_overflow", "Slot quantity exceeds MaximumStack.");
            }

            if (!definition.Value.IsStackable && slot.Quantity != 1)
            {
                return OperationResult.Failure("inventory.invariant.non_stackable_quantity", "Non-stackable items must have quantity one.");
            }
        }

        return OperationResult.Success;
    }

    public static void ThrowIfInvalid(PlayerInventoryRuntime runtime, ItemDefinitionCatalog catalog)
    {
        var result = Validate(runtime, catalog);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{result.Error.Code}: {result.Error.Message}");
        }
    }
}

public sealed record CurrencyBalance(string CurrencyType, long Balance, long Version, string DirtyState);

public sealed record CurrencyWalletSnapshot(long CharacterId, IReadOnlyList<CurrencyBalance> Balances);

public sealed class CurrencyWalletRuntime
{
    private readonly Dictionary<string, CurrencyBalance> _balances;

    public CurrencyWalletRuntime(long characterId, IEnumerable<CurrencyBalance>? balances = null)
    {
        CharacterId = characterId;
        _balances = (balances ?? Array.Empty<CurrencyBalance>())
            .ToDictionary(balance => balance.CurrencyType, StringComparer.OrdinalIgnoreCase);
    }

    public long CharacterId { get; }

    public CurrencyWalletSnapshot Snapshot() =>
        new(CharacterId, _balances.Values.OrderBy(balance => balance.CurrencyType, StringComparer.OrdinalIgnoreCase).ToArray());

    public CurrencyWalletRuntime Clone() => new(CharacterId, _balances.Values);

    public long Query(string currencyType) =>
        _balances.TryGetValue(currencyType, out var balance) ? balance.Balance : 0;

    public OperationResult Debit(string currencyType, long amount)
    {
        if (amount <= 0)
        {
            return OperationResult.Failure("currency.amount_invalid", "Currency debit amount must be positive.");
        }

        var current = Query(currencyType);
        if (current < amount)
        {
            return OperationResult.Failure("currency.insufficient", "Insufficient currency balance.");
        }

        return Set(currencyType, checked(current - amount));
    }

    public OperationResult Credit(string currencyType, long amount)
    {
        if (amount <= 0)
        {
            return OperationResult.Failure("currency.amount_invalid", "Currency credit amount must be positive.");
        }

        try
        {
            return Set(currencyType, checked(Query(currencyType) + amount));
        }
        catch (OverflowException)
        {
            return OperationResult.Failure("currency.overflow", "Currency credit would overflow.");
        }
    }

    public void ReplaceWith(CurrencyWalletSnapshot snapshot)
    {
        if (snapshot.CharacterId != CharacterId)
        {
            throw new InvalidOperationException("Currency snapshot identity does not match the runtime wallet.");
        }

        _balances.Clear();
        foreach (var balance in snapshot.Balances)
        {
            _balances[balance.CurrencyType] = balance;
        }
    }

    private OperationResult Set(string currencyType, long balance)
    {
        if (balance < 0)
        {
            return OperationResult.Failure("currency.negative_balance", "Currency balance cannot become negative.");
        }

        _balances.TryGetValue(currencyType, out var current);
        _balances[currencyType] = new CurrencyBalance(
            currencyType,
            balance,
            (current?.Version ?? 0) + 1,
            "Dirty");
        return OperationResult.Success;
    }
}

public sealed record InventoryMutationRequest(
    int? SourceSlotIndex = null,
    int? TargetSlotIndex = null,
    long? InventoryItemId = null,
    int? ItemTemplateId = null,
    int Quantity = 0);

public sealed record InventoryTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    string SessionId,
    long? AccountId,
    InventoryOperationType OperationType,
    long ExpectedInventoryVersion,
    string Source,
    IReadOnlyList<InventoryMutationRequest> RequestedMutations,
    long RequestedCurrencyMutation,
    int? MerchantTemplateId,
    DateTimeOffset CreatedAtUtc,
    InventoryMutationAuthorityKind AuthorityKind = InventoryMutationAuthorityKind.PlayerSession);

public static class InventoryAuthorityPolicy
{
    public static OperationResult ValidateShape(InventoryTransactionRequest request)
    {
        if (request.CharacterId <= 0)
        {
            return OperationResult.Failure("inventory.authority_character_invalid", "Inventory authority requires a positive character identity.");
        }

        if (request.AuthorityKind == InventoryMutationAuthorityKind.PlayerSession)
        {
            if (request.AccountId is not > 0 || string.IsNullOrWhiteSpace(request.SessionId))
            {
                return OperationResult.Failure(
                    "inventory.session_authority_invalid",
                    "Player inventory mutations require an account and active session identity.");
            }

            return request.OperationType is InventoryOperationType.SystemGrant or InventoryOperationType.SystemRemove
                ? OperationResult.Failure(
                    "inventory.player_authority_operation_invalid",
                    "Player sessions cannot invoke trusted system inventory operations.")
                : OperationResult.Success;
        }

        if (request.OperationType is not InventoryOperationType.SystemGrant and not InventoryOperationType.SystemRemove)
        {
            return OperationResult.Failure(
                "inventory.trusted_authority_operation_invalid",
                "Trusted server authority is restricted to system grant and system remove operations.");
        }

        return request.AccountId is null
            ? OperationResult.Success
            : OperationResult.Failure(
                "inventory.trusted_authority_account_ambiguous",
                "Trusted server mutations must not carry a player-controlled account identity.");
    }
}

public sealed record InventoryTransactionResult(
    InventoryTransactionResultCode Code,
    Guid TransactionId,
    string IdempotencySafeId,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    long CurrencyBefore,
    long CurrencyAfter,
    string FailureCode = "",
    PlayerInventorySnapshot? InventorySnapshot = null,
    CurrencyWalletSnapshot? CurrencySnapshot = null)
{
    public bool Succeeded => Code is InventoryTransactionResultCode.Success or InventoryTransactionResultCode.DuplicateCompleted;
}

public sealed record InventoryAuditRecord(
    Guid AuditId,
    Guid TransactionId,
    string IdempotencySafeId,
    long CharacterId,
    string SessionId,
    InventoryOperationType OperationType,
    string Source,
    int? MerchantTemplateId,
    int? ItemTemplateId,
    long? InventoryItemId,
    int QuantityBefore,
    int QuantityAfter,
    string CurrencyType,
    long CurrencyBefore,
    long CurrencyAfter,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    InventoryTransactionResultCode Result,
    string FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string CorrelationId);

public interface IInventoryAuditLedger
{
    IReadOnlyList<InventoryAuditRecord> Records { get; }

    void Append(InventoryAuditRecord record);
}

public sealed class InMemoryInventoryAuditLedger : IInventoryAuditLedger
{
    private readonly ConcurrentQueue<InventoryAuditRecord> _records = [];

    public IReadOnlyList<InventoryAuditRecord> Records => _records.ToArray();

    public void Append(InventoryAuditRecord record) => _records.Enqueue(record);
}

public enum InventoryFailureInjectionPoint
{
    None,
    DbFailureBeforeTransaction,
    DbFailureAfterInventoryWrite,
    DbFailureAfterCurrencyWrite,
    AuditWriteFailure,
    RuntimeCommitFailure,
    Deadlock,
    Timeout,
    VersionConflict,
    DuplicateRequest,
    DisconnectDuringCommit
}

public sealed class InventoryFailureInjection
{
    public InventoryFailureInjectionPoint Point { get; set; } = InventoryFailureInjectionPoint.None;
}

public sealed record InventoryPersistenceBundle(
    PlayerInventorySnapshot Inventory,
    CurrencyWalletSnapshot Currency);

public sealed record InventoryPersistenceCommit(
    InventoryTransactionRequest Request,
    PlayerInventorySnapshot InventoryBefore,
    PlayerInventorySnapshot InventoryAfter,
    CurrencyWalletSnapshot CurrencyBefore,
    CurrencyWalletSnapshot CurrencyAfter,
    InventoryAuditRecord AuditRecord,
    string PayloadHash);

public sealed record InventoryReplayLookup(
    bool Found,
    bool PayloadMatches,
    InventoryTransactionResult? Result);

public interface IInventoryPersistenceStore
{
    Task<OperationResult> ValidateAuthorityAsync(InventoryTransactionRequest request, CancellationToken cancellationToken);

    Task<InventoryPersistenceBundle> LoadAsync(long characterId, CancellationToken cancellationToken);

    Task<InventoryReplayLookup> FindCompletedAsync(string idempotencyKey, string payloadHash, CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<long>>> ReservePersistentItemIdsAsync(int count, CancellationToken cancellationToken);

    Task<OperationResult> CommitAsync(InventoryPersistenceCommit commit, CancellationToken cancellationToken);
}

public sealed class InMemoryInventoryPersistenceStore : IInventoryPersistenceStore
{
    private readonly ConcurrentDictionary<long, InventoryPersistenceBundle> _bundles = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, InventoryTransactionResult Result)> _idempotency = [];
    private readonly ConcurrentDictionary<long, object> _commitGates = [];
    private long _nextPersistentItemId = 10_000;

    public InMemoryInventoryPersistenceStore(IInventoryAuditLedger auditLedger)
    {
        ArgumentNullException.ThrowIfNull(auditLedger);
    }

    public InventoryFailureInjection FailureInjection { get; } = new();

    public long NextPersistentItemId() => Interlocked.Increment(ref _nextPersistentItemId);

    public Task<OperationResult> ValidateAuthorityAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InventoryAuthorityPolicy.ValidateShape(request));
    }

    public Task<OperationResult<IReadOnlyList<long>>> ReservePersistentItemIdsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (count < 0)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<long>>.Failure(
                "inventory.identity_count_invalid",
                "Persistent inventory identity reservation count cannot be negative."));
        }

        IReadOnlyList<long> values = Enumerable.Range(0, count)
            .Select(_ => NextPersistentItemId())
            .ToArray();
        return Task.FromResult(OperationResult<IReadOnlyList<long>>.Success(values));
    }

    public void Seed(InventoryPersistenceBundle bundle)
    {
        _bundles[bundle.Inventory.CharacterId] = bundle;
        var maximumItemId = bundle.Inventory.Slots.Select(slot => slot.PersistentInventoryItemId).DefaultIfEmpty(_nextPersistentItemId).Max();
        InterlockedExtensions.Max(ref _nextPersistentItemId, maximumItemId);
    }

    public Task<InventoryPersistenceBundle> LoadAsync(long characterId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_bundles.GetOrAdd(
            characterId,
            id => new InventoryPersistenceBundle(
                new PlayerInventorySnapshot(Guid.NewGuid(), id, 32, 0, 0, "Clean", []),
                new CurrencyWalletSnapshot(id, [new CurrencyBalance("Gold", 0, 0, "Clean")]))));
    }

    public Task<InventoryReplayLookup> FindCompletedAsync(string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_idempotency.TryGetValue(idempotencyKey, out var existing))
        {
            return Task.FromResult(new InventoryReplayLookup(false, true, null));
        }

        return Task.FromResult(new InventoryReplayLookup(
            true,
            string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal),
            existing.Result));
    }

    public Task<OperationResult> CommitAsync(InventoryPersistenceCommit commit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point is InventoryFailureInjectionPoint.DbFailureBeforeTransaction or InventoryFailureInjectionPoint.Deadlock or InventoryFailureInjectionPoint.Timeout or InventoryFailureInjectionPoint.DisconnectDuringCommit)
        {
            return Task.FromResult(OperationResult.Failure("inventory.persistence_failure", $"Injected failure: {FailureInjection.Point}."));
        }

        lock (_commitGates.GetOrAdd(commit.Request.CharacterId, static _ => new object()))
        {
            if (_idempotency.TryGetValue(commit.Request.IdempotencyKey, out var existing))
            {
                if (!string.Equals(existing.PayloadHash, commit.PayloadHash, StringComparison.Ordinal))
                {
                    return Task.FromResult(OperationResult.Failure("inventory.replay_conflict", "Idempotency key was reused with a different payload."));
                }

                return Task.FromResult(OperationResult.Failure("inventory.replay_completed", "The inventory transaction was already committed."));
            }

            if (_bundles.TryGetValue(commit.Request.CharacterId, out var current) &&
                (current.Inventory.InventoryId != commit.InventoryBefore.InventoryId ||
                 current.Inventory.Version != commit.InventoryBefore.Version ||
                 !CurrencySnapshotsMatch(current.Currency, commit.CurrencyBefore)))
            {
                return Task.FromResult(OperationResult.Failure("inventory.version_conflict", "The persisted inventory changed before commit."));
            }

            if (FailureInjection.Point == InventoryFailureInjectionPoint.DbFailureAfterInventoryWrite)
            {
                return Task.FromResult(OperationResult.Failure("inventory.persistence_failure_after_inventory", "Injected failure after inventory write."));
            }

            if (FailureInjection.Point == InventoryFailureInjectionPoint.DbFailureAfterCurrencyWrite)
            {
                return Task.FromResult(OperationResult.Failure("inventory.persistence_failure_after_currency", "Injected failure after currency write."));
            }

            if (FailureInjection.Point == InventoryFailureInjectionPoint.AuditWriteFailure)
            {
                return Task.FromResult(OperationResult.Failure("inventory.audit_write_failed", "Injected audit write failure."));
            }

            _bundles[commit.Request.CharacterId] = new InventoryPersistenceBundle(commit.InventoryAfter, commit.CurrencyAfter);
            _idempotency[commit.Request.IdempotencyKey] = (
                commit.PayloadHash,
                new InventoryTransactionResult(
                    InventoryTransactionResultCode.Success,
                    commit.Request.TransactionId,
                    commit.AuditRecord.IdempotencySafeId,
                    commit.InventoryBefore.Version,
                    commit.InventoryAfter.Version,
                    Balance(commit.CurrencyBefore, commit.AuditRecord.CurrencyType),
                    Balance(commit.CurrencyAfter, commit.AuditRecord.CurrencyType),
                    InventorySnapshot: commit.InventoryAfter,
                    CurrencySnapshot: commit.CurrencyAfter));
            return Task.FromResult(OperationResult.Success);
        }
    }

    private static bool CurrencySnapshotsMatch(CurrencyWalletSnapshot current, CurrencyWalletSnapshot expected) =>
        current.CharacterId == expected.CharacterId &&
        current.Balances.OrderBy(value => value.CurrencyType, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                expected.Balances.OrderBy(value => value.CurrencyType, StringComparer.OrdinalIgnoreCase));

    private static long Balance(CurrencyWalletSnapshot snapshot, string currencyType) =>
        snapshot.Balances.FirstOrDefault(balance => string.Equals(balance.CurrencyType, currencyType, StringComparison.OrdinalIgnoreCase))?.Balance ?? 0;
}

file static class InterlockedExtensions
{
    public static void Max(ref long location, long candidate)
    {
        var current = Volatile.Read(ref location);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref location, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

public sealed class MerchantDefinitionCatalog
{
    private readonly IReadOnlyDictionary<int, MerchantMapping> _merchants;

    public MerchantDefinitionCatalog(IEnumerable<MerchantMapping> merchants)
    {
        _merchants = new ReadOnlyDictionary<int, MerchantMapping>(
            merchants.ToDictionary(merchant => merchant.MerchantTemplateId));
    }

    public IReadOnlyList<MerchantMapping> Merchants => _merchants.Values.ToArray();

    public OperationResult<MerchantMapping> Resolve(int merchantTemplateId) =>
        _merchants.TryGetValue(merchantTemplateId, out var merchant) && merchant.Enabled
            ? OperationResult<MerchantMapping>.Success(merchant)
            : OperationResult<MerchantMapping>.Failure("merchant.invalid", "Merchant is missing or disabled.", merchantTemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public interface IInventoryTransactionCoordinator : ICharacterInventoryProjectionSource
{
    IReadOnlyList<InventoryTransactionRuntimeEvent> Events { get; }

    Task<OperationResult<long>> GetCurrentVersionAsync(long characterId, CancellationToken cancellationToken);

    Task<InventoryTransactionResult> ExecuteAsync(InventoryTransactionRequest request, CancellationToken cancellationToken);

    InventoryInspectorSnapshot CaptureInspector(InventoryInspectorQuery query);
}

public sealed class InventoryTransactionCoordinator : IInventoryTransactionCoordinator
{
    private readonly ItemDefinitionCatalog _itemCatalog;
    private readonly MerchantDefinitionCatalog _merchantCatalog;
    private readonly IInventoryPersistenceStore _persistence;
    private readonly IInventoryAuditLedger _auditLedger;
    private readonly AsyncKeyedLock<long> _locks = new();
    private readonly ConcurrentDictionary<long, PlayerInventoryRuntime> _inventories = [];
    private readonly ConcurrentDictionary<long, CurrencyWalletRuntime> _wallets = [];
    private readonly Func<long>? _persistentItemIdFactoryOverride;
    private readonly InventoryFailureInjection _failureInjection;

    public InventoryTransactionCoordinator(
        ItemDefinitionCatalog itemCatalog,
        MerchantDefinitionCatalog merchantCatalog,
        IInventoryPersistenceStore persistence,
        IInventoryAuditLedger auditLedger,
        Func<long>? persistentItemIdFactory = null,
        InventoryFailureInjection? failureInjection = null)
    {
        _itemCatalog = itemCatalog;
        _merchantCatalog = merchantCatalog;
        _persistence = persistence;
        _auditLedger = auditLedger;
        _persistentItemIdFactoryOverride = persistentItemIdFactory;
        _failureInjection = failureInjection ?? new InventoryFailureInjection();
    }

    public IReadOnlyList<InventoryTransactionRuntimeEvent> Events => _events.ToArray();

    private readonly ConcurrentQueue<InventoryTransactionRuntimeEvent> _events = [];

    public async Task<OperationResult<long>> GetCurrentVersionAsync(long characterId, CancellationToken cancellationToken)
    {
        try
        {
            using var gate = await _locks.AcquireAsync(characterId, cancellationToken);
            var loaded = await _persistence.LoadAsync(characterId, cancellationToken);
            RefreshWallet(loaded.Currency);
            return OperationResult<long>.Success(RefreshRuntime(loaded.Inventory).Version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return OperationResult<long>.Failure(
                "inventory.persistence_unavailable",
                "The authoritative inventory version could not be loaded.");
        }
    }

    public async Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var gate = await _locks.AcquireAsync(characterId, cancellationToken);
            var loaded = await _persistence.LoadAsync(characterId, cancellationToken);
            RefreshWallet(loaded.Currency);
            return OperationResult<PlayerInventorySnapshot>.Success(RefreshRuntime(loaded.Inventory).Snapshot());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return OperationResult<PlayerInventorySnapshot>.Failure(
                "inventory.persistence_unavailable",
                "The authoritative inventory snapshot could not be loaded.");
        }
    }

    public async Task<InventoryTransactionResult> ExecuteAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var failure = new InventoryTransactionResult(
                InventoryTransactionResultCode.PersistenceFailure,
                request.TransactionId,
                SafeId(request.IdempotencyKey),
                0,
                0,
                0,
                0,
                "inventory.persistence_unavailable");
            _events.Enqueue(new InventoryTransactionRuntimeEvent(
                request.TransactionId,
                request.CharacterId,
                request.OperationType,
                failure.Code,
                failure.FailureCode,
                DateTimeOffset.UtcNow));
            return failure;
        }
    }

    private async Task<InventoryTransactionResult> ExecuteCoreAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var payloadHash = PayloadHash(request);
        var safeId = SafeId(request.IdempotencyKey);
        var authority = await _persistence.ValidateAuthorityAsync(request, cancellationToken);
        if (!authority.Succeeded)
        {
            return new InventoryTransactionResult(
                InventoryTransactionResultCode.Rejected,
                request.TransactionId,
                safeId,
                0,
                0,
                0,
                0,
                authority.Error.Code);
        }

        var replay = await _persistence.FindCompletedAsync(request.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return new InventoryTransactionResult(
                InventoryTransactionResultCode.Conflict,
                request.TransactionId,
                safeId,
                0,
                0,
                0,
                0,
                "inventory.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return await HydrateReplayAsync(request, replay.Result, cancellationToken);
        }

        using var gate = await _locks.AcquireAsync(request.CharacterId, cancellationToken);
        try
        {
            replay = await _persistence.FindCompletedAsync(request.IdempotencyKey, payloadHash, cancellationToken);
            if (replay is { Found: true, PayloadMatches: false })
            {
                return new InventoryTransactionResult(
                    InventoryTransactionResultCode.Conflict,
                    request.TransactionId,
                    safeId,
                    0,
                    0,
                    0,
                    0,
                    "inventory.replay_conflict");
            }

            if (replay is { Found: true, Result: not null })
            {
                return await HydrateReplayAsync(request, replay.Result, cancellationToken);
            }

            var loaded = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
            var runtime = RefreshRuntime(loaded.Inventory);
            var wallet = RefreshWallet(loaded.Currency);
            if (request.ExpectedInventoryVersion != runtime.Version)
            {
                return Reject(request, safeId, runtime, wallet, InventoryTransactionResultCode.VersionConflict, "inventory.version_conflict");
            }

            var beforeInventory = runtime.Snapshot();
            var beforeCurrency = wallet.Snapshot();
            var workingInventory = runtime.Clone();
            var workingWallet = wallet.Clone();
            var provisionalItemId = 0L;
            var operation = ApplyOperation(
                request,
                workingInventory,
                workingWallet,
                () => --provisionalItemId,
                out var currencyType,
                out var itemTemplateId);
            if (!operation.Succeeded)
            {
                var code = MapFailure(operation.Error.Code);
                var rejected = Result(request, safeId, beforeInventory, beforeInventory, beforeCurrency, beforeCurrency, currencyType, code, operation.Error.Code);
                AppendAudit(request, rejected, itemTemplateId, currencyType, beforeInventory, beforeInventory, beforeCurrency, beforeCurrency);
                _events.Enqueue(new InventoryTransactionRuntimeEvent(request.TransactionId, request.CharacterId, request.OperationType, code, operation.Error.Code, DateTimeOffset.UtcNow));
                return rejected;
            }

            var requiredPersistentItemIds = checked((int)-provisionalItemId);
            if (requiredPersistentItemIds > 0)
            {
                var reserved = await ReservePersistentItemIdsAsync(requiredPersistentItemIds, cancellationToken);
                if (!reserved.Succeeded || reserved.Value is null)
                {
                    return Reject(
                        request,
                        safeId,
                        runtime,
                        wallet,
                        InventoryTransactionResultCode.PersistenceFailure,
                        reserved.Error.Code);
                }

                var queue = new Queue<long>(reserved.Value);
                workingInventory = runtime.Clone();
                workingWallet = wallet.Clone();
                operation = ApplyOperation(
                    request,
                    workingInventory,
                    workingWallet,
                    () => queue.Dequeue(),
                    out currencyType,
                    out itemTemplateId);
                if (!operation.Succeeded || queue.Count != 0)
                {
                    return Reject(
                        request,
                        safeId,
                        runtime,
                        wallet,
                        InventoryTransactionResultCode.InternalFailure,
                        "inventory.identity_reservation_mismatch");
                }
            }

            var invariant = InventoryInvariantValidator.Validate(workingInventory, _itemCatalog);
            if (!invariant.Succeeded)
            {
                return Reject(request, safeId, runtime, wallet, InventoryTransactionResultCode.Rejected, invariant.Error.Code);
            }

            var afterInventory = workingInventory.Snapshot();
            var afterCurrency = workingWallet.Snapshot();
            var success = Result(request, safeId, beforeInventory, afterInventory, beforeCurrency, afterCurrency, currencyType, InventoryTransactionResultCode.Success, "");
            var audit = BuildAudit(request, success, itemTemplateId, currencyType, beforeInventory, afterInventory, beforeCurrency, afterCurrency);
            var persisted = await _persistence.CommitAsync(
                new InventoryPersistenceCommit(request, beforeInventory, afterInventory, beforeCurrency, afterCurrency, audit, payloadHash),
                cancellationToken);

            if (!persisted.Succeeded)
            {
                if (string.Equals(persisted.Error.Code, "inventory.replay_completed", StringComparison.Ordinal))
                {
                    var completedReplay = await _persistence.FindCompletedAsync(request.IdempotencyKey, payloadHash, cancellationToken);
                    if (completedReplay is { Found: true, PayloadMatches: true, Result: not null })
                    {
                        return await HydrateReplayAsync(request, completedReplay.Result, cancellationToken);
                    }
                }

                var reloaded = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
                var currentRuntime = RefreshRuntime(reloaded.Inventory);
                var currentWallet = RefreshWallet(reloaded.Currency);
                var currentInventorySnapshot = currentRuntime.Snapshot();
                var currentCurrencySnapshot = currentWallet.Snapshot();
                var failureCode = persisted.Error.Code;
                var resultCode = failureCode switch
                {
                    "inventory.version_conflict" => InventoryTransactionResultCode.VersionConflict,
                    "inventory.replay_conflict" => InventoryTransactionResultCode.Conflict,
                    _ => InventoryTransactionResultCode.PersistenceFailure
                };
                var failure = Result(
                    request,
                    safeId,
                    currentInventorySnapshot,
                    currentInventorySnapshot,
                    currentCurrencySnapshot,
                    currentCurrencySnapshot,
                    currencyType,
                    resultCode,
                    failureCode);
                AppendAudit(request, failure, itemTemplateId, currencyType, currentInventorySnapshot, currentInventorySnapshot, currentCurrencySnapshot, currentCurrencySnapshot);
                return failure;
            }

            _auditLedger.Append(audit);

            if (_failureInjection.Point == InventoryFailureInjectionPoint.RuntimeCommitFailure)
            {
                var reloaded = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
                runtime.ReplaceWith(reloaded.Inventory);
                wallet.ReplaceWith(reloaded.Currency);
                var failure = Result(
                    request,
                    safeId,
                    beforeInventory,
                    reloaded.Inventory,
                    beforeCurrency,
                    reloaded.Currency,
                    currencyType,
                    InventoryTransactionResultCode.InternalFailure,
                    "inventory.runtime_commit_failed_reloaded");
                AppendAudit(request, failure, itemTemplateId, currencyType, beforeInventory, reloaded.Inventory, beforeCurrency, reloaded.Currency);
                _events.Enqueue(new InventoryTransactionRuntimeEvent(request.TransactionId, request.CharacterId, request.OperationType, InventoryTransactionResultCode.InternalFailure, failure.FailureCode, DateTimeOffset.UtcNow));
                return failure;
            }

            runtime.ReplaceWith(afterInventory);
            wallet.ReplaceWith(afterCurrency);
            _events.Enqueue(new InventoryTransactionRuntimeEvent(request.TransactionId, request.CharacterId, request.OperationType, InventoryTransactionResultCode.Success, "", DateTimeOffset.UtcNow));
            return success;
        }
        catch (OverflowException)
        {
            return new InventoryTransactionResult(
                InventoryTransactionResultCode.InvalidPrice,
                request.TransactionId,
                safeId,
                0,
                0,
                0,
                0,
                "inventory.overflow");
        }
    }

    public InventoryInspectorSnapshot CaptureInspector(InventoryInspectorQuery query) =>
        InventoryRuntimeInspector.Capture(_inventories.Values, _wallets.Values, _auditLedger.Records, query, _merchantCatalog.Merchants, _itemCatalog);

    private async Task<OperationResult<IReadOnlyList<long>>> ReservePersistentItemIdsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        if (_persistentItemIdFactoryOverride is not null)
        {
            IReadOnlyList<long> values = Enumerable.Range(0, count)
                .Select(_ => _persistentItemIdFactoryOverride())
                .ToArray();
            return OperationResult<IReadOnlyList<long>>.Success(values);
        }

        return await _persistence.ReservePersistentItemIdsAsync(count, cancellationToken);
    }

    private async Task<InventoryTransactionResult> HydrateReplayAsync(
        InventoryTransactionRequest request,
        InventoryTransactionResult replay,
        CancellationToken cancellationToken)
    {
        var authoritative = await _persistence.LoadAsync(request.CharacterId, cancellationToken);
        var inventory = RefreshRuntime(authoritative.Inventory).Snapshot();
        var currency = RefreshWallet(authoritative.Currency).Snapshot();
        var currencyType = replay.CurrencySnapshot?.Balances
            .FirstOrDefault()?.CurrencyType ?? "Gold";
        return replay with
        {
            Code = InventoryTransactionResultCode.DuplicateCompleted,
            InventoryVersionAfter = inventory.Version,
            CurrencyAfter = Balance(currency, currencyType),
            InventorySnapshot = inventory,
            CurrencySnapshot = currency
        };
    }

    private PlayerInventoryRuntime RefreshRuntime(PlayerInventorySnapshot snapshot) =>
        _inventories.AddOrUpdate(
            snapshot.CharacterId,
            _ => NewRuntime(snapshot),
            (_, existing) =>
            {
                if (existing.InventoryId != snapshot.InventoryId || existing.Capacity != snapshot.Capacity)
                {
                    return NewRuntime(snapshot);
                }

                existing.ReplaceWith(snapshot);
                return existing;
            });

    private PlayerInventoryRuntime NewRuntime(PlayerInventorySnapshot snapshot) =>
        new(
            snapshot.InventoryId,
            snapshot.CharacterId,
            snapshot.Capacity,
            snapshot.Slots,
            snapshot.Version,
            snapshot.MutationSequence,
            snapshot.DirtyState,
            _itemCatalog);

    private CurrencyWalletRuntime RefreshWallet(CurrencyWalletSnapshot snapshot) =>
        _wallets.AddOrUpdate(
            snapshot.CharacterId,
            _ => new CurrencyWalletRuntime(snapshot.CharacterId, snapshot.Balances),
            (_, existing) =>
            {
                existing.ReplaceWith(snapshot);
                return existing;
            });

    private OperationResult ApplyOperation(
        InventoryTransactionRequest request,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        Func<long> persistentItemIdFactory,
        out string currencyType,
        out int? itemTemplateId)
    {
        currencyType = "Gold";
        itemTemplateId = request.RequestedMutations.FirstOrDefault()?.ItemTemplateId;
        var mutation = request.RequestedMutations.FirstOrDefault();
        return request.OperationType switch
        {
            InventoryOperationType.AddItem =>
                Add(request, mutation, inventory, persistentItemIdFactory, out itemTemplateId),
            InventoryOperationType.SystemGrant =>
                SystemGrant(request, inventory, wallet, persistentItemIdFactory, out currencyType, out itemTemplateId),
            InventoryOperationType.RemoveItem or InventoryOperationType.SystemRemove =>
                Remove(mutation, inventory, out itemTemplateId),
            InventoryOperationType.MoveItem =>
                inventory.Move(mutation?.SourceSlotIndex ?? -1, mutation?.TargetSlotIndex ?? -1, request.CreatedAtUtc),
            InventoryOperationType.SwapItems =>
                inventory.Swap(mutation?.SourceSlotIndex ?? -1, mutation?.TargetSlotIndex ?? -1, request.CreatedAtUtc),
            InventoryOperationType.SplitStack =>
                inventory.Split(
                    mutation?.SourceSlotIndex ?? -1,
                    mutation?.TargetSlotIndex ?? -1,
                    mutation?.Quantity ?? 0,
                    _itemCatalog,
                    persistentItemIdFactory,
                    request.CreatedAtUtc),
            InventoryOperationType.MergeStacks =>
                inventory.Merge(mutation?.SourceSlotIndex ?? -1, mutation?.TargetSlotIndex ?? -1, _itemCatalog, request.CreatedAtUtc),
            InventoryOperationType.MerchantBuy =>
                MerchantBuy(request, mutation, inventory, wallet, persistentItemIdFactory, out currencyType, out itemTemplateId),
            InventoryOperationType.MerchantSell =>
                MerchantSell(request, mutation, inventory, wallet, out currencyType, out itemTemplateId),
            _ => OperationResult.Failure("inventory.operation_unsupported", "Inventory operation is not supported.")
        };
    }

    private OperationResult Add(
        InventoryTransactionRequest request,
        InventoryMutationRequest? mutation,
        PlayerInventoryRuntime inventory,
        Func<long> persistentItemIdFactory,
        out int? itemTemplateId)
    {
        itemTemplateId = mutation?.ItemTemplateId;
        if (mutation?.ItemTemplateId is null)
        {
            return OperationResult.Failure("inventory.item_invalid", "AddItem requires ItemTemplateId.");
        }

        var definition = _itemCatalog.Resolve(mutation.ItemTemplateId.Value);
        return !definition.Succeeded
            ? OperationResult.Failure(definition.Error.Code, definition.Error.Message, definition.Error.Source)
            : inventory.AddItem(definition.Value!, mutation.Quantity, persistentItemIdFactory, request.CreatedAtUtc);
    }

    private OperationResult SystemGrant(
        InventoryTransactionRequest request,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        Func<long> persistentItemIdFactory,
        out string currencyType,
        out int? itemTemplateId)
    {
        currencyType = "Gold";
        itemTemplateId = request.RequestedMutations.FirstOrDefault()?.ItemTemplateId;
        if (request.RequestedMutations.Count == 0 && request.RequestedCurrencyMutation <= 0)
        {
            return OperationResult.Failure("inventory.grant_empty", "SystemGrant requires at least one item or currency grant.");
        }

        if (request.RequestedCurrencyMutation < 0)
        {
            return OperationResult.Failure("currency.negative_grant", "SystemGrant currency amount cannot be negative.");
        }

        foreach (var grant in request.RequestedMutations)
        {
            if (grant.ItemTemplateId is null || grant.Quantity <= 0)
            {
                return OperationResult.Failure("inventory.item_invalid", "SystemGrant item entries require an item template and positive quantity.");
            }

            var definition = _itemCatalog.Resolve(grant.ItemTemplateId.Value);
            if (!definition.Succeeded || definition.Value is null)
            {
                return OperationResult.Failure(definition.Error.Code, definition.Error.Message, definition.Error.Source);
            }

            var add = inventory.AddItem(definition.Value, grant.Quantity, persistentItemIdFactory, request.CreatedAtUtc);
            if (!add.Succeeded)
            {
                return add;
            }
        }

        return request.RequestedCurrencyMutation == 0
            ? OperationResult.Success
            : wallet.Credit(currencyType, request.RequestedCurrencyMutation);
    }

    private OperationResult Remove(InventoryMutationRequest? mutation, PlayerInventoryRuntime inventory, out int? itemTemplateId)
    {
        itemTemplateId = mutation?.ItemTemplateId;
        return mutation?.ItemTemplateId is null
            ? OperationResult.Failure("inventory.item_invalid", "RemoveItem requires ItemTemplateId.")
            : inventory.RemoveItem(mutation.ItemTemplateId.Value, mutation.Quantity, _itemCatalog, DateTimeOffset.UtcNow);
    }

    private OperationResult MerchantBuy(
        InventoryTransactionRequest request,
        InventoryMutationRequest? mutation,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        Func<long> persistentItemIdFactory,
        out string currencyType,
        out int? itemTemplateId)
    {
        currencyType = "Gold";
        itemTemplateId = mutation?.ItemTemplateId;
        if (request.MerchantTemplateId is null)
        {
            return OperationResult.Failure("merchant.invalid", "MerchantBuy requires MerchantTemplateId.");
        }

        var merchant = _merchantCatalog.Resolve(request.MerchantTemplateId.Value);
        if (!merchant.Succeeded)
        {
            return OperationResult.Failure(merchant.Error.Code, merchant.Error.Message, merchant.Error.Source);
        }

        if (mutation?.ItemTemplateId is null || mutation.Quantity <= 0)
        {
            return OperationResult.Failure("inventory.quantity_invalid", "MerchantBuy requires a valid item and positive quantity.");
        }

        var item = _itemCatalog.Resolve(mutation.ItemTemplateId.Value);
        if (!item.Succeeded)
        {
            return OperationResult.Failure(item.Error.Code, item.Error.Message, item.Error.Source);
        }

        var merchantItem = merchant.Value!.RuntimeItems.FirstOrDefault(entry => entry.ItemTemplateId == mutation.ItemTemplateId.Value);
        if (merchantItem is null)
        {
            return OperationResult.Failure("merchant.item_missing", "Merchant does not sell the requested item.");
        }

        currencyType = item.Value!.CurrencyType == "Unknown" ? merchant.Value.CurrencyType : item.Value.CurrencyType;
        var total = checked(merchantItem.Price * mutation.Quantity);
        if (total <= 0)
        {
            return OperationResult.Failure("merchant.price_invalid", "Merchant buy price must be positive.");
        }

        var debit = wallet.Debit(currencyType, total);
        if (!debit.Succeeded)
        {
            return debit;
        }

        return inventory.AddItem(item.Value, mutation.Quantity, persistentItemIdFactory, request.CreatedAtUtc);
    }

    private OperationResult MerchantSell(
        InventoryTransactionRequest request,
        InventoryMutationRequest? mutation,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        out string currencyType,
        out int? itemTemplateId)
    {
        currencyType = "Gold";
        itemTemplateId = mutation?.ItemTemplateId;
        if (request.MerchantTemplateId is null)
        {
            return OperationResult.Failure("merchant.invalid", "MerchantSell requires MerchantTemplateId.");
        }

        var merchant = _merchantCatalog.Resolve(request.MerchantTemplateId.Value);
        if (!merchant.Succeeded)
        {
            return OperationResult.Failure(merchant.Error.Code, merchant.Error.Message, merchant.Error.Source);
        }

        if (mutation?.ItemTemplateId is null || mutation.Quantity <= 0)
        {
            return OperationResult.Failure("inventory.quantity_invalid", "MerchantSell requires a valid item and positive quantity.");
        }

        var item = _itemCatalog.Resolve(mutation.ItemTemplateId.Value);
        if (!item.Succeeded)
        {
            return OperationResult.Failure(item.Error.Code, item.Error.Message, item.Error.Source);
        }

        if (item.Value!.QuestItemFlag || item.Value.SellPolicy == SellPolicy.NotSellable)
        {
            return OperationResult.Failure("merchant.sell_forbidden", "Item sell policy forbids selling this item.");
        }

        currencyType = item.Value.CurrencyType == "Unknown" ? merchant.Value!.CurrencyType : item.Value.CurrencyType;
        var merchantItem = merchant.Value!.RuntimeItems.FirstOrDefault(entry =>
            entry.ItemTemplateId == mutation.ItemTemplateId.Value);
        var unitPrice = merchantItem?.PurchasingPrice ?? item.Value.BaseSellPrice;
        var total = checked(unitPrice * mutation.Quantity);
        if (total <= 0)
        {
            return OperationResult.Failure("merchant.price_invalid", "Merchant sell price must be positive.");
        }

        var remove = inventory.RemoveItem(item.Value.ItemTemplateId, mutation.Quantity, _itemCatalog, request.CreatedAtUtc);
        if (!remove.Succeeded)
        {
            return remove;
        }

        return wallet.Credit(currencyType, total);
    }

    private InventoryTransactionResult Reject(
        InventoryTransactionRequest request,
        string safeId,
        PlayerInventoryRuntime runtime,
        CurrencyWalletRuntime wallet,
        InventoryTransactionResultCode code,
        string failureCode)
    {
        var inventory = runtime.Snapshot();
        var currency = wallet.Snapshot();
        return Result(request, safeId, inventory, inventory, currency, currency, "Gold", code, failureCode);
    }

    private static InventoryTransactionResult Result(
        InventoryTransactionRequest request,
        string safeId,
        PlayerInventorySnapshot inventoryBefore,
        PlayerInventorySnapshot inventoryAfter,
        CurrencyWalletSnapshot currencyBefore,
        CurrencyWalletSnapshot currencyAfter,
        string currencyType,
        InventoryTransactionResultCode code,
        string failureCode) =>
        new(
            code,
            request.TransactionId,
            safeId,
            inventoryBefore.Version,
            inventoryAfter.Version,
            Balance(currencyBefore, currencyType),
            Balance(currencyAfter, currencyType),
            failureCode,
            inventoryAfter,
            currencyAfter);

    private void AppendAudit(
        InventoryTransactionRequest request,
        InventoryTransactionResult result,
        int? itemTemplateId,
        string currencyType,
        PlayerInventorySnapshot inventoryBefore,
        PlayerInventorySnapshot inventoryAfter,
        CurrencyWalletSnapshot currencyBefore,
        CurrencyWalletSnapshot currencyAfter) =>
        _auditLedger.Append(BuildAudit(request, result, itemTemplateId, currencyType, inventoryBefore, inventoryAfter, currencyBefore, currencyAfter));

    private static InventoryAuditRecord BuildAudit(
        InventoryTransactionRequest request,
        InventoryTransactionResult result,
        int? itemTemplateId,
        string currencyType,
        PlayerInventorySnapshot inventoryBefore,
        PlayerInventorySnapshot inventoryAfter,
        CurrencyWalletSnapshot currencyBefore,
        CurrencyWalletSnapshot currencyAfter)
    {
        var beforeQuantity = itemTemplateId is null ? 0 : inventoryBefore.Slots.Where(slot => slot.ItemTemplateId == itemTemplateId).Sum(slot => slot.Quantity);
        var afterQuantity = itemTemplateId is null ? 0 : inventoryAfter.Slots.Where(slot => slot.ItemTemplateId == itemTemplateId).Sum(slot => slot.Quantity);
        return new InventoryAuditRecord(
            Guid.NewGuid(),
            request.TransactionId,
            result.IdempotencySafeId,
            request.CharacterId,
            request.SessionId,
            request.OperationType,
            request.Source,
            request.MerchantTemplateId,
            itemTemplateId,
            inventoryAfter.Slots.FirstOrDefault(slot => itemTemplateId is not null && slot.ItemTemplateId == itemTemplateId)?.PersistentInventoryItemId,
            beforeQuantity,
            afterQuantity,
            currencyType,
            Balance(currencyBefore, currencyType),
            Balance(currencyAfter, currencyType),
            inventoryBefore.Version,
            inventoryAfter.Version,
            result.Code,
            result.FailureCode,
            request.CreatedAtUtc,
            DateTimeOffset.UtcNow,
            request.TransactionId.ToString("N"));
    }

    private static InventoryTransactionResultCode MapFailure(string failureCode) =>
        failureCode switch
        {
            "inventory.capacity_insufficient" => InventoryTransactionResultCode.InsufficientCapacity,
            "inventory.quantity_insufficient" => InventoryTransactionResultCode.InsufficientQuantity,
            "currency.insufficient" => InventoryTransactionResultCode.InsufficientCurrency,
            "inventory.item_invalid" or "inventory.split_not_stackable" or "inventory.merge_not_stackable" => InventoryTransactionResultCode.InvalidItem,
            "merchant.invalid" or "merchant.item_missing" => InventoryTransactionResultCode.InvalidMerchant,
            "merchant.price_invalid" or "inventory.overflow" or "currency.overflow" => InventoryTransactionResultCode.InvalidPrice,
            _ => InventoryTransactionResultCode.Rejected
        };

    private static long Balance(CurrencyWalletSnapshot snapshot, string currencyType) =>
        snapshot.Balances.FirstOrDefault(balance => string.Equals(balance.CurrencyType, currencyType, StringComparison.OrdinalIgnoreCase))?.Balance ?? 0;

    private static string PayloadHash(InventoryTransactionRequest request)
    {
        var text = string.Join(
            "|",
            request.CharacterId,
            request.AuthorityKind,
            request.AccountId,
            request.SessionId,
            request.Source,
            request.OperationType,
            request.ExpectedInventoryVersion,
            request.MerchantTemplateId,
            string.Join(",", request.RequestedMutations.Select(m => $"{m.SourceSlotIndex}:{m.TargetSlotIndex}:{m.InventoryItemId}:{m.ItemTemplateId}:{m.Quantity}")),
            request.RequestedCurrencyMutation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string SafeId(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant()[..16];
}

public sealed record InventoryTransactionRuntimeEvent(
    Guid TransactionId,
    long CharacterId,
    InventoryOperationType OperationType,
    InventoryTransactionResultCode Result,
    string FailureCode,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryInspectorQuery(
    long? CharacterId = null,
    int? ItemTemplateId = null,
    int? MerchantTemplateId = null,
    InventoryOperationType? OperationType = null,
    InventoryTransactionResultCode? Result = null,
    bool PendingOnly = false,
    bool FailedOnly = false,
    int Offset = 0,
    int Limit = 100);

public sealed record InventoryInspectorSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<InventorySummarySnapshot> Inventories,
    IReadOnlyList<InventoryItemSnapshot> Items,
    IReadOnlyList<InventoryTransactionSnapshot> Transactions,
    IReadOnlyList<InventoryMerchantSnapshot> Merchants);

public sealed record InventorySummarySnapshot(
    long CharacterId,
    int Capacity,
    int UsedSlots,
    int FreeSlots,
    long InventoryVersion,
    string DirtyState,
    int PendingTransactionCount,
    Guid? LastTransactionId,
    DateTimeOffset? LastMutationAtUtc);

public sealed record InventoryItemSnapshot(
    long CharacterId,
    int SlotIndex,
    long RuntimeObjectId,
    long PersistentInventoryItemId,
    int ItemTemplateId,
    int Quantity,
    int MaximumStack,
    string BindState,
    string ItemCategory,
    DateTimeOffset UpdatedAtUtc);

public sealed record InventoryTransactionSnapshot(
    Guid TransactionId,
    string SafeIdempotencyIdentifier,
    InventoryOperationType OperationType,
    string State,
    InventoryTransactionResultCode Result,
    long CharacterId,
    int? MerchantTemplateId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string FailureCode);

public sealed record InventoryMerchantSnapshot(
    int MerchantTemplateId,
    int BoundNpcTemplateId,
    int ItemCount,
    string CurrencyType,
    bool Enabled,
    MerchantStockPolicy StockPolicy,
    string EvidenceProtocolStatus);

public static class InventoryRuntimeInspector
{
    public static InventoryInspectorSnapshot Capture(
        IEnumerable<PlayerInventoryRuntime> inventories,
        IEnumerable<CurrencyWalletRuntime> wallets,
        IEnumerable<InventoryAuditRecord> transactions,
        InventoryInspectorQuery query,
        IEnumerable<MerchantMapping>? merchants = null,
        ItemDefinitionCatalog? itemCatalog = null)
    {
        var now = DateTimeOffset.UtcNow;
        var catalog = itemCatalog ?? new ItemDefinitionCatalog([]);
        var tx = transactions
            .Where(record => query.CharacterId is null || record.CharacterId == query.CharacterId)
            .Where(record => query.MerchantTemplateId is null || record.MerchantTemplateId == query.MerchantTemplateId)
            .Where(record => query.OperationType is null || record.OperationType == query.OperationType)
            .Where(record => query.Result is null || record.Result == query.Result)
            .Where(record => !query.FailedOnly || record.Result != InventoryTransactionResultCode.Success)
            .OrderByDescending(record => record.CompletedAtUtc)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToArray();

        var summaries = inventories
            .Where(inventory => query.CharacterId is null || inventory.CharacterId == query.CharacterId)
            .Select(inventory =>
            {
                var last = transactions
                    .Where(record => record.CharacterId == inventory.CharacterId)
                    .OrderByDescending(record => record.CompletedAtUtc)
                    .FirstOrDefault();
                return new InventorySummarySnapshot(
                    inventory.CharacterId,
                    inventory.Capacity,
                    inventory.UsedSlots,
                    inventory.FreeSlots,
                    inventory.Version,
                    inventory.DirtyState,
                    0,
                    last?.TransactionId,
                    last?.CompletedAtUtc);
            })
            .ToArray();

        var items = inventories
            .Where(inventory => query.CharacterId is null || inventory.CharacterId == query.CharacterId)
            .SelectMany(inventory => inventory.Slots.Values.Select(slot =>
            {
                var definition = catalog.Resolve(slot.ItemTemplateId);
                return new InventoryItemSnapshot(
                    inventory.CharacterId,
                    slot.SlotIndex,
                    slot.RuntimeInventoryObjectId,
                    slot.PersistentInventoryItemId,
                    slot.ItemTemplateId,
                    slot.Quantity,
                    definition.Value?.MaximumStack ?? 0,
                    slot.BindState,
                    definition.Value?.ItemCategory.ToString() ?? ItemCategory.Unknown.ToString(),
                    slot.UpdatedAtUtc);
            }))
            .Where(item => query.ItemTemplateId is null || item.ItemTemplateId == query.ItemTemplateId)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToArray();

        var merchantSnapshots = (merchants ?? Array.Empty<MerchantMapping>())
            .Where(merchant => query.MerchantTemplateId is null || merchant.MerchantTemplateId == query.MerchantTemplateId)
            .Select(merchant => new InventoryMerchantSnapshot(
                merchant.MerchantTemplateId,
                merchant.NpcTemplateId,
                merchant.RuntimeItems.Count,
                merchant.CurrencyType,
                merchant.Enabled,
                MerchantStockPolicy.Unlimited,
                "Merchant Client Protocol: SerializerBlockedByEvidence"))
            .ToArray();

        return new InventoryInspectorSnapshot(
            now,
            summaries,
            items,
            tx.Select(record => new InventoryTransactionSnapshot(
                record.TransactionId,
                record.IdempotencySafeId,
                record.OperationType,
                "Completed",
                record.Result,
                record.CharacterId,
                record.MerchantTemplateId,
                record.CreatedAtUtc,
                record.CompletedAtUtc,
                record.FailureCode)).ToArray(),
            merchantSnapshots);
    }
}
