namespace God2.ServerV2.Application;

// Server-side operation. Reward contents are loaded from persisted snapshots.
public sealed record QuestRewardClaimRequest(
    Guid TransactionId,
    long CharacterId,
    Guid QuestInstanceId,
    long ExpectedQuestVersion,
    Guid InventoryId,
    long ExpectedInventoryVersion,
    long ExpectedMutationSequence);

public enum QuestRewardClaimStatus
{
    Claimed,
    Replayed,
    CharacterMissing,
    QuestInstanceMissing,
    QuestNotReady,
    QuestVersionConflict,
    RewardSnapshotMissing,
    RewardSnapshotInvalid,
    RewardEvidenceBlocked,
    UnsupportedReward,
    InventoryMissing,
    InventoryVersionConflict,
    ItemMissing,
    ItemDisabled,
    InsufficientCapacity,
    ClaimConflict
}

public sealed record QuestRewardClaimResult(
    QuestRewardClaimStatus Status,
    Guid TransactionId,
    Guid QuestInstanceId,
    long InventoryVersionBefore,
    long InventoryVersionAfter)
{
    public bool Succeeded =>
        Status is QuestRewardClaimStatus.Claimed
            or QuestRewardClaimStatus.Replayed;
}

public interface IQuestRewardClaimWriter
{
    ValueTask<QuestRewardClaimResult> ClaimAsync(
        QuestRewardClaimRequest request,
        CancellationToken cancellationToken);
}

public sealed record QuestRewardEvidenceIdentity(
    long QuestId,
    string DefinitionFingerprint,
    string RewardFingerprint,
    string EvidenceReference);

// Implementations must consult trusted, reviewed evidence.
// A nonempty reference or enabled quest alone is insufficient.
public interface IQuestRewardEvidenceGate
{
    ValueTask<bool> IsApprovedAsync(
        QuestRewardEvidenceIdentity identity,
        CancellationToken cancellationToken);
}

// Safe default until an evidence-backed content release is available.
public sealed class BlockedQuestRewardEvidenceGate : IQuestRewardEvidenceGate
{
    public ValueTask<bool> IsApprovedAsync(
        QuestRewardEvidenceIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
