using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed record ProductionMonsterEncounterTarget(
    WorldSessionBinding Binding,
    PlayerObject Player,
    MonsterObject Monster,
    MonsterCombatRuntimeState CombatState,
    MonsterCombatDefinition CombatDefinition);

public interface IProductionMonsterCombatService
{
    Task<CombatResult> BasicAttackMonsterAsync(
        string sessionId,
        long monsterRuntimeEntityId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IProductionMonsterCombatServiceFactory
{
    OperationResult<IProductionMonsterCombatService> CreateForSession(string sessionId);
}

/// <summary>
/// Production-side bridge from a bound world session and a MapRuntime monster identity to
/// the combat authority. It intentionally stops before protocol dispatch when no official
/// battle C2S family or evidence-complete production encounter is available.
/// </summary>
public sealed class AuthoritativeWorldMonsterTargetResolver
{
    private readonly IWorldSessionCoordinator _sessions;

    public AuthoritativeWorldMonsterTargetResolver(IWorldSessionCoordinator sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        if (_sessions.AuthorityKind != WorldContentAuthorityKind.MariaDb)
        {
            throw new InvalidOperationException("Production monster targeting requires MariaDB world authority.");
        }
    }

    public OperationResult<ProductionMonsterEncounterTarget> Resolve(
        string sessionId,
        long monsterRuntimeEntityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var binding = _sessions.GetBinding(sessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return Failure(binding.Error.Code, binding.Error.Message, binding.Error.Source);
        }

        var current = binding.Value;
        if (current.Session.ProtocolStage != Protocol.ProtocolStage.InWorld ||
            !current.Session.IsAuthenticated ||
            current.Session.CharacterId != current.Character.CharacterId ||
            current.Session.AccountId != current.Character.AccountId)
        {
            return Failure(
                "battle.world_session_invalid",
                "Monster encounter targeting requires the matching authenticated InWorld owner.",
                sessionId);
        }

        var player = current.MapRuntime.Objects.Get(current.MapSession.PlayerRuntimeEntityId);
        if (!player.Succeeded || player.Value is not PlayerObject playerObject ||
            playerObject.State.CharacterId != current.Character.CharacterId ||
            playerObject.State.MapId != current.MapSession.MapId)
        {
            return Failure(
                "battle.player_runtime_missing",
                "The authoritative player runtime entity is unavailable.",
                sessionId);
        }

        var monster = current.MapRuntime.Objects.Get(monsterRuntimeEntityId);
        if (!monster.Succeeded || monster.Value is not MonsterObject monsterObject)
        {
            return Failure(
                "battle.monster_runtime_missing",
                "The requested target is not an active monster in the bound MapRuntime.",
                monsterRuntimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!monsterObject.State.Enabled ||
            monsterObject.State.MapId != current.MapSession.MapId ||
            monsterObject.Identity.MapId != current.MapSession.MapId)
        {
            return Failure(
                "battle.monster_runtime_scope_invalid",
                "The monster target is disabled or belongs to a different map instance.",
                monsterRuntimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var combatState = current.MapRuntime.MonsterCombat.Get(monsterRuntimeEntityId);
        if (!combatState.Succeeded || combatState.Value is null ||
            combatState.Value.LifecycleState != MonsterLifecycleState.Active ||
            combatState.Value.CombatState is MonsterCombatStateKind.Dead or MonsterCombatStateKind.RespawnPending or MonsterCombatStateKind.Faulted)
        {
            return Failure(
                combatState.Succeeded ? "battle.monster_not_encounter_eligible" : combatState.Error.Code,
                combatState.Succeeded
                    ? "The authoritative monster is not in an encounter-eligible lifecycle state."
                    : combatState.Error.Message,
                combatState.Succeeded
                    ? monsterRuntimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : combatState.Error.Source);
        }

        var definition = current.MapRuntime.MonsterCombat.GetDefinition(monsterObject.State.SpawnId);
        if (!definition.Succeeded || definition.Value is null ||
            definition.Value.StatPolicyStatus == CombatPolicyStatus.EvidenceBlocked)
        {
            return Failure(
                definition.Succeeded ? "battle.monster_stat_policy_blocked" : definition.Error.Code,
                definition.Succeeded
                    ? "The monster combat policy is not evidence-complete for production."
                    : definition.Error.Message,
                definition.Succeeded ? monsterObject.State.SpawnId.ToString(System.Globalization.CultureInfo.InvariantCulture) : definition.Error.Source);
        }

        return OperationResult<ProductionMonsterEncounterTarget>.Success(new ProductionMonsterEncounterTarget(
            current,
            playerObject,
            monsterObject,
            combatState.Value,
            definition.Value));
    }

    private static OperationResult<ProductionMonsterEncounterTarget> Failure(
        string code,
        string message,
        string? source = null) =>
        OperationResult<ProductionMonsterEncounterTarget>.Failure(code, message, source ?? string.Empty);
}

public sealed class WorldSessionInteractionRegistryAdapter : IWorldInteractionSessionRegistry
{
    private readonly IWorldSessionCoordinator _sessions;

    public WorldSessionInteractionRegistryAdapter(IWorldSessionCoordinator sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        if (_sessions.AuthorityKind != WorldContentAuthorityKind.MariaDb)
        {
            throw new InvalidOperationException("Production combat interaction registry requires MariaDB world authority.");
        }
    }

    public OperationResult<InteractionSessionBinding> Get(string sessionId)
    {
        var binding = _sessions.GetBinding(sessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return OperationResult<InteractionSessionBinding>.Failure(
                binding.Error.Code,
                binding.Error.Message,
                binding.Error.Source);
        }

        return OperationResult<InteractionSessionBinding>.Success(ToInteractionBinding(binding.Value));
    }

    public void Set(InteractionSessionBinding binding) =>
        throw new NotSupportedException("Production combat must mutate world bindings through IWorldSessionCoordinator.");

    public bool Remove(string sessionId) => false;

    private static InteractionSessionBinding ToInteractionBinding(WorldSessionBinding binding) =>
        new(
            binding.Session,
            binding.MapSession,
            binding.MapRuntime,
            binding.MapRuntime.WorldInstanceId,
            PlayerRuntimeVersion: 0,
            IsTransitioning: false,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
