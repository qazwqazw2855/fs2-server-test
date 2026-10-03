using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class QuestProgressPlannerTests
{
    private static QuestProgressSnapshot Snapshot(
        params QuestObjectiveProgress[] objectives) =>
        new(Guid.NewGuid(), 1, 3, objectives);

    private static QuestProgressEvent Event(
        QuestObjectiveKind kind = QuestObjectiveKind.DefeatMonster,
        long target = 100, long count = 1, long character = 1) =>
        new(Guid.NewGuid(), character, kind, target, count);

    [Fact]
    public void Only_matching_objective_advances_and_input_is_preserved()
    {
        var snapshot = Snapshot(
            new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, 3, 0),
            new QuestObjectiveProgress(2, QuestObjectiveKind.DefeatMonster, 200, 1, 0));

        var plan = QuestProgressPlanner.Plan(snapshot, Event(count: 2));

        Assert.True(plan.Succeeded);
        Assert.False(plan.Ready);
        var changed = Assert.Single(plan.ChangedObjectives);
        Assert.Equal(1, changed.ObjectiveId);
        Assert.Equal(2, changed.CurrentCount);
        Assert.All(snapshot.Objectives, value => Assert.Equal(0, value.CurrentCount));
        Assert.Equal(3, plan.ExpectedQuestVersion);
    }

    [Fact]
    public void Huge_event_count_caps_progress_without_overflow()
    {
        var snapshot = Snapshot(
            new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, long.MaxValue, 1));

        var plan = QuestProgressPlanner.Plan(
            snapshot, Event(count: long.MaxValue));

        Assert.True(plan.Ready);
        Assert.Equal(
            long.MaxValue,
            Assert.Single(plan.ChangedObjectives).CurrentCount);
    }

    [Fact]
    public void Readiness_requires_every_objective()
    {
        var snapshot = Snapshot(
            new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, 1, 1),
            new QuestObjectiveProgress(2, QuestObjectiveKind.VisitMap, 200, 1, 0));

        var plan = QuestProgressPlanner.Plan(
            snapshot, Event(QuestObjectiveKind.VisitMap, 200));

        Assert.True(plan.Ready);
        Assert.Equal(2, Assert.Single(plan.ChangedObjectives).ObjectiveId);
    }

    [Fact]
    public void Another_character_cannot_advance_the_quest()
    {
        var plan = QuestProgressPlanner.Plan(
            Snapshot(new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, 1, 0)),
            Event(character: 2));

        Assert.Equal(QuestProgressFailure.CharacterMismatch, plan.Failure);
        Assert.False(plan.Ready);
        Assert.Empty(plan.ChangedObjectives);
    }

    [Fact]
    public void Empty_objectives_do_not_imply_completion()
    {
        var plan = QuestProgressPlanner.Plan(Snapshot(), Event());

        Assert.Equal(QuestProgressFailure.EmptyObjectives, plan.Failure);
        Assert.False(plan.Ready);
    }

    [Fact]
    public void Same_target_with_different_event_kind_does_not_advance()
    {
        var plan = QuestProgressPlanner.Plan(
            Snapshot(new QuestObjectiveProgress(1, QuestObjectiveKind.VisitMap, 100, 1, 0)),
            Event());

        Assert.True(plan.Succeeded);
        Assert.False(plan.Ready);
        Assert.Empty(plan.ChangedObjectives);
    }

    [Fact]
    public void Completed_objective_produces_no_further_change()
    {
        var plan = QuestProgressPlanner.Plan(
            Snapshot(new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, 1, 1)),
            Event());

        Assert.True(plan.Ready);
        Assert.Empty(plan.ChangedObjectives);
    }

    [Fact]
    public void Duplicate_objective_identity_is_invalid()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuestProgressPlanner.Plan(
                Snapshot(
                    new QuestObjectiveProgress(1, QuestObjectiveKind.DefeatMonster, 100, 1, 0),
                    new QuestObjectiveProgress(1, QuestObjectiveKind.VisitMap, 200, 1, 0)),
                Event()));
    }

    [Fact]
    public void Interaction_cannot_supply_an_arbitrary_progress_count()
    {
        Assert.Throws<ArgumentException>(() =>
            QuestProgressPlanner.Plan(
                Snapshot(new QuestObjectiveProgress(1, QuestObjectiveKind.InteractNpc, 100, 3, 0)),
                Event(QuestObjectiveKind.InteractNpc, count: 3)));
    }
}
