using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum EquipmentEnhancementTargetType
{
    Weapon,
    Equipment
}

public enum EquipmentEnhancementGrade
{
    General,
    Advanced,
    Special
}

public enum EquipmentEnhancementFailurePolicy
{
    DestroyTarget,
    PreserveTarget
}

public sealed record EquipmentEnhancementMaterialDefinition(
    int ItemTemplateId,
    int ClientItemId,
    string DisplayName,
    EquipmentEnhancementTargetType TargetType,
    int EquipmentTier,
    EquipmentEnhancementGrade Grade,
    int MinimumIncrement,
    int MaximumIncrement,
    int MaximumDurabilityLoss,
    EquipmentEnhancementFailurePolicy FailurePolicy,
    string EvidenceStatus,
    string Source,
    bool Enabled);

public sealed record EquipmentEnhancementRateDefinition(
    EquipmentEnhancementGrade Grade,
    int TargetEnhancementLevel,
    int SuccessRateBasisPoints,
    string EvidenceStatus,
    string Source,
    bool Enabled);

public sealed class EquipmentEnhancementCatalog
{
    private readonly IReadOnlyDictionary<int, EquipmentEnhancementMaterialDefinition> _materials;
    private readonly IReadOnlyDictionary<(EquipmentEnhancementGrade Grade, int Level), EquipmentEnhancementRateDefinition> _rates;

    public EquipmentEnhancementCatalog(
        IEnumerable<EquipmentEnhancementMaterialDefinition> materials,
        IEnumerable<EquipmentEnhancementRateDefinition> rates)
    {
        _materials = new ReadOnlyDictionary<int, EquipmentEnhancementMaterialDefinition>(
            materials.Where(value => value.Enabled).ToDictionary(value => value.ItemTemplateId));
        _rates = new ReadOnlyDictionary<(EquipmentEnhancementGrade, int), EquipmentEnhancementRateDefinition>(
            rates.Where(value => value.Enabled).ToDictionary(value => (value.Grade, value.TargetEnhancementLevel)));
    }

    public int MaterialCount => _materials.Count;

    public int RateCount => _rates.Count;

    public bool TryResolveMaterial(int itemTemplateId, out EquipmentEnhancementMaterialDefinition definition) =>
        _materials.TryGetValue(itemTemplateId, out definition!);

    public bool TryResolveRate(
        EquipmentEnhancementGrade grade,
        int targetEnhancementLevel,
        out EquipmentEnhancementRateDefinition definition) =>
        _rates.TryGetValue((grade, targetEnhancementLevel), out definition!);
}

public sealed record EquipmentEnhancementState(
    long InventoryId,
    int ItemTemplateId,
    EquipmentEnhancementTargetType TargetType,
    int EquipmentTier,
    int EnhancementLevel,
    int MaximumEnhancementLevel,
    int? CurrentDurability,
    int? MaximumDurability,
    int MaximumDurabilityPenalty);

public sealed record EquipmentEnhancementEvaluation(
    int MaterialItemTemplateId,
    int SuccessRateBasisPoints,
    bool EnhancementSucceeded,
    bool TargetDestroyed,
    int AppliedIncrement,
    EquipmentEnhancementState UpdatedTarget);

public sealed record EquipmentEnhancementTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    long AccountId,
    string SessionId,
    int MaterialItemTemplateId,
    long TargetInventoryId,
    DateTimeOffset CreatedAtUtc);

public sealed record EquipmentEnhancementTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    bool EnhancementSucceeded,
    bool TargetDestroyed,
    int EnhancementLevelBefore,
    int EnhancementLevelAfter,
    int AppliedIncrement,
    int SuccessRateBasisPoints,
    int? CurrentDurabilityAfter,
    int? MaximumDurabilityAfter,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public interface IEquipmentEnhancementCoordinator
{
    Task<EquipmentEnhancementTransactionResult> EnhanceEquipmentAsync(
        EquipmentEnhancementTransactionRequest request,
        CancellationToken cancellationToken);
}

public sealed class EquipmentEnhancementEngine
{
    private readonly EquipmentEnhancementCatalog _catalog;

    public EquipmentEnhancementEngine(EquipmentEnhancementCatalog catalog) =>
        _catalog = catalog;

    public OperationResult<EquipmentEnhancementEvaluation> Evaluate(
        int materialItemTemplateId,
        EquipmentEnhancementState target,
        int successRollBasisPoints,
        int incrementRoll)
    {
        if (!_catalog.TryResolveMaterial(materialItemTemplateId, out var material))
        {
            return Failure("equipment.enhancement.material_invalid", "Enhancement material is missing or disabled.");
        }

        if (target.InventoryId <= 0 || target.ItemTemplateId <= 0 || target.EquipmentTier is < 1 or > 10 ||
            target.EnhancementLevel < 0 || target.MaximumEnhancementLevel <= 0 ||
            target.EnhancementLevel >= target.MaximumEnhancementLevel ||
            target.MaximumDurabilityPenalty < 0 || successRollBasisPoints is < 0 or >= 10_000 || incrementRoll < 0)
        {
            return Failure("equipment.enhancement.target_invalid", "Enhancement target state or random roll is invalid.");
        }

        if (material.TargetType != target.TargetType)
        {
            return Failure("equipment.enhancement.target_type_mismatch", "Enhancement material does not match the target catalog type.");
        }

        if (material.EquipmentTier != target.EquipmentTier)
        {
            return Failure("equipment.enhancement.tier_mismatch", "Enhancement material tier does not match the target equipment tier.");
        }

        var targetLevel = checked(target.EnhancementLevel + 1);
        if (!_catalog.TryResolveRate(material.Grade, targetLevel, out var rate))
        {
            return Failure("equipment.enhancement.rate_missing", "No enabled success rate exists for this enhancement level.");
        }

        var succeeded = successRollBasisPoints < rate.SuccessRateBasisPoints;
        if (!succeeded)
        {
            return OperationResult<EquipmentEnhancementEvaluation>.Success(new EquipmentEnhancementEvaluation(
                materialItemTemplateId,
                rate.SuccessRateBasisPoints,
                EnhancementSucceeded: false,
                TargetDestroyed: material.FailurePolicy == EquipmentEnhancementFailurePolicy.DestroyTarget,
                AppliedIncrement: 0,
                target));
        }

        var incrementRange = checked(material.MaximumIncrement - material.MinimumIncrement + 1);
        if (material.MinimumIncrement <= 0 || incrementRange <= 0)
        {
            return Failure("equipment.enhancement.material_invalid", "Enhancement increment range is invalid.");
        }

        var requestedIncrement = checked(material.MinimumIncrement + incrementRoll % incrementRange);
        var nextLevel = Math.Min(target.MaximumEnhancementLevel, checked(target.EnhancementLevel + requestedIncrement));
        var appliedIncrement = nextLevel - target.EnhancementLevel;
        int? nextMaximumDurability = target.MaximumDurability is int maximum
            ? Math.Max(0, maximum - material.MaximumDurabilityLoss)
            : null;
        var nextCurrentDurability = target.CurrentDurability is int current && nextMaximumDurability is int nextMaximum
            ? Math.Min(current, nextMaximum)
            : target.CurrentDurability;
        var updated = target with
        {
            EnhancementLevel = nextLevel,
            CurrentDurability = nextCurrentDurability,
            MaximumDurability = nextMaximumDurability,
            MaximumDurabilityPenalty = checked(target.MaximumDurabilityPenalty + material.MaximumDurabilityLoss)
        };

        return OperationResult<EquipmentEnhancementEvaluation>.Success(new EquipmentEnhancementEvaluation(
            materialItemTemplateId,
            rate.SuccessRateBasisPoints,
            EnhancementSucceeded: true,
            TargetDestroyed: false,
            appliedIncrement,
            updated));

        OperationResult<EquipmentEnhancementEvaluation> Failure(string code, string message) =>
            OperationResult<EquipmentEnhancementEvaluation>.Failure(
                code,
                message,
                materialItemTemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static int ResolveEquipmentTier(int? requiredLevel)
    {
        var level = Math.Max(0, requiredLevel ?? 0);
        return Math.Clamp(level / 10 + 1, 1, 10);
    }
}
