using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed record ProductionQuestContent(
    int QuestId,
    string NameZhTw,
    string? DescriptionZhTw,
    string ObjectiveEvidenceStatus,
    string RewardEvidenceStatus,
    string ContentReleaseId);

public sealed record ProductionEquipmentSetMemberContent(
    int ItemId,
    string SlotName,
    string EvidenceStatus);

public sealed record ProductionEquipmentSetContent(
    int SetId,
    string NameZhTw,
    int RequiredPieces,
    string EffectsZhTw,
    bool BonusEnabled,
    IReadOnlyList<ProductionEquipmentSetMemberContent> Members,
    string ContentReleaseId);

public sealed record ProductionPetInnateContent(
    int InnateId,
    string NameZhTw,
    string? DescriptionZhTw,
    string? EffectReference,
    int? EffectValue,
    string EffectEvidenceStatus,
    string ContentReleaseId);

public interface IProductionGameplayContentAuthority
{
    string ActiveReleaseId { get; }

    bool IsReady { get; }

    OperationResult RequireReady();

    OperationResult<ProductionQuestContent> ResolveQuest(int questId);

    OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId);

    OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId);
}
