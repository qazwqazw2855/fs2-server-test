namespace God2.ServerV2.Application;

public enum QuestObjectiveKind
{
    DefeatMonster,
    InteractNpc,
    VisitMap
}

public sealed record QuestObjectiveProgress(
    long ObjectiveId,
    QuestObjectiveKind Kind,
    long TargetId,
    long RequiredCount,
    long CurrentCount);

public sealed record QuestProgressSnapshot(
    Guid QuestInstanceId,
    long CharacterId,
    long QuestVersion,
    IReadOnlyList<QuestObjectiveProgress> Objectives);

// Constructed by trusted server runtime after the action is confirmed.
// Do not construct directly from an unverified client packet.
public sealed record QuestProgressEvent(
    Guid EventId,
    long CharacterId,
    QuestObjectiveKind Kind,
    long TargetId,
    long Count);

public enum QuestProgressFailure
{
    None,
    CharacterMismatch,
    EmptyObjectives
}

public sealed record QuestProgressPlan(
    QuestProgressFailure Failure,
    Guid QuestInstanceId,
    Guid EventId,
    long ExpectedQuestVersion,
    bool Ready,
    IReadOnlyList<QuestObjectiveProgress> ChangedObjectives)
{
    public bool Succeeded => Failure == QuestProgressFailure.None;
}

// Pure planning. Event replay protection and version CAS belong to the store.
public static class QuestProgressPlanner
{
    public static QuestProgressPlan Plan(
        QuestProgressSnapshot snapshot,
        QuestProgressEvent progressEvent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(progressEvent);
        ArgumentNullException.ThrowIfNull(snapshot.Objectives);

        if (snapshot.QuestInstanceId == Guid.Empty ||
            snapshot.CharacterId <= 0 || snapshot.QuestVersion < 0)
            throw new InvalidDataException("Invalid quest progress snapshot.");

        if (progressEvent.EventId == Guid.Empty ||
            progressEvent.CharacterId <= 0 ||
            progressEvent.TargetId <= 0 ||
            progressEvent.Count <= 0 ||
            !Enum.IsDefined(progressEvent.Kind))
            throw new ArgumentException("Invalid quest progress event.");

        // Each arrival/interaction event represents one confirmed occurrence.
        if (progressEvent.Kind != QuestObjectiveKind.DefeatMonster &&
            progressEvent.Count != 1)
            throw new ArgumentException("This event requires count one.");

        var seen = new HashSet<long>();
        foreach (var objective in snapshot.Objectives)
        {
            ArgumentNullException.ThrowIfNull(objective);
            if (objective.ObjectiveId <= 0 ||
                !seen.Add(objective.ObjectiveId) ||
                !Enum.IsDefined(objective.Kind) ||
                objective.TargetId <= 0 ||
                objective.RequiredCount <= 0 ||
                objective.CurrentCount < 0 ||
                objective.CurrentCount > objective.RequiredCount)
                throw new InvalidDataException("Invalid quest objective.");
        }

        QuestProgressPlan Reject(QuestProgressFailure failure) =>
            new(failure, snapshot.QuestInstanceId, progressEvent.EventId,
                snapshot.QuestVersion, false,
                Array.Empty<QuestObjectiveProgress>());

        if (snapshot.CharacterId != progressEvent.CharacterId)
            return Reject(QuestProgressFailure.CharacterMismatch);
        if (snapshot.Objectives.Count == 0)
            return Reject(QuestProgressFailure.EmptyObjectives);

        var changes = new List<QuestObjectiveProgress>();
        var ready = true;

        foreach (var objective in snapshot.Objectives)
        {
            var current = objective.CurrentCount;
            if (objective.Kind == progressEvent.Kind &&
                objective.TargetId == progressEvent.TargetId)
            {
                // Subtraction first avoids overflowing when Count is very large.
                var remaining = objective.RequiredCount - current;
                current += Math.Min(remaining, progressEvent.Count);
            }

            if (current != objective.CurrentCount)
                changes.Add(objective with { CurrentCount = current });
            if (current != objective.RequiredCount)
                ready = false;
        }

        return new QuestProgressPlan(
            QuestProgressFailure.None,
            snapshot.QuestInstanceId,
            progressEvent.EventId,
            snapshot.QuestVersion,
            ready,
            Array.AsReadOnly(
                changes.OrderBy(value => value.ObjectiveId).ToArray()));
    }
}
