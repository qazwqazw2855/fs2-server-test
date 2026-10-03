namespace God2.ServerV2.Application;

public sealed record QuestProgressRequest(
    Guid QuestInstanceId,
    long ExpectedQuestVersion,
    QuestProgressEvent Event);

public enum QuestProgressStatus
{
    Applied,
    Replayed,
    CharacterMissing,
    QuestInstanceMissing,
    QuestNotActive,
    VersionConflict,
    EventConflict,
    EmptyObjectives,
    EvidenceBlocked
}

public sealed record QuestProgressResult(
    QuestProgressStatus Status,
    Guid QuestInstanceId,
    Guid EventId,
    long QuestVersionBefore,
    long QuestVersionAfter,
    bool Ready,
    IReadOnlyList<QuestObjectiveProgress> ChangedObjectives)
{
    public bool Succeeded =>
        Status is QuestProgressStatus.Applied or QuestProgressStatus.Replayed;
}

public interface IQuestProgressWriter
{
    ValueTask<QuestProgressResult> ApplyAsync(
        QuestProgressRequest request,
        CancellationToken cancellationToken);
}

public sealed record QuestProgressDefinitionIdentity(
    long QuestId,
    string DefinitionFingerprint,
    string ObjectivesFingerprint);

// Approval must bind the reviewed definition and the complete objective set.
// The objective fingerprint excludes mutable CurrentCount values.
public interface IQuestProgressEvidenceGate
{
    ValueTask<bool> IsApprovedAsync(
        QuestProgressDefinitionIdentity identity,
        CancellationToken cancellationToken);
}

public sealed class BlockedQuestProgressEvidenceGate : IQuestProgressEvidenceGate
{
    public ValueTask<bool> IsApprovedAsync(
        QuestProgressDefinitionIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
