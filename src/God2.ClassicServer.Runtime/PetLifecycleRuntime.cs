namespace God2.ClassicServer.Runtime;

public sealed record PetMutationAuthority(long CharacterId, long AccountId, string SessionId);

public sealed record PetAcquisitionTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    PetMutationAuthority Authority,
    int PetTemplateId,
    string PetName,
    PetAcquisitionOrigin Origin,
    int InitialAutomaticGrowthTotal,
    long? WildSourceMonsterId = null,
    int? WildEncounterLevel = null);

public sealed record PetAcquisitionTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long PetInstanceId,
    int PetTemplateId,
    int Level,
    PetGrowthClassification? Growth,
    PetCoreStats? CoreStats,
    long MaximumHp,
    long MaximumMp,
    long? MaximumLifespan,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public sealed record PetLevelUpTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    PetMutationAuthority Authority,
    long PetInstanceId,
    int ReachedLevel,
    long ExperienceAfter,
    int AutomaticGrowthTotal);

public sealed record PetLevelUpTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long PetInstanceId,
    int PreviousLevel,
    int CurrentLevel,
    long ExperienceAfter,
    PetGrowthClassification? Growth,
    PetCoreStats? CoreStats,
    long MaximumHp,
    long MaximumMp,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public sealed record PetFourthSkillTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    PetMutationAuthority Authority,
    long PetInstanceId,
    int LearningItemTemplateId);

public sealed record PetFourthSkillTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long PetInstanceId,
    long SkillId,
    string SkillNameZhTw,
    long InventoryVersionBefore,
    long InventoryVersionAfter,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public sealed record PetDeathTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    PetMutationAuthority Authority,
    long PetInstanceId);

public sealed record PetDeathTransactionResult(
    Guid TransactionId,
    bool Replayed,
    long PetInstanceId,
    long? LifespanBefore,
    long? LifespanAfter,
    string FailureCode)
{
    public bool Succeeded => string.IsNullOrEmpty(FailureCode);
}

public interface IPetLifecycleCoordinator
{
    Task<PetAcquisitionTransactionResult> AcquirePetAsync(
        PetAcquisitionTransactionRequest request,
        CancellationToken cancellationToken);

    Task<PetLevelUpTransactionResult> ApplyPetLevelUpAsync(
        PetLevelUpTransactionRequest request,
        CancellationToken cancellationToken);

    Task<PetFourthSkillTransactionResult> TeachPetFourthSkillAsync(
        PetFourthSkillTransactionRequest request,
        CancellationToken cancellationToken);

    Task<PetDeathTransactionResult> ApplyPetDeathAsync(
        PetDeathTransactionRequest request,
        CancellationToken cancellationToken);
}

public static class PetVitalRules
{
    public static long CalculateMaximumHp(PetCoreStats stats) => Calculate(stats).MaximumHp;

    public static long CalculateMaximumMp(PetCoreStats stats) => Calculate(stats).MaximumMp;

    public static long CalculatePhysicalAttack(PetCoreStats stats) => Calculate(stats).PhysicalAttack;

    public static long CalculatePhysicalDefense(PetCoreStats stats) => Calculate(stats).PhysicalDefense;

    public static long CalculateMagicAttack(PetCoreStats stats) => Calculate(stats).MagicAttack;

    public static long CalculateMagicDefense(PetCoreStats stats) => Calculate(stats).MagicDefense;

    public static RoundedDerivedCombatStatContribution Calculate(PetCoreStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return OfficialAttributeDerivation.CalculatePet(
            new PrimaryAttributePoints(
                stats.Constitution,
                stats.Strength,
                stats.Intelligence,
                stats.Speed)).Truncate();
    }
}
