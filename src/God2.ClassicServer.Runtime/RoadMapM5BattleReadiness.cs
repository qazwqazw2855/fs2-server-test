namespace God2.ClassicServer.Runtime;

public sealed record RoadMapM5BattleReadinessInput(
    long SemanticEncounterCandidates,
    long EnabledRuntimeSpawnRows,
    int RepositoryMonsterSpawns,
    int RuntimeMonsterEntities,
    bool RuntimeEncounterSemanticsEnabled,
    long SemanticSkillCandidates,
    int RepositorySkillDefinitions,
    int RuntimeExecutionEligibleSkills,
    bool CommandRuntimeMutationEnabled,
    bool ServerResultSerializerEnabled);

public sealed record RoadMapM5BattleReadinessResult(
    bool Ready,
    string FirstBrokenNode);

/// <summary>
/// Keeps the M5 promotion gate aligned with the production repository and Runtime path.
/// Semantic-table candidates alone are never sufficient to authorize gameplay mutation.
/// </summary>
public static class RoadMapM5BattleReadiness
{
    public static RoadMapM5BattleReadinessResult Evaluate(RoadMapM5BattleReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.SemanticEncounterCandidates <= 0)
        {
            return Blocked("MariaDB evidence-complete Monster encounter slice");
        }

        if (input.EnabledRuntimeSpawnRows <= 0)
        {
            return Blocked("Production spawns row for the eligible Monster");
        }

        if (input.RepositoryMonsterSpawns <= 0)
        {
            return Blocked("MariaDbWorldContentRepository Monster spawn mapping");
        }

        if (input.RuntimeMonsterEntities <= 0)
        {
            return Blocked("MapRuntime Monster entity construction");
        }

        if (!input.RuntimeEncounterSemanticsEnabled)
        {
            return Blocked("Production Monster HP/MP/stat/AI/formation/reward runtime mapping");
        }

        if (input.SemanticSkillCandidates <= 0)
        {
            return Blocked("MariaDB evidence-complete Skill execution slice");
        }

        if (input.RepositorySkillDefinitions <= 0)
        {
            return Blocked("MariaDbSkillDefinitionRepository content load");
        }

        if (input.RuntimeExecutionEligibleSkills <= 0)
        {
            return Blocked("Production SkillDefinition runtime validation");
        }

        if (!input.CommandRuntimeMutationEnabled)
        {
            return Blocked("Official Battle command semantic mutation contract");
        }

        return !input.ServerResultSerializerEnabled
            ? Blocked("Official Battle S2C result serializer contract")
            : new RoadMapM5BattleReadinessResult(true, string.Empty);
    }

    private static RoadMapM5BattleReadinessResult Blocked(string node) => new(false, node);
}

/// <summary>
/// Explicit production capability switches for the M5 vertical slice. A capability may only be
/// enabled when the MariaDB repository and Runtime carry the complete evidence-backed semantics;
/// creating a generic world entity is not sufficient.
/// </summary>
public static class RoadMapM5BattleRuntimeCapabilities
{
    public const bool FullMonsterEncounterSemanticsEnabled = false;
}

public sealed record RoadMapM5MonsterSpawnIdentity(
    int MonsterTemplateId,
    int MapId,
    int PositionX,
    int PositionY);

/// <summary>
/// Counts identities that exist on both sides of a production edge. Independent non-zero row
/// counts must never satisfy M5 when they describe different monsters or skills.
/// </summary>
public static class RoadMapM5IdentityAlignment
{
    public static int CountAlignedIds(IEnumerable<int> semanticIds, IEnumerable<int> runtimeIds)
    {
        ArgumentNullException.ThrowIfNull(semanticIds);
        ArgumentNullException.ThrowIfNull(runtimeIds);

        var aligned = semanticIds.ToHashSet();
        aligned.IntersectWith(runtimeIds);
        return aligned.Count;
    }

    public static int CountAlignedMonsterSpawns(
        IEnumerable<RoadMapM5MonsterSpawnIdentity> semanticSpawns,
        IEnumerable<RoadMapM5MonsterSpawnIdentity> runtimeSpawns)
    {
        ArgumentNullException.ThrowIfNull(semanticSpawns);
        ArgumentNullException.ThrowIfNull(runtimeSpawns);

        var aligned = semanticSpawns.ToHashSet();
        aligned.IntersectWith(runtimeSpawns);
        return aligned.Count;
    }
}
