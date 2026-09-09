using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Contracts;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbGameplayInventoryRuntime : IRuntimeCacheBuilder, IInventoryTransactionCoordinator, IItemUseCoordinator, IEquipmentEnhancementCoordinator, IPetGrowthGradeRuntime, IPetLifecycleCoordinator, IPhysicalSkillDamageRuntime, IMagicSkillDamageRuntime, IClientSkillMetadataRuntime, ICharacterGoldProjectionSource, ICharacterInventoryProjectionSource
{
    private readonly DatabaseOptions _options;
    private InventoryTransactionCoordinator? _inner;
    private ItemUseEffectEngine? _itemUseEffects;
    private EquipmentEnhancementEngine? _equipmentEnhancement;
    private PetGrowthGradeEngine? _petGrowthGrades;
    private PhysicalSkillDamageEngine? _physicalSkillDamage;
    private MagicSkillDamageEngine? _magicSkillDamage;
    private ClientSkillMetadataEngine? _clientSkillMetadata;

    public MariaDbGameplayInventoryRuntime(DatabaseOptions options)
    {
        _options = options;
    }

    public string PersistenceAuthority => nameof(MariaDbGameplayInventoryRepository);

    public bool IsInitialized => Volatile.Read(ref _inner) is not null;

    public int CatalogItemCount { get; private set; }

    public int QuarantinedItemCount { get; private set; }

    public int FatalIssueCount { get; private set; }

    public int ItemEffectCount { get; private set; }

    public int EquipmentEnhancementMaterialCount { get; private set; }

    public int EquipmentEnhancementRateCount { get; private set; }

    public int PetGrowthGradeRuleCount { get; private set; }

    public int PetAutomaticGrowthAllocationRuleCount { get; private set; }

    public int PhysicalSkillDamageCoefficientCount { get; private set; }

    public int MagicSkillDamageCoefficientCount { get; private set; }

    public int ClientSkillMetadataCount { get; private set; }

    public int ClientSkillMetadataMappingCount { get; private set; }

    public IReadOnlyList<InventoryTransactionRuntimeEvent> Events => Inner.Events;

    public async Task<OperationResult> BuildAsync(
        IReadOnlyList<StaticDataLoadCount> staticData,
        CancellationToken cancellationToken)
    {
        _ = staticData;
        try
        {
            var catalog = await new MariaDbGameplayContentCatalogRepository(_options).LoadAsync(cancellationToken);
            var enhancementCatalog = await new MariaDbEquipmentEnhancementCatalogRepository(_options).LoadAsync(cancellationToken);
            var petGrowthCatalog = await new MariaDbPetGrowthGradeCatalogRepository(_options).LoadAsync(cancellationToken);
            var physicalSkillDamageCatalog = await new MariaDbPhysicalSkillDamageCatalogRepository(_options).LoadAsync(cancellationToken);
            var magicSkillDamageCatalog = await new MariaDbMagicSkillDamageCatalogRepository(_options).LoadAsync(cancellationToken);
            var clientSkillMetadataCatalog = await new MariaDbClientSkillMetadataCatalogRepository(_options).LoadAsync(cancellationToken);
            CatalogItemCount = catalog.ItemCatalog.Definitions.Count;
            QuarantinedItemCount = catalog.QuarantinedItems.Count;
            FatalIssueCount = catalog.Issues.Count(issue => issue.Severity == GameplayContentSeverity.Fatal);
            ItemEffectCount = catalog.ItemEffectCatalog.EffectCount;
            EquipmentEnhancementMaterialCount = enhancementCatalog.MaterialCount;
            EquipmentEnhancementRateCount = enhancementCatalog.RateCount;
            PetGrowthGradeRuleCount = petGrowthCatalog.RuleCount;
            PetAutomaticGrowthAllocationRuleCount = petGrowthCatalog.AllocationRuleCount;
            PhysicalSkillDamageCoefficientCount = physicalSkillDamageCatalog.RuleCount;
            MagicSkillDamageCoefficientCount = magicSkillDamageCatalog.RuleCount;
            ClientSkillMetadataCount = clientSkillMetadataCatalog.RecordCount;
            ClientSkillMetadataMappingCount = clientSkillMetadataCatalog.FormalSkillMappingCount;
            // An empty catalog is valid when no item has crossed the Evidence gate yet.
            // Item transactions then fail closed as InvalidItem instead of promoting guessed content.
            if (FatalIssueCount > 0)
            {
                return OperationResult.Failure(
                    "inventory.production_catalog_invalid",
                    "The MariaDB gameplay inventory catalog contains fatal issues.",
                    nameof(MariaDbGameplayContentCatalogRepository));
            }

            var audit = new InMemoryInventoryAuditLedger();
            var coordinator = new InventoryTransactionCoordinator(
                catalog.ItemCatalog,
                catalog.MerchantCatalog,
                new MariaDbGameplayInventoryRepository(_options),
                audit);
            Volatile.Write(ref _itemUseEffects, new ItemUseEffectEngine(catalog.ItemCatalog, catalog.ItemEffectCatalog));
            Volatile.Write(ref _equipmentEnhancement, new EquipmentEnhancementEngine(enhancementCatalog));
            Volatile.Write(ref _petGrowthGrades, new PetGrowthGradeEngine(petGrowthCatalog));
            Volatile.Write(ref _physicalSkillDamage, new PhysicalSkillDamageEngine(physicalSkillDamageCatalog));
            Volatile.Write(ref _magicSkillDamage, new MagicSkillDamageEngine(magicSkillDamageCatalog));
            Volatile.Write(ref _clientSkillMetadata, new ClientSkillMetadataEngine(clientSkillMetadataCatalog));
            Volatile.Write(ref _inner, coordinator);
            return OperationResult.Success;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            Volatile.Write(ref _itemUseEffects, null);
            Volatile.Write(ref _equipmentEnhancement, null);
            Volatile.Write(ref _petGrowthGrades, null);
            Volatile.Write(ref _physicalSkillDamage, null);
            Volatile.Write(ref _magicSkillDamage, null);
            Volatile.Write(ref _clientSkillMetadata, null);
            Volatile.Write(ref _inner, null);
            return OperationResult.Failure(
                "inventory.production_initialization_failed",
                $"MariaDB gameplay inventory initialization failed closed ({exception.GetType().Name}).",
                nameof(MariaDbGameplayInventoryRuntime));
        }
    }

    public Task<OperationResult<long>> GetCurrentVersionAsync(long characterId, CancellationToken cancellationToken)
    {
        var inner = Volatile.Read(ref _inner);
        return inner is null
            ? Task.FromResult(OperationResult<long>.Failure(
                "inventory.production_not_initialized",
                "MariaDB gameplay inventory runtime is not initialized."))
            : inner.GetCurrentVersionAsync(characterId, cancellationToken);
    }

    public async Task<OperationResult<long>> LoadGoldAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            return OperationResult<long>.Failure(
                "world_authority.gold_projection_not_initialized",
                "MariaDB gameplay inventory authority is not initialized.");
        }

        try
        {
            var bundle = await new MariaDbGameplayInventoryRepository(_options)
                .LoadAsync(characterId, cancellationToken);
            var gold = bundle.Currency.Balances
                .FirstOrDefault(balance => string.Equals(balance.CurrencyType, "Gold", StringComparison.OrdinalIgnoreCase))?.Balance ?? 0;
            return OperationResult<long>.Success(gold);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            return OperationResult<long>.Failure(
                "world_authority.gold_projection_load_failed",
                "MariaDB could not load the authoritative Gold balance.");
        }
    }

    public async Task<OperationResult<PlayerInventorySnapshot>> LoadInventoryAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            return OperationResult<PlayerInventorySnapshot>.Failure(
                "world_authority.inventory_projection_not_initialized",
                "MariaDB gameplay inventory authority is not initialized.");
        }

        try
        {
            var bundle = await new MariaDbGameplayInventoryRepository(_options)
                .LoadAsync(characterId, cancellationToken);
            return OperationResult<PlayerInventorySnapshot>.Success(bundle.Inventory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            return OperationResult<PlayerInventorySnapshot>.Failure(
                "world_authority.inventory_projection_load_failed",
                "MariaDB could not load the authoritative inventory snapshot.");
        }
    }

    public Task<InventoryTransactionResult> ExecuteAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var inner = Volatile.Read(ref _inner);
        return inner is null
            ? Task.FromResult(new InventoryTransactionResult(
                InventoryTransactionResultCode.PersistenceFailure,
                request.TransactionId,
                SafeId(request.IdempotencyKey),
                0,
                0,
                0,
                0,
                "inventory.production_not_initialized"))
            : inner.ExecuteAsync(request, cancellationToken);
    }

    public InventoryInspectorSnapshot CaptureInspector(InventoryInspectorQuery query) =>
        Inner.CaptureInspector(query);

    public OperationResult<ItemUseEvaluation> EvaluateItemUse(ItemUseRequest request, ItemUseActorState actor)
    {
        var engine = Volatile.Read(ref _itemUseEffects);
        return engine is null
            ? OperationResult<ItemUseEvaluation>.Failure(
                "item.use.runtime_not_initialized",
                "MariaDB item-use runtime is not initialized.")
            : engine.Evaluate(request, actor);
    }

    public Task<ItemUseTransactionResult> UseItemAsync(
        ItemUseTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var engine = Volatile.Read(ref _itemUseEffects);
        return engine is null
            ? Task.FromResult(FailedItemUse(request, "item.use.runtime_not_initialized"))
            : new MariaDbItemUseRepository(_options).UseItemAsync(request, engine, cancellationToken);
    }

    public Task<EquipmentEnhancementTransactionResult> EnhanceEquipmentAsync(
        EquipmentEnhancementTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var engine = Volatile.Read(ref _equipmentEnhancement);
        return engine is null
            ? Task.FromResult(FailedEquipmentEnhancement(request, "equipment.enhancement.runtime_not_initialized"))
            : new MariaDbEquipmentEnhancementRepository(_options).EnhanceAsync(request, engine, cancellationToken);
    }

    public OperationResult<PetGrowthClassification> ClassifyPetInitialGrowth(int automaticGrowthTotal)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? OperationResult<PetGrowthClassification>.Failure(
                "pet.growth.runtime_not_initialized",
                "MariaDB pet-growth runtime is not initialized.")
            : engine.ClassifyInitial(automaticGrowthTotal);
    }

    public OperationResult<PetGrowthClassification> ObservePetLevelUp(
        PetGrowthClassification current,
        int reachedLevel,
        int automaticGrowthDeltaTotal)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? OperationResult<PetGrowthClassification>.Failure(
                "pet.growth.runtime_not_initialized",
                "MariaDB pet-growth runtime is not initialized.")
            : engine.ObserveLevelUp(current, reachedLevel, automaticGrowthDeltaTotal);
    }

    public OperationResult<PetAutomaticGrowthAllocation> ResolvePetAutomaticGrowth(
        int categoryId,
        PetGrowthGrade grade,
        int reachedLevel)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? OperationResult<PetAutomaticGrowthAllocation>.Failure(
                "pet.growth.runtime_not_initialized",
                "MariaDB pet-growth runtime is not initialized.")
            : engine.ResolveAutomaticGrowth(categoryId, grade, reachedLevel);
    }

    public OperationResult<PetCoreStats> CalculatePetCoreStats(
        int categoryId,
        PetGrowthGrade grade,
        int level)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? OperationResult<PetCoreStats>.Failure(
                "pet.growth.runtime_not_initialized",
                "MariaDB pet-growth runtime is not initialized.")
            : engine.CalculateCoreStats(categoryId, grade, level);
    }

    public OperationResult<PhysicalSkillDamageCoefficient> ResolvePhysicalSkillCoefficient(
        PhysicalSkillFamily family,
        int tier)
    {
        var engine = Volatile.Read(ref _physicalSkillDamage);
        return engine is null
            ? OperationResult<PhysicalSkillDamageCoefficient>.Failure(
                "battle.skill.physical_coefficient_runtime_not_initialized",
                "MariaDB physical skill damage coefficient runtime is not initialized.")
            : engine.ResolvePhysicalSkillCoefficient(family, tier);
    }

    public OperationResult<decimal> ResolvePhysicalSkillMultiplier(
        PhysicalSkillFamily family,
        int tier,
        SkillCoefficientSelection selection = SkillCoefficientSelection.Midpoint)
    {
        var engine = Volatile.Read(ref _physicalSkillDamage);
        return engine is null
            ? OperationResult<decimal>.Failure(
                "battle.skill.physical_coefficient_runtime_not_initialized",
                "MariaDB physical skill damage coefficient runtime is not initialized.")
            : engine.ResolvePhysicalSkillMultiplier(family, tier, selection);
    }

    public OperationResult<MagicSkillDamageCoefficient> ResolveMagicSkillCoefficient(
        BattleElement element,
        int tier)
    {
        var engine = Volatile.Read(ref _magicSkillDamage);
        return engine is null
            ? OperationResult<MagicSkillDamageCoefficient>.Failure(
                "battle.skill.magic_coefficient_runtime_not_initialized",
                "MariaDB magic skill damage coefficient runtime is not initialized.")
            : engine.ResolveMagicSkillCoefficient(element, tier);
    }

    public OperationResult<MagicSkillDamageResult> CalculateMagicSkillDamage(
        MagicSkillDamageRequest request)
    {
        var engine = Volatile.Read(ref _magicSkillDamage);
        return engine is null
            ? OperationResult<MagicSkillDamageResult>.Failure(
                "battle.skill.magic_coefficient_runtime_not_initialized",
                "MariaDB magic skill damage coefficient runtime is not initialized.")
            : engine.CalculateMagicSkillDamage(request);
    }

    public OperationResult<ClientSkillMetadata> ResolveClientSkillMetadata(int officialClientItemId)
    {
        var engine = Volatile.Read(ref _clientSkillMetadata);
        return engine is null
            ? OperationResult<ClientSkillMetadata>.Failure(
                "skill.client_metadata_runtime_not_initialized",
                "MariaDB client skill metadata runtime is not initialized.")
            : engine.ResolveClientSkillMetadata(officialClientItemId);
    }

    public IReadOnlyList<ClientSkillMetadata> ResolveClientSkillMetadataForFormalSkill(long skillId) =>
        Volatile.Read(ref _clientSkillMetadata)?.ResolveClientSkillMetadataForFormalSkill(skillId) ?? [];

    public Task<PetAcquisitionTransactionResult> AcquirePetAsync(
        PetAcquisitionTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? Task.FromResult(new PetAcquisitionTransactionResult(
                request.TransactionId, false, 0, request.PetTemplateId, 0, null, null, 0, 0, null,
                "pet.lifecycle.runtime_not_initialized"))
            : new MariaDbPetLifecycleRepository(_options).AcquireAsync(request, engine, cancellationToken);
    }

    public Task<PetLevelUpTransactionResult> ApplyPetLevelUpAsync(
        PetLevelUpTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var engine = Volatile.Read(ref _petGrowthGrades);
        return engine is null
            ? Task.FromResult(new PetLevelUpTransactionResult(
                request.TransactionId, false, request.PetInstanceId, 0, 0, request.ExperienceAfter,
                null, null, 0, 0, "pet.lifecycle.runtime_not_initialized"))
            : new MariaDbPetLifecycleRepository(_options).ApplyLevelUpAsync(request, engine, cancellationToken);
    }

    public Task<PetFourthSkillTransactionResult> TeachPetFourthSkillAsync(
        PetFourthSkillTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _petGrowthGrades) is null)
        {
            return Task.FromResult(new PetFourthSkillTransactionResult(
                request.TransactionId, false, request.PetInstanceId, 0, string.Empty, 0, 0,
                "pet.lifecycle.runtime_not_initialized"));
        }
        return new MariaDbPetLifecycleRepository(_options).TeachFourthSkillAsync(
            request, new PetFourthSkillEngine(), cancellationToken);
    }

    public Task<PetDeathTransactionResult> ApplyPetDeathAsync(
        PetDeathTransactionRequest request,
        CancellationToken cancellationToken) =>
        Volatile.Read(ref _petGrowthGrades) is null
            ? Task.FromResult(new PetDeathTransactionResult(
                request.TransactionId, false, request.PetInstanceId, null, null,
                "pet.lifecycle.runtime_not_initialized"))
            : new MariaDbPetLifecycleRepository(_options).ApplyDeathAsync(request, cancellationToken);

    private InventoryTransactionCoordinator Inner =>
        Volatile.Read(ref _inner) ?? throw new InvalidOperationException("MariaDB gameplay inventory runtime is not initialized.");

    private static string SafeId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private static ItemUseTransactionResult FailedItemUse(ItemUseTransactionRequest request, string code) =>
        new(request.TransactionId, false, 0, 0, 0, 0, 0, 0, code);

    private static EquipmentEnhancementTransactionResult FailedEquipmentEnhancement(
        EquipmentEnhancementTransactionRequest request,
        string code) =>
        new(request.TransactionId, false, 0, 0, false, false, 0, 0, 0, 0, null, null, code);
}
