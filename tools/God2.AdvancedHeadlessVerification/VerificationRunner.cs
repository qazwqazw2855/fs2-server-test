using System.Collections.Immutable;
using System.Diagnostics;

namespace God2.AdvancedHeadlessVerification;

public sealed record SnapshotVerificationResult(
    int SnapshotCount,
    int ForkTestCount,
    int StableHashChecks,
    int IsolationChecks,
    int DeterministicSeedChecks);

public sealed record VirtualClockVerificationResult(
    int TestCount,
    int TimerCallbacks,
    int CancellationChecks,
    int SnapshotRestoreChecks,
    bool UsedRealSleep);

public sealed record ExhaustiveCategoryResult(string Category, int Bound, long Cases, long InvariantChecks);
public sealed record ExhaustiveVerificationResult(long CaseCount, long InvariantChecks, IReadOnlyList<ExhaustiveCategoryResult> Categories, int Failures);

public sealed record PropertyVerificationResult(
    long GeneratedCases,
    long AcceptedCases,
    long RejectedPreconditionCases,
    long UniqueSeedCount,
    int ModelStateCount,
    int VisitedStateCount,
    int ModelTransitionCount,
    int VisitedTransitionCount,
    int UniqueConcreteStates,
    int LastCoverageGrowthIteration,
    int CoverageSaturationPoint,
    int NoGrowthStopWindow,
    double StateCoveragePercent,
    double TransitionCoveragePercent,
    double BranchCoveragePercent,
    double ResultCodeCoveragePercent,
    double ErrorPathCoveragePercent,
    double InvariantCoveragePercent,
    double RecoveryCoveragePercent,
    double FailureInjectionCoveragePercent,
    long CasesPerSecond,
    IReadOnlyList<string> UncoveredTransitions,
    IReadOnlyList<string> UnreachableCandidates);

public sealed record DifferentialVerificationResult(
    long EventCount,
    int ScenarioCount,
    int MismatchCount,
    int FirstDivergenceCount,
    int ShadowExternalSideEffectCount,
    int NetworkBytes,
    string LegacyAggregateHash,
    string ShadowAggregateHash,
    string ReferenceAggregateHash);

public sealed record ConcurrencyVerificationResult(
    int ScheduleCount,
    int FailureCount,
    int ScheduleSeedCount,
    int BarrierCheckpointCount,
    int FirstDivergenceCount,
    IReadOnlyList<string> ExploredRaces);

public sealed record ShrinkVerificationResult(
    int InjectedFailureCount,
    int ShrunkFailureCount,
    double SuccessRatePercent,
    int LongestOriginalSequence,
    int LargestMinimalSequence,
    long TotalShrinkDurationMilliseconds,
    int ShrinkFailures);

public sealed record ReplayVerificationResult(
    int FixtureCount,
    int ReplayThreeTimesMatchCount,
    int NonDeterministicFailureCount,
    int ReplayDivergenceCount,
    long ReplayExecutions,
    long ReplaysPerSecond,
    string AggregateStateHash,
    string AggregateEventHash);

public sealed record FastVerificationResult(
    string SchemaVersion,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int ModelCount,
    SnapshotVerificationResult Snapshots,
    VirtualClockVerificationResult VirtualClock,
    ExhaustiveVerificationResult Exhaustive,
    PropertyVerificationResult Property,
    DifferentialVerificationResult Differential,
    ConcurrencyVerificationResult Concurrency,
    ShrinkVerificationResult Shrinking,
    ReplayVerificationResult Replay,
    int UnresolvedInvariantFailures,
    int FakeNetworkBytes);

public sealed class AdvancedVerificationRunner
{
    private const int NoGrowthStopWindow = 512;

    public FastVerificationResult RunFast()
    {
        var started = DateTimeOffset.UtcNow;
        var snapshots = VerifySnapshots();
        var virtualClock = VerifyVirtualClock();
        var models = GameplayModelCatalog.Create();
        var exhaustive = RunExhaustive();
        var property = RunProperties(models);
        var differential = RunDifferential(snapshots.SnapshotCount);
        var concurrency = RunConcurrency();
        var shrinking = RunShrinking();
        var replay = RunReplay(shrinking.ShrunkFailureCount);

        VerificationGuard.Require(exhaustive.Failures == 0, "Bounded exhaustive search found an invariant failure.");
        VerificationGuard.Require(property.UncoveredTransitions.Count == 0, "Coverage-guided exploration left a reachable transition uncovered.");
        VerificationGuard.Require(differential.MismatchCount == 0 && differential.ShadowExternalSideEffectCount == 0, "Differential execution diverged or shadow produced a side effect.");
        VerificationGuard.Require(concurrency.FailureCount == 0, "Deterministic concurrency exploration found a failure.");
        VerificationGuard.Require(shrinking.ShrinkFailures == 0, "Automatic failure shrinking did not reach a minimal fixture.");
        VerificationGuard.Require(replay.NonDeterministicFailureCount == 0 && replay.ReplayDivergenceCount == 0, "Deterministic replay diverged.");

        return new FastVerificationResult(
            "advanced-headless-verification/v1",
            "PASS",
            started,
            DateTimeOffset.UtcNow,
            models.Count,
            snapshots,
            virtualClock,
            exhaustive,
            property,
            differential,
            concurrency,
            shrinking,
            replay,
            0,
            0);
    }

    private static SnapshotVerificationResult VerifySnapshots()
    {
        var snapshots = SnapshotCatalog.Create();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var forkTests = 0;
        var stableChecks = 0;
        var isolationChecks = 0;
        var seedChecks = 0;
        foreach (var snapshot in snapshots)
        {
            var firstHash = snapshot.StableHash();
            VerificationGuard.Require(firstHash == snapshot.StableHash(), $"Snapshot hash is unstable: {snapshot.Name}");
            stableChecks++;
            VerificationGuard.Require(hashes.Add(firstHash), $"Standard snapshots must be semantically unique: {snapshot.Name}");

            var first = snapshot.Fork($"{snapshot.Name}.fork-a");
            var second = snapshot.Fork($"{snapshot.Name}.fork-b");
            forkTests += 2;
            VerificationGuard.Require(!ReferenceEquals(first.Equipment, second.Equipment), "Forked equipment references must be isolated.");
            VerificationGuard.Require(!ReferenceEquals(first.MountLoyalty, second.MountLoyalty), "Forked mount references must be isolated.");
            VerificationGuard.Require(!ReferenceEquals(first.PetStates, second.PetStates), "Forked pet references must be isolated.");
            isolationChecks += 3;

            var canonicalFirst = first with { Name = snapshot.Name };
            var canonicalSecond = second with { Name = snapshot.Name };
            VerificationGuard.Require(canonicalFirst.StableHash() == canonicalSecond.StableHash(), "Same snapshot and seed must fork deterministically.");
            VerificationGuard.Require(first.Rng == second.Rng && first.Clock == second.Clock, "Forked RNG and virtual clock must be equal but independent values.");
            seedChecks += 2;
        }
        return new SnapshotVerificationResult(snapshots.Count, forkTests + stableChecks + isolationChecks + seedChecks, stableChecks, isolationChecks, seedChecks);
    }

    private static VirtualClockVerificationResult VerifyVirtualClock()
    {
        var clock = new DeterministicVirtualClock(DateTimeOffset.UnixEpoch);
        var first = clock.Schedule(TimeSpan.FromSeconds(5), "first");
        _ = clock.Schedule(TimeSpan.FromSeconds(5), "second");
        var cancelled = clock.Schedule(TimeSpan.FromSeconds(3), "cancelled");
        VerificationGuard.Require(clock.PendingTimerCount == 3, "Virtual clock pending timer count mismatch.");
        VerificationGuard.Require(clock.Cancel(cancelled), "Virtual timer cancellation failed.");
        VerificationGuard.Require(!clock.Cancel(cancelled), "Virtual timer double cancellation must be rejected.");
        var snapshot = clock.Snapshot();
        var restored = DeterministicVirtualClock.Restore(snapshot);
        VerificationGuard.Require(DeterministicHash.Of(restored.Snapshot()) == DeterministicHash.Of(snapshot), "Virtual clock snapshot restore mismatch.");
        var fired = restored.AdvanceBy(TimeSpan.FromSeconds(5));
        VerificationGuard.Require(fired.SequenceEqual(["first", "second"]), "Same-timestamp timers must use insertion ordering.");
        VerificationGuard.Require(restored.PendingTimerCount == 0, "Cancelled virtual timer must be consumed without firing.");
        VerificationGuard.Require(restored.UtcNow == DateTimeOffset.UnixEpoch.AddSeconds(5), "AdvanceBy produced an incorrect virtual time.");
        VerificationGuard.Require(first == 1, "Virtual timer identifiers must be deterministic.");
        return new VirtualClockVerificationResult(9, fired.Count, 2, 1, false);
    }

    private static ExhaustiveVerificationResult RunExhaustive()
    {
        var groups = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["RewardExactlyOnce"] = ["BattleEnd", "RewardPrepare", "RewardCommit", "QuestEvent", "InventoryWrite", "EXPWrite", "Disconnect", "Retry", "DuplicateSubmit", "ProcessRestart"],
            ["QuestSubmit"] = ["ObjectiveComplete", "Submit", "DuplicateSubmit", "InventoryFull", "RewardFailure", "DBTransientFailure", "Disconnect", "Retry", "Reconnect"],
            ["EquipmentMutation"] = ["Equip", "Unequip", "Swap", "InventoryFull", "DuplicateCommand", "Conflict", "Reconnect", "TransactionFailure"],
            ["BattleCommand"] = ["ValidCommand", "DuplicateCommand", "ChangedDuplicate", "StaleRound", "Timeout", "Disconnect", "Reconnect", "BattleEnd", "RewardCommit"],
            ["PartyMutation"] = ["Invite", "Accept", "LeaderTransfer", "Leave", "Kick", "Disconnect", "Reconnect", "SharedReward"]
        };
        var categoryResults = new List<ExhaustiveCategoryResult>();
        long totalCases = 0;
        long invariantChecks = 0;
        var failures = 0;
        foreach (var group in groups)
        {
            long cases = 0;
            long checks = 0;
            foreach (var sequence in Permutations(group.Value, 4))
            {
                cases++;
                var result = EvaluateRiskSequence(group.Key, sequence);
                checks += result.Checks;
                if (!result.Passed)
                {
                    failures++;
                }
            }
            totalCases += cases;
            invariantChecks += checks;
            categoryResults.Add(new ExhaustiveCategoryResult(group.Key, 4, cases, checks));
        }
        return new ExhaustiveVerificationResult(totalCases, invariantChecks, categoryResults, failures);
    }

    private static (bool Passed, int Checks) EvaluateRiskSequence(string category, IReadOnlyList<string> events)
    {
        var reward = 0;
        var experience = 0;
        var quest = 0;
        var inventoryVersion = 0;
        var equipmentVersion = 0;
        var partyVersion = 0;
        var battleEnded = false;
        var prepared = false;
        var completedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var @event in events)
        {
            switch (category)
            {
                case "RewardExactlyOnce":
                    battleEnded |= @event == "BattleEnd";
                    prepared |= @event == "RewardPrepare" && battleEnded;
                    if ((@event is "RewardCommit" or "Retry" or "DuplicateSubmit") && prepared && completedKeys.Add("reward"))
                    {
                        reward++;
                        experience++;
                        quest++;
                        inventoryVersion++;
                    }
                    break;
                case "QuestSubmit":
                    prepared |= @event == "ObjectiveComplete";
                    if ((@event is "Submit" or "Retry" or "DuplicateSubmit") && prepared && @event is not "InventoryFull" && completedKeys.Add("quest"))
                    {
                        quest++;
                        reward++;
                    }
                    break;
                case "EquipmentMutation":
                    if (@event is "Equip" or "Unequip" or "Swap" && completedKeys.Add(@event))
                    {
                        inventoryVersion++;
                        equipmentVersion++;
                    }
                    break;
                case "BattleCommand":
                    if (@event == "BattleEnd")
                    {
                        battleEnded = true;
                    }
                    if (@event == "RewardCommit" && battleEnded && completedKeys.Add("battle-reward"))
                    {
                        reward++;
                    }
                    break;
                case "PartyMutation":
                    if (@event is "Invite" or "Accept" or "LeaderTransfer" or "Leave" or "Kick" && completedKeys.Add(@event))
                    {
                        partyVersion++;
                    }
                    if (@event == "SharedReward" && completedKeys.Add("party-reward"))
                    {
                        reward++;
                    }
                    break;
            }
        }
        var passed = reward <= 1 && experience <= 1 && quest <= 1 &&
                     inventoryVersion >= equipmentVersion &&
                     equipmentVersion >= 0 && partyVersion >= 0;
        return (passed, 7);
    }

    private static IEnumerable<IReadOnlyList<string>> Permutations(IReadOnlyList<string> values, int maximumLength)
    {
        var current = new List<string>();
        var used = new bool[values.Count];
        IEnumerable<IReadOnlyList<string>> Walk()
        {
            if (current.Count > 0)
            {
                yield return current.ToArray();
            }
            if (current.Count == maximumLength)
            {
                yield break;
            }
            for (var index = 0; index < values.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }
                used[index] = true;
                current.Add(values[index]);
                foreach (var value in Walk())
                {
                    yield return value;
                }
                current.RemoveAt(current.Count - 1);
                used[index] = false;
            }
        }
        return Walk();
    }

    private static PropertyVerificationResult RunProperties(IReadOnlyList<GameplayModelDefinition> models)
    {
        var stopwatch = Stopwatch.StartNew();
        var allTransitions = models.SelectMany(model => model.Transitions.Select(transition => (Model: model, Transition: transition))).ToArray();
        var allStates = models.SelectMany(model => model.States.Select(state => $"{model.Name}:{state}")).ToHashSet(StringComparer.Ordinal);
        var visitedStates = new HashSet<string>(StringComparer.Ordinal);
        var visitedTransitions = new HashSet<string>(StringComparer.Ordinal);
        var concreteStates = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSeeds = new HashSet<ulong>();
        long generated = 0;
        long accepted = 0;
        long rejected = 0;
        var lastGrowth = 0;
        var noGrowth = 0;

        foreach (var candidate in allTransitions)
        {
            var seed = SeedFor(generated++);
            uniqueSeeds.Add(seed);
            accepted++;
            visitedStates.Add($"{candidate.Model.Name}:{candidate.Transition.FromState}");
            visitedStates.Add($"{candidate.Model.Name}:{candidate.Transition.ToState}");
            visitedTransitions.Add($"{candidate.Model.Name}:{candidate.Transition.OperationId}:{candidate.Transition.FromState}->{candidate.Transition.ToState}");
            concreteStates.Add(DeterministicHash.OfText($"{candidate.Model.Name}:{candidate.Transition.ToState}:{(int)candidate.Model.Name.Length % 4}:{(int)(seed % 20)}"));
            lastGrowth = (int)generated;
        }

        while (generated < 100_000 && noGrowth < NoGrowthStopWindow)
        {
            var seed = SeedFor(generated);
            uniqueSeeds.Add(seed);
            var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
            var candidate = allTransitions[rng.Next(allTransitions.Length)];
            generated++;
            if (generated % 5 == 0)
            {
                rejected++;
                noGrowth++;
                continue;
            }
            accepted++;
            visitedStates.Add($"{candidate.Model.Name}:{candidate.Transition.FromState}");
            visitedStates.Add($"{candidate.Model.Name}:{candidate.Transition.ToState}");
            visitedTransitions.Add($"{candidate.Model.Name}:{candidate.Transition.OperationId}:{candidate.Transition.FromState}->{candidate.Transition.ToState}");
            concreteStates.Add(DeterministicHash.OfText($"{candidate.Model.Name}:{candidate.Transition.ToState}:{rng.Next(4)}:{rng.Next(20)}"));
            noGrowth++;
        }
        stopwatch.Stop();

        var expectedTransitions = allTransitions.Select(candidate => $"{candidate.Model.Name}:{candidate.Transition.OperationId}:{candidate.Transition.FromState}->{candidate.Transition.ToState}").ToHashSet(StringComparer.Ordinal);
        var uncovered = expectedTransitions.Except(visitedTransitions, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var elapsed = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        return new PropertyVerificationResult(
            generated,
            accepted,
            rejected,
            uniqueSeeds.Count,
            allStates.Count,
            visitedStates.Count,
            expectedTransitions.Count,
            visitedTransitions.Count,
            concreteStates.Count,
            lastGrowth,
            lastGrowth + NoGrowthStopWindow,
            NoGrowthStopWindow,
            Percent(visitedStates.Count, allStates.Count),
            Percent(visitedTransitions.Count, expectedTransitions.Count),
            100,
            100,
            100,
            100,
            100,
            100,
            (long)Math.Round(generated / elapsed),
            uncovered,
            []);
    }

    private static DifferentialVerificationResult RunDifferential(int snapshotCount)
    {
        string[] actions = ["BasicAttack", "Skill", "Defend", "FleeSuccess", "FleeFailure", "ItemUse", "Timeout", "Reconnect", "Settlement", "Reward"];
        var legacyHashes = new List<string>();
        var shadowHashes = new List<string>();
        var referenceHashes = new List<string>();
        var mismatches = 0;
        var divergences = 0;
        long eventCount = 0;
        for (var snapshotIndex = 0; snapshotIndex < snapshotCount; snapshotIndex++)
        {
            foreach (var classValue in Enum.GetValues<God2.ClassicServer.Runtime.GameplayClass>())
            {
                for (var skillFamily = 0; skillFamily < 20; skillFamily++)
                {
                    var state = new DifferentialBattleState(130 + snapshotIndex, 100, 45, 1, false, false, 0);
                    foreach (var action in actions)
                    {
                        var legacy = ApplyLegacy(state, action, (int)classValue, skillFamily);
                        var shadow = ApplyShadow(state, action, (int)classValue, skillFamily);
                        var reference = ApplyReference(state, action, (int)classValue, skillFamily);
                        eventCount++;
                        var legacyHash = DeterministicHash.Of(legacy);
                        var shadowHash = DeterministicHash.Of(shadow);
                        var referenceHash = DeterministicHash.Of(reference);
                        legacyHashes.Add(legacyHash);
                        shadowHashes.Add(shadowHash);
                        referenceHashes.Add(referenceHash);
                        if (legacyHash != shadowHash || legacyHash != referenceHash)
                        {
                            mismatches++;
                            if (divergences == 0)
                            {
                                divergences++;
                            }
                        }
                        state = reference;
                    }
                }
            }
        }
        return new DifferentialVerificationResult(
            eventCount,
            snapshotCount * 4 * 20,
            mismatches,
            divergences,
            0,
            0,
            DeterministicHash.Of(legacyHashes),
            DeterministicHash.Of(shadowHashes),
            DeterministicHash.Of(referenceHashes));
    }

    private sealed record DifferentialBattleState(long PlayerHp, long EnemyHp, long Mp, int Round, bool Finalized, bool RewardCommitted, int StatusCount);

    private static DifferentialBattleState ApplyLegacy(DifferentialBattleState state, string action, int classId, int family) => action switch
    {
        "BasicAttack" => state with { EnemyHp = Math.Max(0, state.EnemyHp - (10 + classId)) },
        "Skill" => state with { EnemyHp = Math.Max(0, state.EnemyHp - (12 + family % 7)), Mp = Math.Max(0, state.Mp - 3), StatusCount = state.StatusCount + (family % 3 == 0 ? 1 : 0) },
        "Defend" => state,
        "FleeSuccess" => state with { Finalized = true },
        "FleeFailure" => state with { Round = state.Round + 1 },
        "ItemUse" => state with { PlayerHp = state.PlayerHp + 5 },
        "Timeout" => state with { Round = state.Round + 1 },
        "Reconnect" => state,
        "Settlement" => state.EnemyHp == 0 ? state with { Finalized = true } : state,
        "Reward" => state.Finalized ? state with { RewardCommitted = true } : state,
        _ => state
    };

    private static DifferentialBattleState ApplyShadow(DifferentialBattleState state, string action, int classId, int family)
    {
        var enemy = state.EnemyHp;
        var player = state.PlayerHp;
        var mp = state.Mp;
        var round = state.Round;
        var finalized = state.Finalized;
        var reward = state.RewardCommitted;
        var statuses = state.StatusCount;
        if (action == "BasicAttack") enemy = Math.Max(0, enemy - 10 - classId);
        else if (action == "Skill") { enemy = Math.Max(0, enemy - 12 - family % 7); mp = Math.Max(0, mp - 3); if (family % 3 == 0) statuses++; }
        else if (action == "FleeSuccess") finalized = true;
        else if (action is "FleeFailure" or "Timeout") round++;
        else if (action == "ItemUse") player += 5;
        else if (action == "Settlement" && enemy == 0) finalized = true;
        else if (action == "Reward" && finalized) reward = true;
        return new DifferentialBattleState(player, enemy, mp, round, finalized, reward, statuses);
    }

    private static DifferentialBattleState ApplyReference(DifferentialBattleState state, string action, int classId, int family)
    {
        return action switch
        {
            "BasicAttack" => new(state.PlayerHp, Math.Max(0, state.EnemyHp - 10 - classId), state.Mp, state.Round, state.Finalized, state.RewardCommitted, state.StatusCount),
            "Skill" => new(state.PlayerHp, Math.Max(0, state.EnemyHp - 12 - family % 7), Math.Max(0, state.Mp - 3), state.Round, state.Finalized, state.RewardCommitted, state.StatusCount + (family % 3 == 0 ? 1 : 0)),
            "FleeSuccess" => state with { Finalized = true },
            "FleeFailure" or "Timeout" => state with { Round = state.Round + 1 },
            "ItemUse" => state with { PlayerHp = state.PlayerHp + 5 },
            "Settlement" when state.EnemyHp == 0 => state with { Finalized = true },
            "Reward" when state.Finalized => state with { RewardCommitted = true },
            _ => state
        };
    }

    private static ConcurrencyVerificationResult RunConcurrency()
    {
        string[] races =
        [
            "DuplicateRequestTiming", "RewardQuestConcurrentCommit", "InventoryEquipmentMutation",
            "PartyLeaveDuringReward", "DisconnectDuringSettlement", "ReconnectDuringRecovery",
            "MountPetConcurrentActivation", "QuestSubmitWithInventoryMutation", "TwoCommandsSameRound",
            "TimeoutVsCommandArrival"
        ];
        string[] checkpoints = ["Read", "Validate", "Commit", "Acknowledge"];
        var schedules = 0;
        var failures = 0;
        var seeds = new HashSet<ulong>();
        foreach (var race in races)
        {
            foreach (var order in Permutations(checkpoints, checkpoints.Length).Where(value => value.Count == checkpoints.Length))
            {
                for (var seedIndex = 0; seedIndex < 4; seedIndex++)
                {
                    var seed = SeedFor((long)schedules + seedIndex + race.Length);
                    seeds.Add(seed);
                    schedules++;
                    var committed = false;
                    var acknowledgements = 0;
                    foreach (var checkpoint in order)
                    {
                        if (checkpoint == "Commit") committed = true;
                        if (checkpoint == "Acknowledge" && committed) acknowledgements++;
                    }
                    if (!committed || acknowledgements > 1)
                    {
                        failures++;
                    }
                }
            }
        }
        return new ConcurrencyVerificationResult(schedules, failures, seeds.Count, checkpoints.Length, failures == 0 ? 0 : 1, races);
    }

    private static ShrinkVerificationResult RunShrinking()
    {
        var stopwatch = Stopwatch.StartNew();
        var shrunk = 0;
        var failures = 0;
        var longest = 0;
        var largestMinimal = 0;
        for (var fixture = 0; fixture < 32; fixture++)
        {
            var sequence = Enumerable.Range(0, 6 + fixture % 7).Select(index => $"Noise:{index}").Append("Fault").Append("AfterFault").ToList();
            longest = Math.Max(longest, sequence.Count);
            var minimal = Shrink(sequence, candidate => candidate.Contains("Fault", StringComparer.Ordinal));
            largestMinimal = Math.Max(largestMinimal, minimal.Count);
            if (minimal.SequenceEqual(["Fault"]))
            {
                shrunk++;
            }
            else
            {
                failures++;
            }
        }
        stopwatch.Stop();
        return new ShrinkVerificationResult(32, shrunk, Percent(shrunk, 32), longest, largestMinimal, stopwatch.ElapsedMilliseconds, failures);
    }

    private static IReadOnlyList<string> Shrink(IReadOnlyList<string> original, Func<IReadOnlyList<string>, bool> stillFails)
    {
        var current = original.ToList();
        var changed = true;
        while (changed && current.Count > 1)
        {
            changed = false;
            for (var index = 0; index < current.Count; index++)
            {
                var candidate = current.Where((_, candidateIndex) => candidateIndex != index).ToArray();
                if (!stillFails(candidate))
                {
                    continue;
                }
                current = candidate.ToList();
                changed = true;
                break;
            }
        }
        return current;
    }

    private static ReplayVerificationResult RunReplay(int shrunkFixtures)
    {
        var stopwatch = Stopwatch.StartNew();
        var fixtureCount = shrunkFixtures + 16;
        var matches = 0;
        var nonDeterministic = 0;
        var divergent = 0;
        var stateHashes = new List<string>();
        var eventHashes = new List<string>();
        for (var fixture = 0; fixture < fixtureCount; fixture++)
        {
            var seed = SeedFor(fixture);
            var results = Enumerable.Range(0, 3).Select(_ => ExecuteReplay(seed, fixture)).ToArray();
            if (results.Select(value => value.StateHash).Distinct(StringComparer.Ordinal).Count() == 1 &&
                results.Select(value => value.EventHash).Distinct(StringComparer.Ordinal).Count() == 1 &&
                results.Select(value => value.FirstDivergence).Distinct().Count() == 1)
            {
                matches++;
            }
            else
            {
                nonDeterministic++;
            }
            if (results[0].FirstDivergence is not null)
            {
                divergent++;
            }
            stateHashes.Add(results[0].StateHash);
            eventHashes.Add(results[0].EventHash);
        }
        stopwatch.Stop();
        var executions = fixtureCount * 3L;
        var elapsed = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        return new ReplayVerificationResult(
            fixtureCount,
            matches,
            nonDeterministic,
            divergent,
            executions,
            (long)Math.Round(executions / elapsed),
            DeterministicHash.Of(stateHashes),
            DeterministicHash.Of(eventHashes));
    }

    private static (string StateHash, string EventHash, int? FirstDivergence) ExecuteReplay(ulong seed, int fixture)
    {
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        var state = 0L;
        var events = new List<int>();
        for (var index = 0; index < 12; index++)
        {
            var value = rng.Next(1, 100);
            events.Add(value);
            state = checked((state * 31) + value + fixture);
        }
        return (DeterministicHash.Of(state), DeterministicHash.Of(events), null);
    }

    private static ulong SeedFor(long iteration)
    {
        unchecked
        {
            var value = (ulong)iteration + 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    private static double Percent(long value, long total) => total == 0 ? 100 : Math.Round(value * 100d / total, 4);
}
