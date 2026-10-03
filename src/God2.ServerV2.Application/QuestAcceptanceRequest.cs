namespace God2.ServerV2.Application;

// The request carries identity, never objective or reward contents.
public sealed record QuestAcceptanceRequest(
    Guid QuestInstanceId,
    long CharacterId,
    long QuestId,
    string ExpectedDefinitionFingerprint);

public enum QuestAcceptanceStatus
{
    Accepted,
    Replayed,
    CharacterMissing,
    CharacterLevelUnknown,
    LevelRejected,
    QuestMissing,
    QuestDisabled,
    DefinitionMissing,
    DefinitionConflict,
    EvidenceBlocked,
    AlreadyAccepted,
    InstanceConflict
}

public sealed record QuestAcceptanceResult(
    QuestAcceptanceStatus Status,
    Guid QuestInstanceId,
    string DefinitionFingerprint)
{
    public bool Succeeded =>
        Status is QuestAcceptanceStatus.Accepted or QuestAcceptanceStatus.Replayed;
}

public interface IQuestAcceptanceDefinitionSource
{
    ValueTask<QuestAcceptanceDefinition?> GetAsync(
        long questId,
        CancellationToken cancellationToken);
}

// Approval covers the entire definition, including nonrepeatability,
// level policy and the absence or satisfaction of other prerequisites.
// Enabled catalog rows and nonempty source references are not approval.
public interface IQuestAcceptanceEvidenceGate
{
    ValueTask<bool> IsApprovedAsync(
        QuestAcceptanceSnapshot snapshot,
        long characterId,
        CancellationToken cancellationToken);
}

public sealed class BlockedQuestAcceptanceEvidenceGate
    : IQuestAcceptanceEvidenceGate
{
    public ValueTask<bool> IsApprovedAsync(
        QuestAcceptanceSnapshot snapshot,
        long characterId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

public interface IQuestAcceptanceWriter
{
    ValueTask<QuestAcceptanceResult> AcceptAsync(
        QuestAcceptanceRequest request,
        CancellationToken cancellationToken);
}
