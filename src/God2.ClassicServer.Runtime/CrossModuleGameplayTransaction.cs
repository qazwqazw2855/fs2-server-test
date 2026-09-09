using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace God2.ClassicServer.Runtime;

public enum CrossModuleTransactionResultCode
{
    Committed,
    DuplicateCommitted,
    Conflict,
    RolledBack,
    RecoveryRequired
}

public enum CrossModuleFailurePoint
{
    None,
    BattleSettlement,
    Reward,
    QuestCompletion,
    Inventory,
    Experience,
    LevelUp,
    Outbox,
    BeforeCommit,
    CommitResponseLost,
    CrashAfterPrepare
}

public sealed record CrossModuleGameplayState(
    long CharacterId,
    long Version,
    long Experience,
    int Level,
    IReadOnlyDictionary<int, int> Inventory,
    IReadOnlySet<int> CompletedQuests,
    IReadOnlySet<Guid> SettledBattles,
    IReadOnlyList<string> Outbox,
    int RewardCommitCount);

public sealed record CrossModuleTransactionRequest(
    Guid TransactionId,
    string IdempotencyKey,
    long CharacterId,
    Guid BattleId,
    int QuestId,
    int ItemId,
    int ItemQuantity,
    long ExperienceGain,
    long ExpectedVersion);

public sealed record CrossModuleTransactionReceipt(
    Guid TransactionId,
    string PayloadHash,
    CrossModuleTransactionResultCode Code,
    long VersionBefore,
    long VersionAfter,
    string FailureCode,
    CrossModuleGameplayState State);

public sealed record CrossModuleGameplayStoreDiagnostics(
    int ReceiptCount,
    int PreparedTransactionCount,
    int TransactionGateHolderCount,
    int KeyedLockCount,
    int KeyedLockReferenceCount);

public sealed class InMemoryCrossModuleGameplayStore
{
    private readonly object _sync = new();
    private CrossModuleGameplayState _state;
    private readonly Dictionary<string, CrossModuleTransactionReceipt> _receipts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PreparedTransaction> _prepared = new(StringComparer.Ordinal);

    public InMemoryCrossModuleGameplayStore(CrossModuleGameplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CharacterId <= 0 || state.Version < 0 || state.Experience < 0 || state.Level < 1 || state.RewardCommitCount < 0)
        {
            throw new ArgumentException("Initial cross-module gameplay state is invalid.", nameof(state));
        }
        _state = Freeze(state);
    }

    public CrossModuleFailurePoint FailurePoint { get; set; }
    internal SemaphoreSlim TransactionGate { get; } = new(1, 1);

    public CrossModuleGameplayState Snapshot()
    {
        lock (_sync) return Freeze(_state);
    }

    public CrossModuleGameplayStoreDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            // The store deliberately uses one bounded transaction gate instead of a
            // per-key lock registry.  Exposing the zero keyed-lock counts makes that
            // resource invariant observable without leaking idempotency keys.
            return new CrossModuleGameplayStoreDiagnostics(
                _receipts.Count,
                _prepared.Count,
                TransactionGate.CurrentCount == 0 ? 1 : 0,
                0,
                0);
        }
    }

    public CrossModuleTransactionReceipt? Find(string idempotencyKey)
    {
        lock (_sync)
        {
            var receipt = _receipts.GetValueOrDefault(HashKey(idempotencyKey));
            return receipt is null ? null : Freeze(receipt);
        }
    }

    internal bool Prepare(CrossModuleTransactionRequest request, string payloadHash, CrossModuleGameplayState candidate)
    {
        lock (_sync)
        {
            if (candidate.CharacterId != request.CharacterId || candidate.Version != checked(request.ExpectedVersion + 1))
            {
                throw new InvalidOperationException("Prepared gameplay state does not match its transaction base version.");
            }

            var safeKey = HashKey(request.IdempotencyKey);
            if (_prepared.TryGetValue(safeKey, out var existing))
            {
                return existing.Hash == payloadHash &&
                    existing.TransactionId == request.TransactionId &&
                    existing.CharacterId == request.CharacterId &&
                    existing.ExpectedVersion == request.ExpectedVersion;
            }

            _prepared[safeKey] = new PreparedTransaction(
                payloadHash,
                Freeze(candidate),
                request.TransactionId,
                request.CharacterId,
                request.ExpectedVersion);
            return true;
        }
    }

    internal CrossModuleTransactionReceipt Commit(
        CrossModuleTransactionRequest request,
        string payloadHash,
        CrossModuleGameplayState candidate)
    {
        lock (_sync)
        {
            var safeKey = HashKey(request.IdempotencyKey);
            if (_receipts.TryGetValue(safeKey, out var existing))
            {
                return existing.PayloadHash == payloadHash
                    ? Freeze(existing with { Code = CrossModuleTransactionResultCode.DuplicateCommitted })
                    : Freeze(existing with
                    {
                        Code = CrossModuleTransactionResultCode.Conflict,
                        FailureCode = "gameplay.transaction.idempotency_payload_conflict"
                    });
            }
            if (_state.Version != request.ExpectedVersion)
            {
                return new CrossModuleTransactionReceipt(
                    request.TransactionId, payloadHash, CrossModuleTransactionResultCode.Conflict,
                    _state.Version, _state.Version, "gameplay.transaction.version_conflict", Freeze(_state));
            }
            if (candidate.CharacterId != request.CharacterId || candidate.Version != checked(request.ExpectedVersion + 1))
            {
                throw new InvalidOperationException("Committed gameplay state does not match its transaction base version.");
            }
            var receipt = new CrossModuleTransactionReceipt(
                request.TransactionId,
                payloadHash,
                CrossModuleTransactionResultCode.Committed,
                _state.Version,
                candidate.Version,
                string.Empty,
                Freeze(candidate));
            _state = Freeze(candidate);
            _receipts[safeKey] = receipt;
            _prepared.Remove(safeKey);
            return Freeze(receipt);
        }
    }

    public IReadOnlyList<CrossModuleTransactionReceipt> Recover()
    {
        TransactionGate.Wait();
        try
        {
            lock (_sync)
            {
                var recovered = new List<CrossModuleTransactionReceipt>();
                foreach (var pair in _prepared.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray())
                {
                    if (_receipts.ContainsKey(pair.Key))
                    {
                        _prepared.Remove(pair.Key);
                        continue;
                    }
                    var value = pair.Value;
                    CrossModuleTransactionReceipt receipt;
                    if (_state.CharacterId != value.CharacterId || _state.Version != value.ExpectedVersion)
                    {
                        receipt = new CrossModuleTransactionReceipt(
                            value.TransactionId,
                            value.Hash,
                            CrossModuleTransactionResultCode.Conflict,
                            value.ExpectedVersion,
                            _state.Version,
                            "gameplay.transaction.recovery_version_conflict",
                            Freeze(_state));
                    }
                    else
                    {
                        receipt = new CrossModuleTransactionReceipt(
                            value.TransactionId,
                            value.Hash,
                            CrossModuleTransactionResultCode.Committed,
                            value.ExpectedVersion,
                            value.Candidate.Version,
                            string.Empty,
                            Freeze(value.Candidate));
                        _state = Freeze(value.Candidate);
                    }
                    _receipts[pair.Key] = receipt;
                    _prepared.Remove(pair.Key);
                    recovered.Add(Freeze(receipt));
                }
                return recovered;
            }
        }
        finally
        {
            TransactionGate.Release();
        }
    }

    private static CrossModuleGameplayState Freeze(CrossModuleGameplayState state) => state with
    {
        Inventory = state.Inventory.ToFrozenDictionary(),
        CompletedQuests = state.CompletedQuests.ToFrozenSet(),
        SettledBattles = state.SettledBattles.ToFrozenSet(),
        Outbox = new ReadOnlyCollection<string>(state.Outbox.ToArray())
    };

    private static CrossModuleTransactionReceipt Freeze(CrossModuleTransactionReceipt receipt) =>
        receipt with { State = Freeze(receipt.State) };

    private static string HashKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record PreparedTransaction(
        string Hash,
        CrossModuleGameplayState Candidate,
        Guid TransactionId,
        long CharacterId,
        long ExpectedVersion);
}

public sealed class CrossModuleGameplayTransactionCoordinator
{
    private readonly InMemoryCrossModuleGameplayStore _store;
    private readonly ICrossModuleProgressionPolicy _progression;
    private readonly SemaphoreSlim _gate;

    public CrossModuleGameplayTransactionCoordinator(
        InMemoryCrossModuleGameplayStore store,
        ICrossModuleProgressionPolicy? progression = null)
    {
        _store = store;
        _progression = progression ?? new EvidenceBlockedCrossModuleProgressionPolicy();
        _gate = store.TransactionGate;
    }

    public async Task<CrossModuleTransactionReceipt> CommitAsync(
        CrossModuleTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        var payloadHash = PayloadHash(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = _store.Find(request.IdempotencyKey);
            if (existing is not null)
            {
                if (existing.PayloadHash != payloadHash)
                {
                    return existing with { Code = CrossModuleTransactionResultCode.Conflict, FailureCode = "gameplay.transaction.idempotency_payload_conflict" };
                }

                return existing.Code == CrossModuleTransactionResultCode.Committed
                    ? existing with { Code = CrossModuleTransactionResultCode.DuplicateCommitted }
                    : existing;
            }

            var before = _store.Snapshot();
            if (before.CharacterId != request.CharacterId || before.Version != request.ExpectedVersion)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.Conflict, "gameplay.transaction.state_conflict");
            }
            if (request.TransactionId == Guid.Empty || request.BattleId == Guid.Empty ||
                request.ItemQuantity < 0 || request.ExperienceGain < 0 || request.QuestId <= 0 || request.ItemId <= 0)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.RolledBack, "gameplay.transaction.input_invalid");
            }
            if (before.SettledBattles.Contains(request.BattleId))
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.Conflict, "gameplay.transaction.battle_already_settled");
            }

            CrossModuleGameplayState candidate;
            try
            {
                ThrowAt(CrossModuleFailurePoint.BattleSettlement);
                var battles = new HashSet<Guid>(before.SettledBattles) { request.BattleId };
                ThrowAt(CrossModuleFailurePoint.Reward);
                var rewardCount = checked(before.RewardCommitCount + 1);
                ThrowAt(CrossModuleFailurePoint.QuestCompletion);
                var quests = new HashSet<int>(before.CompletedQuests) { request.QuestId };
                ThrowAt(CrossModuleFailurePoint.Inventory);
                var inventory = new Dictionary<int, int>(before.Inventory);
                inventory[request.ItemId] = checked(inventory.GetValueOrDefault(request.ItemId) + request.ItemQuantity);
                ThrowAt(CrossModuleFailurePoint.Experience);
                var progression = _progression.Apply(before.Experience, before.Level, request.ExperienceGain);
                ThrowAt(CrossModuleFailurePoint.LevelUp);
                ThrowAt(CrossModuleFailurePoint.Outbox);
                var outbox = before.Outbox.Append($"gameplay-committed:{request.TransactionId:N}").ToArray();
                candidate = new CrossModuleGameplayState(
                    before.CharacterId,
                    checked(before.Version + 1),
                    progression.Experience,
                    progression.Level,
                    inventory,
                    quests,
                    battles,
                    outbox,
                    rewardCount);
                ThrowAt(CrossModuleFailurePoint.BeforeCommit);
            }
            catch (InjectedCrossModuleFailure exception)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.RolledBack, exception.Code);
            }
            catch (CrossModuleProgressionEvidenceBlockedException exception)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.RolledBack, exception.FailureCode);
            }
            catch (OverflowException)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.RolledBack, "gameplay.transaction.arithmetic_overflow");
            }

            if (!_store.Prepare(request, payloadHash, candidate))
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.Conflict, "gameplay.transaction.prepared_payload_conflict");
            }
            if (_store.FailurePoint == CrossModuleFailurePoint.CrashAfterPrepare)
            {
                return Failure(request, payloadHash, before, CrossModuleTransactionResultCode.RecoveryRequired, "gameplay.transaction.crash_after_prepare");
            }
            var receipt = _store.Commit(request, payloadHash, candidate);
            if (_store.FailurePoint == CrossModuleFailurePoint.CommitResponseLost)
            {
                return receipt with { Code = CrossModuleTransactionResultCode.RecoveryRequired, FailureCode = "gameplay.transaction.commit_response_lost" };
            }
            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowAt(CrossModuleFailurePoint point)
    {
        if (_store.FailurePoint == point) throw new InjectedCrossModuleFailure($"gameplay.transaction.injected_{point.ToString().ToLowerInvariant()}");
    }

    private static CrossModuleTransactionReceipt Failure(
        CrossModuleTransactionRequest request,
        string hash,
        CrossModuleGameplayState state,
        CrossModuleTransactionResultCode code,
        string failureCode) =>
        new(request.TransactionId, hash, code, state.Version, state.Version, failureCode, state);

    private static string PayloadHash(CrossModuleTransactionRequest request)
    {
        var canonical = $"{request.TransactionId:N}|{request.CharacterId}|{request.BattleId:N}|{request.QuestId}|{request.ItemId}|{request.ItemQuantity}|{request.ExperienceGain}|{request.ExpectedVersion}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed class InjectedCrossModuleFailure(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}

public readonly record struct CrossModuleProgressionResult(long Experience, int Level);

public interface ICrossModuleProgressionPolicy
{
    CrossModuleProgressionResult Apply(long currentExperience, int currentLevel, long experienceGain);
}

/// <summary>
/// Production-safe default while the formal level_experience catalog has no verified rows.
/// A positive experience award fails the containing transaction instead of selecting a
/// research curve implicitly. A verified data-backed policy must be injected explicitly.
/// </summary>
public sealed class EvidenceBlockedCrossModuleProgressionPolicy : ICrossModuleProgressionPolicy
{
    public const string EvidenceStatus = "EvidenceBlockedMissingVerifiedLevelExperience";
    public const string FailureCode = "gameplay.transaction.progression_evidence_blocked";

    public CrossModuleProgressionResult Apply(long currentExperience, int currentLevel, long experienceGain)
    {
        if (currentExperience < 0 || currentLevel < 1 || experienceGain < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentExperience),
                "Progression values must be non-negative and level must be positive.");
        }

        if (experienceGain == 0)
        {
            return new CrossModuleProgressionResult(currentExperience, currentLevel);
        }

        throw new CrossModuleProgressionEvidenceBlockedException(FailureCode);
    }
}

/// <summary>
/// Explicit research/baseline progression policy. This must never be selected implicitly by
/// a production-authoritative path because its experience thresholds are not formally verified.
/// </summary>
public sealed class ConservativeCrossModuleProgressionPolicy : ICrossModuleProgressionPolicy
{
    private readonly ConservativeProgressionCatalog _catalog;

    public ConservativeCrossModuleProgressionPolicy(ConservativeProgressionCatalog? catalog = null)
    {
        _catalog = catalog ?? ConservativeProgressionCatalog.CreateDefault();
    }

    public CrossModuleProgressionResult Apply(long currentExperience, int currentLevel, long experienceGain)
    {
        if (currentExperience < 0 || currentLevel is < 1 || experienceGain < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentExperience), "Progression values must be non-negative and level must be positive.");
        }

        var maximumExperience = _catalog.RequiredTotalExperience(_catalog.MaximumLevel);
        if (currentLevel >= _catalog.MaximumLevel)
        {
            return new CrossModuleProgressionResult(currentExperience, currentLevel);
        }

        var experience = Math.Max(currentExperience, Math.Min(maximumExperience, checked(currentExperience + experienceGain)));
        var level = currentLevel;
        while (level < _catalog.MaximumLevel && experience >= _catalog.RequiredTotalExperience(level + 1))
        {
            level++;
        }

        return new CrossModuleProgressionResult(experience, level);
    }
}

internal sealed class CrossModuleProgressionEvidenceBlockedException(string failureCode) : Exception(failureCode)
{
    public string FailureCode { get; } = failureCode;
}
