using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.AdvancedHeadlessVerification;

public sealed record ResourceSample(
    DateTimeOffset ObservedAtUtc,
    double ElapsedSeconds,
    long WorkingSetBytes,
    long GcHeapBytes,
    long LohBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadCount,
    int HandleCount,
    int DbConnectionCount,
    int ActiveSessionCount,
    int ActiveBattleActorCount,
    int QueueDepth,
    int OutboxBacklog,
    int JournalBacklog,
    int PendingTimerCount,
    int KeyedLockCount,
    int KeyedLockReferenceCount,
    int IdempotencyCacheSize,
    int TransactionStateCount,
    int TemporaryFileCount,
    long CompletedGameplayLoops);

public sealed record ResourceSoakResult(
    string Status,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    double RequestedDurationSeconds,
    double DurationSeconds,
    int WorkerCount,
    long Seed,
    IReadOnlyList<string> ScenarioManifest,
    int SampleCount,
    long CompletedGameplayLoops,
    ResourceSample Baseline,
    ResourceSample Peak,
    ResourceSample Final,
    double WorkingSetSlopeBytesPerMinute,
    double HeapSlopeBytesPerMinute,
    double LohSlopeBytesPerMinute,
    string WorkingSetTrend,
    string HeapTrend,
    string LohTrend,
    string ThreadTrend,
    string HandleTrend,
    string DbConnectionTrend,
    string KeyedLockTrend,
    string QueueOutboxJournalTrend,
    string PostDrainRecovery,
    int UnhandledExceptionCount,
    int GameplayFailureCount,
    int DuplicateRewardCount,
    int DuplicateQuestCompletionCount,
    int InventoryDriftCount,
    int EquipmentDriftCount,
    int PartyDriftCount,
    int MountPetDriftCount,
    int FinalActiveActorCount,
    int FinalOrphanSessionCount,
    int DbConnectionLeakCount,
    int BattleActorLeakCount,
    int SessionLeakCount,
    double CasesPerSecond,
    IReadOnlyList<ResourceSample> Samples)
{
    public long PeakWorkingSetBytes => Peak.WorkingSetBytes;
    public long PeakGcHeapBytes => Peak.GcHeapBytes;
    public long PeakLohBytes => Peak.LohBytes;
}

public sealed record ResourceSoakLiveSnapshot(
    int ActiveSessionCount,
    int ActiveBattleActorCount,
    int QueueDepth,
    int OutboxBacklog,
    int JournalBacklog,
    int PendingTimerCount,
    int KeyedLockCount,
    int KeyedLockReferenceCount,
    int IdempotencyCacheSize,
    int TransactionStateCount);

public sealed class ResourceSoakLiveDiagnostics
{
    private int _activeSessions;
    private int _activeBattleActors;
    private int _queueDepth;
    private int _outboxBacklog;
    private int _journalBacklog;
    private int _pendingTimers;
    private int _keyedLocks;
    private int _keyedLockReferences;
    private int _idempotencyCacheSize;
    private int _transactionStates;
    private int _peakActiveSessions;
    private int _peakActiveBattleActors;
    private int _peakQueueDepth;
    private int _peakOutboxBacklog;
    private int _peakJournalBacklog;
    private int _peakPendingTimers;
    private int _peakKeyedLocks;
    private int _peakKeyedLockReferences;
    private int _peakIdempotencyCacheSize;
    private int _peakTransactionStates;

    public IDisposable TrackSession() => Increment(
        ref _activeSessions, ref _peakActiveSessions, () => Interlocked.Decrement(ref _activeSessions));
    public IDisposable TrackBattleActor() => Increment(
        ref _activeBattleActors, ref _peakActiveBattleActors, () => Interlocked.Decrement(ref _activeBattleActors));
    public IDisposable TrackQueueWork() => Increment(
        ref _queueDepth, ref _peakQueueDepth, () => Interlocked.Decrement(ref _queueDepth));
    public IDisposable TrackOutbox(int count) => Add(
        count, ref _outboxBacklog, ref _peakOutboxBacklog, () => Interlocked.Add(ref _outboxBacklog, -count));
    public IDisposable TrackJournal(int count) => Add(
        count, ref _journalBacklog, ref _peakJournalBacklog, () => Interlocked.Add(ref _journalBacklog, -count));
    public IDisposable TrackPendingTimer() => Increment(
        ref _pendingTimers, ref _peakPendingTimers, () => Interlocked.Decrement(ref _pendingTimers));
    public IDisposable TrackKeyedLocks(int locks, int references) => new CompositeLease(
        Add(locks, ref _keyedLocks, ref _peakKeyedLocks, () => Interlocked.Add(ref _keyedLocks, -locks)),
        Add(references, ref _keyedLockReferences, ref _peakKeyedLockReferences,
            () => Interlocked.Add(ref _keyedLockReferences, -references)));
    public IDisposable TrackIdempotency(int count) => Add(
        count, ref _idempotencyCacheSize, ref _peakIdempotencyCacheSize,
        () => Interlocked.Add(ref _idempotencyCacheSize, -count));
    public IDisposable TrackTransaction() => Increment(
        ref _transactionStates, ref _peakTransactionStates, () => Interlocked.Decrement(ref _transactionStates));

    public ResourceSoakLiveSnapshot Snapshot() => new(
        Math.Max(0, Volatile.Read(ref _activeSessions)),
        Math.Max(0, Volatile.Read(ref _activeBattleActors)),
        Math.Max(0, Volatile.Read(ref _queueDepth)),
        Math.Max(0, Volatile.Read(ref _outboxBacklog)),
        Math.Max(0, Volatile.Read(ref _journalBacklog)),
        Math.Max(0, Volatile.Read(ref _pendingTimers)),
        Math.Max(0, Volatile.Read(ref _keyedLocks)),
        Math.Max(0, Volatile.Read(ref _keyedLockReferences)),
        Math.Max(0, Volatile.Read(ref _idempotencyCacheSize)),
        Math.Max(0, Volatile.Read(ref _transactionStates)));

    public ResourceSoakLiveSnapshot PeakSnapshot() => new(
        Math.Max(0, Volatile.Read(ref _peakActiveSessions)),
        Math.Max(0, Volatile.Read(ref _peakActiveBattleActors)),
        Math.Max(0, Volatile.Read(ref _peakQueueDepth)),
        Math.Max(0, Volatile.Read(ref _peakOutboxBacklog)),
        Math.Max(0, Volatile.Read(ref _peakJournalBacklog)),
        Math.Max(0, Volatile.Read(ref _peakPendingTimers)),
        Math.Max(0, Volatile.Read(ref _peakKeyedLocks)),
        Math.Max(0, Volatile.Read(ref _peakKeyedLockReferences)),
        Math.Max(0, Volatile.Read(ref _peakIdempotencyCacheSize)),
        Math.Max(0, Volatile.Read(ref _peakTransactionStates)));

    private static IDisposable Increment(ref int counter, ref int peak, Action release)
    {
        var current = Interlocked.Increment(ref counter);
        UpdatePeak(ref peak, current);
        return new CounterLease(release);
    }

    private static IDisposable Add(int count, ref int counter, ref int peak, Action release)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return EmptyLease.Instance;
        var current = Interlocked.Add(ref counter, count);
        UpdatePeak(ref peak, current);
        return new CounterLease(release);
    }

    private static void UpdatePeak(ref int peak, int value)
    {
        var observed = Volatile.Read(ref peak);
        while (value > observed)
        {
            var original = Interlocked.CompareExchange(ref peak, value, observed);
            if (original == observed) return;
            observed = original;
        }
    }

    private sealed class CounterLease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class CompositeLease(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        public static EmptyLease Instance { get; } = new();
        public void Dispose() { }
    }
}

public sealed class LowConcurrencyResourceSoak
{
    public static IReadOnlyList<string> Scenarios { get; } =
    [
        "BattleSettlement", "Reward", "QuestCompletion", "InventoryMutation", "Equipment",
        "ExperienceLevelUp", "Party", "Mount", "Pet", "Reconnect", "Recovery",
        "DuplicateCommand", "OutboxDrain", "JournalRecovery", "CrossModuleTransaction",
        "WorldChatBoundedRetention"
    ];

    public async Task<ResourceSoakResult> RunAsync(
        string repositoryRoot,
        TimeSpan duration,
        int workerCount = 2,
        long seed = 0x474F44324652455A,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (workerCount is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(workerCount));
        var settings = MariaDbSettings.Load(repositoryRoot);
        var connectionString = settings.ConnectionString(workerCount + 8);
        var process = Process.GetCurrentProcess();
        var samples = new List<ResourceSample>();
        var live = new ResourceSoakLiveDiagnostics();
        var startTime = DateTimeOffset.UtcNow;
        var started = Stopwatch.StartNew();
        var unhandledExceptions = 0;
        var gameplayFailures = 0;
        var duplicateRewards = 0;
        var duplicateQuestCompletions = 0;
        var inventoryDrift = 0;
        var equipmentDrift = 0;
        var partyDrift = 0;
        var mountPetDrift = 0;
        long completedLoops = 0;
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"god2-affr-soak-{Environment.ProcessId}-{startTime:yyyyMMddHHmmss}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var workers = Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
            {
                using var sessionLease = live.TrackSession();
                var chatCharacterId = Guid.NewGuid();
                var chat = new ServerOwnedChatRuntime(
                    _ => null,
                    burstLimit: 100,
                    burstWindow: TimeSpan.FromMilliseconds(1),
                    maximumInboxMessagesPerParticipant: 64,
                    maximumCompletedCommands: 128);
                chat.RegisterOrUpdate(new ServerChatParticipant(
                    chatCharacterId,
                    $"Soak{worker}",
                    Online: true));
                try
                {
                    await using var connection = new MySqlConnection(connectionString);
                    await connection.OpenAsync(workerCancellation.Token);
                    var iteration = 0L;
                    while (!workerCancellation.IsCancellationRequested)
                    {
                        using var queueLease = live.TrackQueueWork();
                        var store = new InMemoryHeadlessGameplaySnapshotStore();
                        var session = new HeadlessGameplaySession(
                            $"soak-{worker}-{iteration}",
                            800_000 + worker,
                            (GameplayClass)(worker % 4),
                            ConservativeProgressionCatalog.CreateDefault(),
                            [new MountDefinition(1, 12_000, 100, 10, new HashSet<int>(), "SoakFixture")],
                            [new PetDefinition(1, 1, 100, [1000], "SoakFixture")],
                            store);
                        var ports = new HeadlessGameplayLoopPorts(
                            _ => Task.FromResult(true), _ => Task.FromResult(true), _ => Task.FromResult(true),
                            _ => Task.FromResult(true), _ => Task.FromResult(true), _ => Task.FromResult(true),
                            _ => Task.FromResult(true));
                        HeadlessGameplayLoopResult loop;
                        using (live.TrackBattleActor())
                        {
                            loop = await session.ExecuteLoopAsync(ports, 100, workerCancellation.Token);
                        }
                        if (!loop.Succeeded || loop.FakeNetworkBytes != 0 || session.FakeNetworkBytes != 0)
                        {
                            Interlocked.Increment(ref gameplayFailures);
                            throw new InvalidOperationException("Soak gameplay loop failed its invariant checks.");
                        }
                        var moduleChecks = ExercisePartyMountPetEquipment(session, seed, worker, iteration);
                        if (!moduleChecks.Equipment)
                        {
                            Interlocked.Increment(ref equipmentDrift);
                            throw new InvalidOperationException("Equipment soak transition drifted.");
                        }
                        if (!moduleChecks.Party)
                        {
                            Interlocked.Increment(ref partyDrift);
                            throw new InvalidOperationException("Party soak transition drifted.");
                        }
                        if (!moduleChecks.MountPet)
                        {
                            Interlocked.Increment(ref mountPetDrift);
                            throw new InvalidOperationException("Mount or pet soak transition drifted.");
                        }
                        session.Save();
                        var restored = HeadlessGameplaySession.Reconnect(session.SessionId, ConservativeProgressionCatalog.CreateDefault(),
                            [new MountDefinition(1, 12_000, 100, 10, new HashSet<int>(), "SoakFixture")],
                            [new PetDefinition(1, 1, 100, [1000], "SoakFixture")], store);
                        if (DeterministicHash.Of(restored.Progression.Snapshot) != DeterministicHash.Of(session.Progression.Snapshot))
                        {
                            Interlocked.Increment(ref inventoryDrift);
                            throw new InvalidOperationException("Soak reconnect drifted.");
                        }
                        if (DeterministicHash.Of(restored.Mount.Snapshot) != DeterministicHash.Of(session.Mount.Snapshot) ||
                            DeterministicHash.Of(restored.Pet.Snapshot) != DeterministicHash.Of(session.Pet.Snapshot))
                        {
                            Interlocked.Increment(ref mountPetDrift);
                            throw new InvalidOperationException("Soak reconnect drifted mount or pet state.");
                        }

                        var chatResult = chat.Send(new ServerChatCommand(
                            $"soak-chat:{worker}:{iteration}",
                            chatCharacterId,
                            ServerChatChannel.World,
                            $"message-{iteration}"));
                        if (chatResult.ResultCode != ServerChatResultCode.Success ||
                            chat.Inbox(chatCharacterId).Count > 64 ||
                            chat.CompletedCommandCount > 128)
                        {
                            throw new InvalidOperationException("World chat retention exceeded its configured bound.");
                        }

                        using (live.TrackTransaction())
                        {
                            var transactionStore = CreateCrossModuleStore(worker);
                            // The production-safe coordinator default deliberately blocks unverified
                            // progression grants. This workload uses a synthetic, internally verified
                            // fixture and must opt in explicitly so the soak exercises the full commit,
                            // recovery, deduplication, and cleanup paths instead of failing at admission.
                            var coordinator = new CrossModuleGameplayTransactionCoordinator(
                                transactionStore,
                                new ConservativeCrossModuleProgressionPolicy());
                            var request = CreateTransactionRequest(seed, worker, iteration);
                            if ((iteration & 1) == 1)
                            {
                                transactionStore.FailurePoint = CrossModuleFailurePoint.CrashAfterPrepare;
                            }
                            var firstReceipt = await coordinator.CommitAsync(request, workerCancellation.Token);
                            if (firstReceipt.Code == CrossModuleTransactionResultCode.RecoveryRequired)
                            {
                                transactionStore.FailurePoint = CrossModuleFailurePoint.None;
                                var preparedDiagnostics = transactionStore.GetDiagnostics();
                                IReadOnlyList<CrossModuleTransactionReceipt> recovered;
                                using (live.TrackJournal(preparedDiagnostics.PreparedTransactionCount))
                                {
                                    recovered = transactionStore.Recover();
                                }
                                if (recovered.Count != 1 || recovered[0].Code != CrossModuleTransactionResultCode.Committed)
                                {
                                    throw new InvalidOperationException("Cross-module recovery did not commit exactly once.");
                                }
                            }
                            else if (firstReceipt.Code != CrossModuleTransactionResultCode.Committed)
                            {
                                throw new InvalidOperationException("Cross-module transaction was not committed.");
                            }
                            var duplicate = await coordinator.CommitAsync(request, workerCancellation.Token);
                            if (duplicate.Code != CrossModuleTransactionResultCode.DuplicateCommitted)
                            {
                                throw new InvalidOperationException("Cross-module duplicate was not deduplicated.");
                            }
                            var finalState = transactionStore.Snapshot();
                            using (live.TrackOutbox(finalState.Outbox.Count))
                            {
                                if (finalState.RewardCommitCount != 1) Interlocked.Increment(ref duplicateRewards);
                                if (finalState.CompletedQuests.Count != 1) Interlocked.Increment(ref duplicateQuestCompletions);
                                if (finalState.Inventory.GetValueOrDefault(request.ItemId) != request.ItemQuantity) Interlocked.Increment(ref inventoryDrift);
                                var diagnostics = transactionStore.GetDiagnostics();
                                using var idempotencyLease = live.TrackIdempotency(diagnostics.ReceiptCount);
                                using var keyedLease = live.TrackKeyedLocks(diagnostics.KeyedLockCount, diagnostics.KeyedLockReferenceCount);
                                if (diagnostics.PreparedTransactionCount != 0 || diagnostics.TransactionGateHolderCount != 0 ||
                                    diagnostics.KeyedLockCount != 0 || diagnostics.KeyedLockReferenceCount != 0)
                                {
                                    throw new InvalidOperationException("Cross-module transaction left recoverable state or lock residue.");
                                }
                            }
                        }

                        if (Volatile.Read(ref duplicateRewards) != 0 || Volatile.Read(ref duplicateQuestCompletions) != 0 ||
                            Volatile.Read(ref inventoryDrift) != 0)
                        {
                            throw new InvalidOperationException("Exactly-once or state drift invariant failed.");
                        }
                        iteration++;
                        Interlocked.Increment(ref completedLoops);
                        if (iteration % 25 == 0)
                        {
                            await using var command = connection.CreateCommand();
                            command.CommandText = "SELECT 1;";
                            _ = await command.ExecuteScalarAsync(workerCancellation.Token);
                        }
                        using (live.TrackPendingTimer())
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(20), workerCancellation.Token);
                        }
                    }
                }
                catch (OperationCanceledException) when (workerCancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref unhandledExceptions);
                    Console.Error.WriteLine($"SOAK_WORKER_FAILURE worker={worker} type={exception.GetType().Name} reason={SafeFailureReason(exception)}");
                    workerCancellation.Cancel();
                }
                finally
                {
                    chat.Unregister(chatCharacterId);
                    if (chat.ParticipantCount != 0 || chat.Inbox(chatCharacterId).Count != 0)
                    {
                        throw new InvalidOperationException("World chat participant state survived unregister.");
                    }
                }
            }, workerCancellation.Token)).ToArray();

            var sampleInterval = duration < TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);
            samples.Add(await CaptureAsync(process, started.Elapsed, connectionString, live.Snapshot(), completedLoops,
                temporaryRoot, cancellationToken));
            while (started.Elapsed < duration && !workerCancellation.IsCancellationRequested)
            {
                var remaining = duration - started.Elapsed;
                await Task.Delay(remaining < sampleInterval ? remaining : sampleInterval, cancellationToken);
                samples.Add(await CaptureAsync(process, started.Elapsed, connectionString, live.Snapshot(), completedLoops,
                    temporaryRoot, cancellationToken));
                Console.WriteLine($"SOAK_PROGRESS elapsed={started.Elapsed:c} loops={Volatile.Read(ref completedLoops)} workingSet={samples[^1].WorkingSetBytes} heap={samples[^1].GcHeapBytes} threads={samples[^1].ThreadCount} handles={samples[^1].HandleCount}");
            }

            workerCancellation.Cancel();
            await Task.WhenAll(workers);
            MySqlConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            samples.Add(await CaptureAsync(process, started.Elapsed, connectionString, live.Snapshot(), completedLoops,
                temporaryRoot, cancellationToken));
            started.Stop();
            var endTime = DateTimeOffset.UtcNow;

            var baseline = samples[0];
            var final = samples[^1];
            var livePeak = live.PeakSnapshot();
            var peak = new ResourceSample(
                samples.OrderByDescending(value => value.WorkingSetBytes).First().ObservedAtUtc,
                samples.Max(value => value.ElapsedSeconds),
                samples.Max(value => value.WorkingSetBytes),
                samples.Max(value => value.GcHeapBytes),
                samples.Max(value => value.LohBytes),
                samples.Max(value => value.Gen0Collections),
                samples.Max(value => value.Gen1Collections),
                samples.Max(value => value.Gen2Collections),
                samples.Max(value => value.ThreadCount),
                samples.Max(value => value.HandleCount),
                samples.Max(value => value.DbConnectionCount),
                Math.Max(samples.Max(value => value.ActiveSessionCount), livePeak.ActiveSessionCount),
                Math.Max(samples.Max(value => value.ActiveBattleActorCount), livePeak.ActiveBattleActorCount),
                Math.Max(samples.Max(value => value.QueueDepth), livePeak.QueueDepth),
                Math.Max(samples.Max(value => value.OutboxBacklog), livePeak.OutboxBacklog),
                Math.Max(samples.Max(value => value.JournalBacklog), livePeak.JournalBacklog),
                Math.Max(samples.Max(value => value.PendingTimerCount), livePeak.PendingTimerCount),
                Math.Max(samples.Max(value => value.KeyedLockCount), livePeak.KeyedLockCount),
                Math.Max(samples.Max(value => value.KeyedLockReferenceCount), livePeak.KeyedLockReferenceCount),
                Math.Max(samples.Max(value => value.IdempotencyCacheSize), livePeak.IdempotencyCacheSize),
                Math.Max(samples.Max(value => value.TransactionStateCount), livePeak.TransactionStateCount),
                samples.Max(value => value.TemporaryFileCount),
                samples.Max(value => value.CompletedGameplayLoops));
            var analysisSamples = duration >= TimeSpan.FromMinutes(30)
                ? samples.Where(value => value.ElapsedSeconds >= Math.Min(900, duration.TotalSeconds * 0.20)).ToArray()
                : samples.ToArray();
            var workingSlope = SlopePerMinute(analysisSamples.Select(value => (value.ElapsedSeconds, (double)value.WorkingSetBytes)).ToArray());
            var heapSlope = SlopePerMinute(analysisSamples.Select(value => (value.ElapsedSeconds, (double)value.GcHeapBytes)).ToArray());
            var lohSlope = SlopePerMinute(analysisSamples.Select(value => (value.ElapsedSeconds, (double)value.LohBytes)).ToArray());
            var threadLeak = Math.Max(0, final.ThreadCount - baseline.ThreadCount - 2);
            var handleLeak = Math.Max(0, final.HandleCount - baseline.HandleCount - 8);
            var dbLeak = Math.Max(0, final.DbConnectionCount - baseline.DbConnectionCount);
            var battleActorLeak = Math.Max(0, final.ActiveBattleActorCount);
            var sessionLeak = Math.Max(0, final.ActiveSessionCount);
            var longRun = duration >= TimeSpan.FromMinutes(30);
            var workingTrend = longRun
                ? workingSlope <= 1_048_576 ? "Stable" : "Growing"
                : final.WorkingSetBytes <= Math.Max(1, peak.WorkingSetBytes) * 1.10 ? "Stable" : "Growing";
            var heapTrend = longRun
                ? heapSlope <= 524_288 ? "Stable" : "Growing"
                : final.GcHeapBytes <= Math.Max(1, peak.GcHeapBytes) * 1.10 ? "Stable" : "Growing";
            var lohTrend = longRun
                ? lohSlope <= 262_144 ? "Stable" : "Growing"
                : final.LohBytes <= Math.Max(1, peak.LohBytes) * 1.10 ? "Stable" : "Growing";
            var threadTrend = threadLeak == 0 ? "ReturnedToBaseline" : "Growing";
            var handleTrend = handleLeak == 0 ? "ReturnedToBaseline" : "Growing";
            var dbTrend = dbLeak == 0 ? "ReturnedToBaseline" : "Growing";
            var keyedTrend = final.KeyedLockCount == 0 && final.KeyedLockReferenceCount == 0 ? "Drained" : "Growing";
            var queueTrend = final.QueueDepth == 0 && final.OutboxBacklog == 0 && final.JournalBacklog == 0 && final.PendingTimerCount == 0
                ? "NoPermanentBacklog" : "Growing";
            var postDrain = final.ActiveSessionCount == 0 && final.ActiveBattleActorCount == 0 && final.TransactionStateCount == 0 &&
                final.IdempotencyCacheSize == 0 && final.TemporaryFileCount == 0 && queueTrend == "NoPermanentBacklog"
                ? "Recovered" : "ResidueDetected";
            var activityObserved = peak.ActiveSessionCount > 0 && peak.ActiveBattleActorCount > 0 && peak.QueueDepth > 0 &&
                peak.OutboxBacklog > 0 && peak.JournalBacklog > 0 && peak.PendingTimerCount > 0 &&
                peak.IdempotencyCacheSize > 0 && peak.TransactionStateCount > 0;
            var ranLongEnough = started.Elapsed.TotalSeconds + 0.001 >= duration.TotalSeconds;
            var status = ranLongEnough && unhandledExceptions == 0 && gameplayFailures == 0 && duplicateRewards == 0 &&
                duplicateQuestCompletions == 0 && inventoryDrift == 0 && equipmentDrift == 0 && partyDrift == 0 && mountPetDrift == 0 &&
                workingTrend == "Stable" && heapTrend == "Stable" && lohTrend == "Stable" && threadLeak == 0 && handleLeak == 0 &&
                dbLeak == 0 && battleActorLeak == 0 && sessionLeak == 0 && keyedTrend == "Drained" && postDrain == "Recovered" &&
                activityObserved ? "PASS" : "FAIL";
            Console.WriteLine($"SOAK_ANALYSIS status={status} working={workingTrend} heap={heapTrend} loh={lohTrend} threadLeak={threadLeak} handleLeak={handleLeak} dbLeak={dbLeak} actors={battleActorLeak} sessions={sessionLeak} queue={queueTrend} activityObserved={activityObserved}");
            var result = new ResourceSoakResult(
                status, startTime, endTime, duration.TotalSeconds, started.Elapsed.TotalSeconds, workerCount, seed, Scenarios,
                samples.Count, completedLoops, baseline, peak, final, workingSlope, heapSlope, lohSlope,
                workingTrend, heapTrend, lohTrend, threadTrend, handleTrend, dbTrend, keyedTrend, queueTrend, postDrain,
                unhandledExceptions, gameplayFailures, duplicateRewards, duplicateQuestCompletions, inventoryDrift,
                equipmentDrift, partyDrift, mountPetDrift, final.ActiveBattleActorCount, final.ActiveSessionCount, dbLeak,
                battleActorLeak, sessionLeak,
                Math.Round(completedLoops / Math.Max(0.001, started.Elapsed.TotalSeconds), 2), samples);
            VerificationGuard.Require(status == "PASS", "Resource soak detected an unbounded resource trend or residue.");
            return result;
        }
        finally
        {
            started.Stop();
            MySqlConnection.ClearAllPools();
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: false);
        }
    }

    private static InMemoryCrossModuleGameplayStore CreateCrossModuleStore(int worker) => new(new CrossModuleGameplayState(
        900_000 + worker, 0, 0, 1, new Dictionary<int, int>(), new HashSet<int>(), new HashSet<Guid>(), [], 0));

    private static (bool Equipment, bool Party, bool MountPet) ExercisePartyMountPetEquipment(
        HeadlessGameplaySession session,
        long seed,
        int worker,
        long iteration)
    {
        var stage = "Equipment";
        try
        {
            var prefix = $"soak-{seed}-{worker}-{iteration}";
            var now = DateTimeOffset.UnixEpoch.AddSeconds(iteration);
            var item = CreateEquipmentItem(worker);
            var itemCatalog = new ItemDefinitionCatalog(Enumerable.Range(0, 4).Select(CreateEquipmentItem));
            var inventory = new PlayerInventoryRuntime(
                DeterministicGuid($"{prefix}:inventory"),
                800_000 + worker,
                8,
                itemCatalog: itemCatalog);
            var persistentItemId = 10_000_000L + worker * 1_000_000L + iteration;
            var added = inventory.AddItem(item, 1, () => persistentItemId, now);
            var equipment = new EquipmentRuntime(
                800_000 + worker,
                (GameplayClass)(worker % 4),
                new EquipmentDefinitionCatalog(
                [
                    new EquipmentDefinition(item.ItemTemplateId, EquipmentSlotType.Weapon, 1,
                    new HashSet<GameplayClass> { (GameplayClass)(worker % 4) }, new EquipmentStatBonus(0, 0, 5, 0, 0, 0), "SoakFixture")
                ]));
            var equipKey = $"{prefix}:equip";
            var equipped = equipment.Equip(equipKey, persistentItemId, EquipmentSlotType.Weapon, 1, inventory, now);
            var duplicateEquip = equipment.Equip(equipKey, persistentItemId, EquipmentSlotType.Weapon, 1, inventory, now);
            var unequipped = equipment.Unequip($"{prefix}:unequip", EquipmentSlotType.Weapon, inventory, now);
            var equipmentPassed = added.Succeeded && equipped.ResultCode == EquipmentResultCode.Success &&
                duplicateEquip.ResultCode == EquipmentResultCode.DuplicateCompleted && unequipped.ResultCode == EquipmentResultCode.Success &&
                equipment.Snapshot.Equipped.Count == 0 && inventory.Slots.Values.SingleOrDefault()?.PersistentInventoryItemId == persistentItemId;

            stage = "Party";
            var leader = DeterministicGuid($"{prefix}:leader");
            var member = DeterministicGuid($"{prefix}:member");
            var party = new PartyRuntime(DeterministicGuid($"{prefix}:party"), new PartyMemberState(leader, 1, 10, 10, true, 1));
            var inviteKey = $"{prefix}:invite";
            var invited = party.Execute(new PartyCommand(inviteKey, PartyCommandType.Invite, leader, member));
            var duplicateInvite = party.Execute(new PartyCommand(inviteKey, PartyCommandType.Invite, leader, member));
            var accepted = party.Execute(new PartyCommand($"{prefix}:accept", PartyCommandType.Accept, member));
            var transferred = party.Execute(new PartyCommand($"{prefix}:transfer", PartyCommandType.TransferLeader, leader, member));
            var left = party.Execute(new PartyCommand($"{prefix}:leave", PartyCommandType.Leave, leader));
            var partyPassed = invited.ResultCode == PartyResultCode.Success && duplicateInvite.ResultCode == PartyResultCode.DuplicateCompleted &&
                accepted.ResultCode == PartyResultCode.Success && transferred.ResultCode == PartyResultCode.Success &&
                left.ResultCode == PartyResultCode.Success && party.Snapshot.LeaderId == member && party.Snapshot.Members.Count == 1;

            stage = "MountPet";
            var mountAcquire = session.Mount.Execute(new MountCommand($"{prefix}:mount-acquire", MountCommandType.Acquire, 1, 1, false));
            var mountDuplicate = session.Mount.Execute(new MountCommand($"{prefix}:mount-acquire", MountCommandType.Acquire, 1, 1, false));
            var mountEquip = session.Mount.Execute(new MountCommand($"{prefix}:mount-equip", MountCommandType.Equip, 1, 1, false));
            var mountOn = session.Mount.Execute(new MountCommand($"{prefix}:mount-on", MountCommandType.MountOn, 1, 1, false));
            var mountOff = session.Mount.Execute(new MountCommand($"{prefix}:mount-off", MountCommandType.MountOff, 1, 1, false));
            var petAcquire = session.Pet.Execute(new PetCommand($"{prefix}:pet-acquire", PetCommandType.Acquire, 1));
            var petDuplicate = session.Pet.Execute(new PetCommand($"{prefix}:pet-acquire", PetCommandType.Acquire, 1));
            var petDeploy = session.Pet.Execute(new PetCommand($"{prefix}:pet-deploy", PetCommandType.Deploy, 1));
            var petWalk = session.Pet.Execute(new PetCommand($"{prefix}:pet-walk", PetCommandType.Walk, 1));
            var petRecall = session.Pet.Execute(new PetCommand($"{prefix}:pet-recall", PetCommandType.Recall, 1));
            var petWithdraw = session.Pet.Execute(new PetCommand($"{prefix}:pet-withdraw", PetCommandType.Withdraw, 1));
            var mountPetPassed = mountAcquire.ResultCode == MountResultCode.Success && mountDuplicate.ResultCode == MountResultCode.DuplicateCompleted &&
                mountEquip.ResultCode == MountResultCode.Success && mountOn.ResultCode == MountResultCode.Success && mountOff.ResultCode == MountResultCode.Success &&
                petAcquire.ResultCode == PetResultCode.Success && petDuplicate.ResultCode == PetResultCode.DuplicateCompleted &&
                petDeploy.ResultCode == PetResultCode.Success && petWalk.ResultCode == PetResultCode.Success &&
                petRecall.ResultCode == PetResultCode.Success && petWithdraw.ResultCode == PetResultCode.Success &&
                session.Mount.Snapshot.Mounted == false && session.Pet.Snapshot.ActivePetId is null;
            return (equipmentPassed, partyPassed, mountPetPassed);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{stage} soak operation threw.", exception);
        }
    }

    private static CrossModuleTransactionRequest CreateTransactionRequest(long seed, int worker, long iteration)
    {
        var transactionId = DeterministicGuid($"{seed}:transaction:{worker}:{iteration}");
        var battleId = DeterministicGuid($"{seed}:battle:{worker}:{iteration}");
        return new CrossModuleTransactionRequest(transactionId, $"soak-{seed}-{worker}-{iteration}", 900_000 + worker,
            battleId, 1000 + worker, 2000 + worker, 1, 100, 0);
    }

    private static ItemDefinition CreateEquipmentItem(int worker) => new(
        7000 + worker, "soak_weapon", "Soak Weapon", "soak.weapon", ItemCategory.Equipment,
        StackPolicy.Single, 1, BindPolicy.Bound, TradePolicy.NotTradable, SellPolicy.NotSellable,
        0, 0, "None", "Weapon", string.Empty, false, true, "soak-v1", "{}", "SoakFixture");

    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static string SafeFailureReason(Exception exception)
    {
        var known = new[]
        {
            "Soak gameplay loop", "Equipment soak", "Party soak", "Mount or pet soak", "Soak reconnect",
            "Cross-module", "Exactly-once"
        };
        return known.Any(prefix => exception.Message.StartsWith(prefix, StringComparison.Ordinal))
            ? exception.Message.Replace(' ', '_')
            : exception.GetType().Name;
    }

    private static async Task<ResourceSample> CaptureAsync(
        Process process, TimeSpan elapsed, string connectionString, ResourceSoakLiveSnapshot live, long loops,
        string temporaryRoot, CancellationToken cancellationToken)
    {
        process.Refresh();
        var gc = GC.GetGCMemoryInfo();
        var loh = gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0;
        var dbConnections = 0;
        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER=SUBSTRING_INDEX(CURRENT_USER(),'@',1);";
            dbConnections = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        var temporaryFiles = Directory.Exists(temporaryRoot)
            ? Directory.EnumerateFiles(temporaryRoot, "*", SearchOption.AllDirectories).Count() : 0;
        return new ResourceSample(
            DateTimeOffset.UtcNow, elapsed.TotalSeconds, process.WorkingSet64, gc.HeapSizeBytes, loh,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), process.Threads.Count,
            process.HandleCount, dbConnections, live.ActiveSessionCount, live.ActiveBattleActorCount, live.QueueDepth,
            live.OutboxBacklog, live.JournalBacklog, live.PendingTimerCount, live.KeyedLockCount,
            live.KeyedLockReferenceCount, live.IdempotencyCacheSize, live.TransactionStateCount, temporaryFiles, loops);
    }

    private static double SlopePerMinute(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2) return 0;
        var xMean = points.Average(value => value.X);
        var yMean = points.Average(value => value.Y);
        var numerator = points.Sum(value => (value.X - xMean) * (value.Y - yMean));
        var denominator = points.Sum(value => Math.Pow(value.X - xMean, 2));
        return denominator == 0 ? 0 : numerator / denominator * 60;
    }
}
