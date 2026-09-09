using God2.AdvancedHeadlessVerification;

namespace God2.ClassicServer.HeadlessGameplay.Tests;

public sealed class AdvancedHeadlessVerificationTests
{
    public static TheoryData<string> StandardSnapshots
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var snapshot in SnapshotCatalog.Create())
            {
                data.Add(snapshot.Name);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StandardSnapshots))]
    public void Standard_snapshot_forks_are_isolated_and_hash_stable(string name)
    {
        var snapshot = Assert.Single(SnapshotCatalog.Create(), value => value.Name == name);
        var first = snapshot.Fork(name + ".first") with { Name = name };
        var second = snapshot.Fork(name + ".second") with { Name = name };

        Assert.Equal(snapshot.StableHash(), first.StableHash());
        Assert.Equal(first.StableHash(), second.StableHash());
        Assert.False(ReferenceEquals(first.Equipment, second.Equipment));
        Assert.False(ReferenceEquals(first.MountLoyalty, second.MountLoyalty));
    }

    [Fact]
    public void Snapshot_catalog_contains_the_fourteen_required_fork_points()
    {
        var names = SnapshotCatalog.Create().Select(value => value.Name).ToArray();
        Assert.Equal(14, names.Length);
        Assert.Equal(14, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("FreshCharacter", names);
        Assert.Contains("BattleCommandWindow", names);
        Assert.Contains("FailureRecoveryCheckpoint", names);
    }

    [Fact]
    public void Virtual_clock_orders_same_timestamp_and_honors_cancellation()
    {
        var clock = new DeterministicVirtualClock(DateTimeOffset.UnixEpoch);
        clock.Schedule(TimeSpan.FromSeconds(2), "first");
        clock.Schedule(TimeSpan.FromSeconds(2), "second");
        var cancelled = clock.Schedule(TimeSpan.FromSeconds(1), "cancelled");
        Assert.True(clock.Cancel(cancelled));
        Assert.Equal(["first", "second"], clock.AdvanceTo(DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Equal(0, clock.PendingTimerCount);
    }

    [Fact]
    public void Virtual_clock_snapshot_restore_replays_identically()
    {
        var clock = new DeterministicVirtualClock(DateTimeOffset.UnixEpoch);
        clock.Schedule(TimeSpan.FromMinutes(1), "cooldown");
        clock.Schedule(TimeSpan.FromMinutes(2), "status-expire");
        var snapshot = clock.Snapshot();
        var first = DeterministicVirtualClock.Restore(snapshot);
        var second = DeterministicVirtualClock.Restore(snapshot);
        Assert.Equal(first.AdvanceBy(TimeSpan.FromMinutes(2)), second.AdvanceBy(TimeSpan.FromMinutes(2)));
        Assert.Equal(DeterministicHash.Of(first.Snapshot()), DeterministicHash.Of(second.Snapshot()));
    }

    [Fact]
    public void Model_catalog_contains_fifteen_connected_models()
    {
        var models = GameplayModelCatalog.Create();
        Assert.Equal(15, models.Count);
        Assert.Equal(15, models.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (var model in models)
        {
            Assert.Contains(model.InitialState, model.States);
            Assert.NotEmpty(model.Transitions);
            Assert.All(model.Transitions, transition =>
            {
                Assert.Contains(transition.FromState, model.States);
                Assert.Contains(transition.ToState, model.States);
            });
        }
    }

    [Fact]
    public void Fast_verification_executes_all_precision_layers_without_failure()
    {
        var result = new AdvancedVerificationRunner().RunFast();
        Assert.Equal("PASS", result.Status);
        Assert.Equal(14, result.Snapshots.SnapshotCount);
        Assert.Equal(15, result.ModelCount);
        Assert.True(result.Exhaustive.CaseCount > 10_000);
        Assert.Equal(result.Property.ModelStateCount, result.Property.VisitedStateCount);
        Assert.Equal(result.Property.ModelTransitionCount, result.Property.VisitedTransitionCount);
        Assert.Equal(0, result.Differential.MismatchCount);
        Assert.Equal(0, result.Concurrency.FailureCount);
        Assert.Equal(100, result.Shrinking.SuccessRatePercent);
        Assert.Equal(result.Replay.FixtureCount, result.Replay.ReplayThreeTimesMatchCount);
        Assert.Equal(0, result.FakeNetworkBytes);
    }

    [Fact]
    public void Coverage_guidance_stops_after_configured_no_growth_window()
    {
        var result = new AdvancedVerificationRunner().RunFast().Property;
        Assert.Equal(result.LastCoverageGrowthIteration + result.NoGrowthStopWindow, result.CoverageSaturationPoint);
        Assert.Empty(result.UncoveredTransitions);
        Assert.Empty(result.UnreachableCandidates);
        Assert.True(result.GeneratedCases < 100_000);
    }

    [Fact]
    public void Differential_execution_is_three_way_and_side_effect_free()
    {
        var result = new AdvancedVerificationRunner().RunFast().Differential;
        Assert.True(result.EventCount > 10_000);
        Assert.Equal(result.LegacyAggregateHash, result.ShadowAggregateHash);
        Assert.Equal(result.LegacyAggregateHash, result.ReferenceAggregateHash);
        Assert.Equal(0, result.ShadowExternalSideEffectCount);
        Assert.Equal(0, result.NetworkBytes);
    }

    [Fact]
    public void Deterministic_scheduler_explores_all_required_race_families()
    {
        var result = new AdvancedVerificationRunner().RunFast().Concurrency;
        Assert.Equal(10, result.ExploredRaces.Count);
        Assert.True(result.ScheduleCount >= 900);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public void Shrinker_and_replay_are_complete_and_deterministic()
    {
        var result = new AdvancedVerificationRunner().RunFast();
        Assert.Equal(result.Shrinking.InjectedFailureCount, result.Shrinking.ShrunkFailureCount);
        Assert.Equal(1, result.Shrinking.LargestMinimalSequence);
        Assert.Equal(0, result.Shrinking.ShrinkFailures);
        Assert.Equal(0, result.Replay.NonDeterministicFailureCount);
        Assert.Equal(0, result.Replay.ReplayDivergenceCount);
    }
}
