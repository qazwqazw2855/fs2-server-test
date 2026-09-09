using System.Collections.ObjectModel;

namespace God2.ClassicServer.Runtime;

public enum EquipmentSlotType
{
    Weapon,
    Head,
    Body,
    Hands,
    Feet,
    Accessory,
    Mount
}

public sealed record EquipmentStatBonus(long MaximumHp, long MaximumMp, long Attack, long Defense, long Spirit, long Speed)
{
    public static EquipmentStatBonus Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public EquipmentStatBonus Add(EquipmentStatBonus other) =>
        new(
            checked(MaximumHp + other.MaximumHp),
            checked(MaximumMp + other.MaximumMp),
            checked(Attack + other.Attack),
            checked(Defense + other.Defense),
            checked(Spirit + other.Spirit),
            checked(Speed + other.Speed));
}

public sealed record EquipmentDefinition(
    int ItemTemplateId,
    EquipmentSlotType Slot,
    int MinimumLevel,
    IReadOnlySet<GameplayClass> AllowedClasses,
    EquipmentStatBonus Bonus,
    string EvidenceConfidence);

public sealed class EquipmentDefinitionCatalog
{
    private readonly IReadOnlyDictionary<int, EquipmentDefinition> _definitions;

    public EquipmentDefinitionCatalog(IEnumerable<EquipmentDefinition> definitions)
    {
        _definitions = new ReadOnlyDictionary<int, EquipmentDefinition>(
            definitions.ToDictionary(definition => definition.ItemTemplateId));
    }

    public bool TryResolve(int itemTemplateId, out EquipmentDefinition definition) =>
        _definitions.TryGetValue(itemTemplateId, out definition!);
}

public sealed record EquipmentSnapshot(
    long CharacterId,
    IReadOnlyDictionary<EquipmentSlotType, InventorySlot> Equipped,
    EquipmentStatBonus AggregateBonus,
    long Version);

public enum EquipmentResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidItem,
    WrongSlot,
    ClassRestricted,
    LevelRestricted,
    AlreadyEquipped,
    SlotEmpty,
    InventoryFull,
    PersistenceFailure
}

public sealed record EquipmentResult(
    EquipmentResultCode ResultCode,
    EquipmentSnapshot Snapshot,
    PlayerInventorySnapshot Inventory,
    bool Mutated);

public sealed class EquipmentRuntime
{
    private readonly object _sync = new();
    private readonly long _characterId;
    private readonly GameplayClass _class;
    private readonly EquipmentDefinitionCatalog _catalog;
    private readonly Dictionary<EquipmentSlotType, InventorySlot> _equipped;
    private readonly Dictionary<string, (string Fingerprint, EquipmentResult Result)> _completed = new(StringComparer.Ordinal);
    private long _version;

    public EquipmentRuntime(
        long characterId,
        GameplayClass @class,
        EquipmentDefinitionCatalog catalog,
        EquipmentSnapshot? restored = null)
    {
        _characterId = characterId;
        _class = @class;
        _catalog = catalog;
        if (restored is not null && restored.CharacterId != characterId)
        {
            throw new ArgumentException("Equipment snapshot identity does not match.", nameof(restored));
        }

        _equipped = restored?.Equipped.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        _version = restored?.Version ?? 0;
    }

    public EquipmentSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SnapshotCore();
            }
        }
    }

    public EquipmentResult Equip(
        string idempotencyKey,
        long persistentInventoryItemId,
        EquipmentSlotType requestedSlot,
        int characterLevel,
        PlayerInventoryRuntime inventory,
        DateTimeOffset now,
        bool failBeforeCommit = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"equip:{persistentInventoryItemId}:{requestedSlot}:{characterLevel}";
            if (TryReplay(idempotencyKey, fingerprint, inventory, out var replay))
            {
                return replay;
            }

            var item = inventory.Slots.Values.SingleOrDefault(value => value.PersistentInventoryItemId == persistentInventoryItemId);
            if (item is null || !_catalog.TryResolve(item.ItemTemplateId, out var definition))
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.InvalidItem, inventory, false);
            }

            if (definition.Slot != requestedSlot)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.WrongSlot, inventory, false);
            }

            if (!definition.AllowedClasses.Contains(_class))
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.ClassRestricted, inventory, false);
            }

            if (characterLevel < definition.MinimumLevel)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.LevelRestricted, inventory, false);
            }

            if (_equipped.TryGetValue(requestedSlot, out var current) && current.PersistentInventoryItemId == persistentInventoryItemId)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.AlreadyEquipped, inventory, false);
            }

            var working = inventory.Clone();
            var extracted = working.ExtractInstance(persistentInventoryItemId, now);
            if (!extracted.Succeeded)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.InvalidItem, inventory, false);
            }

            if (current is not null)
            {
                var inserted = working.InsertInstance(current, now);
                if (!inserted.Succeeded)
                {
                    return Complete(idempotencyKey, fingerprint, EquipmentResultCode.InventoryFull, inventory, false);
                }
            }

            if (failBeforeCommit)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.PersistenceFailure, inventory, false);
            }

            inventory.ReplaceWith(working.Snapshot());
            _equipped[requestedSlot] = extracted.Value!;
            _version++;
            return Complete(idempotencyKey, fingerprint, EquipmentResultCode.Success, inventory, true);
        }
    }

    public EquipmentResult Unequip(
        string idempotencyKey,
        EquipmentSlotType slot,
        PlayerInventoryRuntime inventory,
        DateTimeOffset now,
        bool failBeforeCommit = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"unequip:{slot}";
            if (TryReplay(idempotencyKey, fingerprint, inventory, out var replay))
            {
                return replay;
            }

            if (!_equipped.TryGetValue(slot, out var current))
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.SlotEmpty, inventory, false);
            }

            var working = inventory.Clone();
            var inserted = working.InsertInstance(current, now);
            if (!inserted.Succeeded)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.InventoryFull, inventory, false);
            }

            if (failBeforeCommit)
            {
                return Complete(idempotencyKey, fingerprint, EquipmentResultCode.PersistenceFailure, inventory, false);
            }

            inventory.ReplaceWith(working.Snapshot());
            _equipped.Remove(slot);
            _version++;
            return Complete(idempotencyKey, fingerprint, EquipmentResultCode.Success, inventory, true);
        }
    }

    private bool TryReplay(
        string key,
        string fingerprint,
        PlayerInventoryRuntime inventory,
        out EquipmentResult result)
    {
        if (_completed.TryGetValue(key, out var replay))
        {
            result = replay.Fingerprint == fingerprint
                ? replay.Result with { ResultCode = EquipmentResultCode.DuplicateCompleted, Mutated = false }
                : new EquipmentResult(EquipmentResultCode.ReplayConflict, SnapshotCore(), inventory.Snapshot(), false);
            return true;
        }

        result = null!;
        return false;
    }

    private EquipmentResult Complete(
        string key,
        string fingerprint,
        EquipmentResultCode code,
        PlayerInventoryRuntime inventory,
        bool mutated)
    {
        var result = new EquipmentResult(code, SnapshotCore(), inventory.Snapshot(), mutated);
        _completed[key] = (fingerprint, result);
        return result;
    }

    private EquipmentSnapshot SnapshotCore()
    {
        var aggregate = _equipped.Values.Aggregate(
            EquipmentStatBonus.Zero,
            (current, item) => _catalog.TryResolve(item.ItemTemplateId, out var definition)
                ? current.Add(definition.Bonus)
                : current);
        return new EquipmentSnapshot(
            _characterId,
            new ReadOnlyDictionary<EquipmentSlotType, InventorySlot>(new Dictionary<EquipmentSlotType, InventorySlot>(_equipped)),
            aggregate,
            _version);
    }
}

public sealed record CraftingIngredient(int ItemTemplateId, int Quantity);

public sealed record CraftingRecipe(
    int RecipeId,
    IReadOnlyList<CraftingIngredient> Ingredients,
    int ResultItemTemplateId,
    int ResultQuantity,
    string CurrencyType,
    long CurrencyCost,
    int? LinkedQuestId,
    bool Enabled,
    string EvidenceConfidence);

public sealed class CraftingRecipeCatalog
{
    private readonly IReadOnlyDictionary<int, CraftingRecipe> _recipes;

    public CraftingRecipeCatalog(IEnumerable<CraftingRecipe> recipes) =>
        _recipes = new ReadOnlyDictionary<int, CraftingRecipe>(recipes.ToDictionary(recipe => recipe.RecipeId));

    public bool TryResolve(int recipeId, out CraftingRecipe recipe) =>
        _recipes.TryGetValue(recipeId, out recipe!) && recipe.Enabled;
}

public enum CraftingResultCode
{
    Success,
    DuplicateCompleted,
    ReplayConflict,
    InvalidRecipe,
    InvalidQuantity,
    MissingMaterials,
    InsufficientCurrency,
    InventoryFull,
    PersistenceFailure
}

public sealed record CraftingResult(
    CraftingResultCode ResultCode,
    int CreatedQuantity,
    int? QuestProgressEventId,
    PlayerInventorySnapshot Inventory,
    CurrencyWalletSnapshot Wallet,
    bool Mutated);

public sealed class CraftingRuntime
{
    private readonly object _sync = new();
    private readonly CraftingRecipeCatalog _recipes;
    private readonly ItemDefinitionCatalog _items;
    private readonly Func<long> _itemIdFactory;
    private readonly Dictionary<string, (string Fingerprint, CraftingResult Result)> _completed = new(StringComparer.Ordinal);

    public CraftingRuntime(
        CraftingRecipeCatalog recipes,
        ItemDefinitionCatalog items,
        Func<long> itemIdFactory)
    {
        _recipes = recipes;
        _items = items;
        _itemIdFactory = itemIdFactory;
    }

    public CraftingResult Craft(
        string idempotencyKey,
        int recipeId,
        int count,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        DateTimeOffset now,
        bool failBeforeCommit = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        lock (_sync)
        {
            var fingerprint = $"{recipeId}:{count}";
            if (_completed.TryGetValue(idempotencyKey, out var replay))
            {
                return replay.Fingerprint == fingerprint
                    ? replay.Result with { ResultCode = CraftingResultCode.DuplicateCompleted, Mutated = false }
                    : new CraftingResult(CraftingResultCode.ReplayConflict, 0, null, inventory.Snapshot(), wallet.Snapshot(), false);
            }

            if (count <= 0)
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.InvalidQuantity, inventory, wallet, 0, null, false);
            }

            if (!_recipes.TryResolve(recipeId, out var recipe) || !_items.Resolve(recipe.ResultItemTemplateId).Succeeded)
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.InvalidRecipe, inventory, wallet, 0, null, false);
            }

            if (recipe.ResultQuantity <= 0 ||
                recipe.CurrencyCost < 0 ||
                recipe.Ingredients.Count == 0 ||
                recipe.Ingredients.Any(ingredient =>
                    ingredient.Quantity <= 0 ||
                    !_items.Resolve(ingredient.ItemTemplateId).Succeeded))
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.InvalidRecipe, inventory, wallet, 0, null, false);
            }

            try
            {
                _ = checked(recipe.ResultQuantity * count);
                _ = checked(recipe.CurrencyCost * count);
                foreach (var ingredient in recipe.Ingredients)
                {
                    _ = checked(ingredient.Quantity * count);
                }
            }
            catch (OverflowException)
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.InvalidQuantity, inventory, wallet, 0, null, false);
            }

            var workingInventory = inventory.Clone();
            var workingWallet = wallet.Clone();
            foreach (var ingredient in recipe.Ingredients)
            {
                var quantity = checked(ingredient.Quantity * count);
                var removed = workingInventory.RemoveItem(ingredient.ItemTemplateId, quantity, _items, now);
                if (!removed.Succeeded)
                {
                    return Complete(idempotencyKey, fingerprint, CraftingResultCode.MissingMaterials, inventory, wallet, 0, null, false);
                }
            }

            if (recipe.CurrencyCost > 0)
            {
                var debit = workingWallet.Debit(recipe.CurrencyType, checked(recipe.CurrencyCost * count));
                if (!debit.Succeeded)
                {
                    return Complete(idempotencyKey, fingerprint, CraftingResultCode.InsufficientCurrency, inventory, wallet, 0, null, false);
                }
            }

            var output = checked(recipe.ResultQuantity * count);
            var definition = _items.Resolve(recipe.ResultItemTemplateId).Value!;
            var added = workingInventory.AddItem(definition, output, _itemIdFactory, now);
            if (!added.Succeeded)
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.InventoryFull, inventory, wallet, 0, null, false);
            }

            if (failBeforeCommit)
            {
                return Complete(idempotencyKey, fingerprint, CraftingResultCode.PersistenceFailure, inventory, wallet, 0, null, false);
            }

            inventory.ReplaceWith(workingInventory.Snapshot());
            wallet.ReplaceWith(workingWallet.Snapshot());
            return Complete(idempotencyKey, fingerprint, CraftingResultCode.Success, inventory, wallet, output, recipe.LinkedQuestId, true);
        }
    }

    private CraftingResult Complete(
        string key,
        string fingerprint,
        CraftingResultCode code,
        PlayerInventoryRuntime inventory,
        CurrencyWalletRuntime wallet,
        int created,
        int? questEvent,
        bool mutated)
    {
        var result = new CraftingResult(code, created, questEvent, inventory.Snapshot(), wallet.Snapshot(), mutated);
        _completed[key] = (fingerprint, result);
        return result;
    }
}
