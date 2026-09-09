using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RoadMapM5BattleReadinessTests
{
    public static TheoryData<RoadMapM5BattleReadinessInput, string> BlockedInputs => new()
    {
        { ReadyInput() with { SemanticEncounterCandidates = 0 }, "MariaDB evidence-complete Monster encounter slice" },
        { ReadyInput() with { EnabledRuntimeSpawnRows = 0 }, "Production spawns row for the eligible Monster" },
        { ReadyInput() with { RepositoryMonsterSpawns = 0 }, "MariaDbWorldContentRepository Monster spawn mapping" },
        { ReadyInput() with { RuntimeMonsterEntities = 0 }, "MapRuntime Monster entity construction" },
        { ReadyInput() with { RuntimeEncounterSemanticsEnabled = false }, "Production Monster HP/MP/stat/AI/formation/reward runtime mapping" },
        { ReadyInput() with { SemanticSkillCandidates = 0 }, "MariaDB evidence-complete Skill execution slice" },
        { ReadyInput() with { RepositorySkillDefinitions = 0 }, "MariaDbSkillDefinitionRepository content load" },
        { ReadyInput() with { RuntimeExecutionEligibleSkills = 0 }, "Production SkillDefinition runtime validation" },
        { ReadyInput() with { CommandRuntimeMutationEnabled = false }, "Official Battle command semantic mutation contract" },
        { ReadyInput() with { ServerResultSerializerEnabled = false }, "Official Battle S2C result serializer contract" }
    };

    [Theory]
    [MemberData(nameof(BlockedInputs))]
    public void Evaluate_FailsClosedAtTheFirstMissingProductionEdge(
        RoadMapM5BattleReadinessInput input,
        string expectedNode)
    {
        var result = RoadMapM5BattleReadiness.Evaluate(input);

        Assert.False(result.Ready);
        Assert.Equal(expectedNode, result.FirstBrokenNode);
    }

    [Fact]
    public void Evaluate_PassesOnlyWhenEveryRepositoryRuntimeAndWireEdgeIsReady()
    {
        var result = RoadMapM5BattleReadiness.Evaluate(ReadyInput());

        Assert.True(result.Ready);
        Assert.Empty(result.FirstBrokenNode);
    }

    [Fact]
    public void IdentityAlignment_DoesNotTreatUnrelatedNonZeroCountsAsTheSameContent()
    {
        var aligned = RoadMapM5IdentityAlignment.CountAlignedIds([101, 102], [201, 202]);

        Assert.Equal(0, aligned);
    }

    [Fact]
    public void IdentityAlignment_CountsDistinctSharedIdentities()
    {
        var aligned = RoadMapM5IdentityAlignment.CountAlignedIds([101, 101, 102], [101, 101, 103]);

        Assert.Equal(1, aligned);
    }

    [Fact]
    public void IdentityAlignment_RequiresTheSameMonsterMapAndCoordinates()
    {
        var semantic = new RoadMapM5MonsterSpawnIdentity(101, 19, 120, 68);

        Assert.Equal(
            0,
            RoadMapM5IdentityAlignment.CountAlignedMonsterSpawns(
                [semantic],
                [semantic with { PositionX = 121 }]));
        Assert.Equal(
            1,
            RoadMapM5IdentityAlignment.CountAlignedMonsterSpawns([semantic], [semantic]));
    }

    [Fact]
    public void CurrentProductionMonsterRuntime_DoesNotClaimFullM5EncounterSemantics()
    {
        Assert.False(RoadMapM5BattleRuntimeCapabilities.FullMonsterEncounterSemanticsEnabled);
    }

    private static RoadMapM5BattleReadinessInput ReadyInput() => new(
        SemanticEncounterCandidates: 1,
        EnabledRuntimeSpawnRows: 1,
        RepositoryMonsterSpawns: 1,
        RuntimeMonsterEntities: 1,
        RuntimeEncounterSemanticsEnabled: true,
        SemanticSkillCandidates: 1,
        RepositorySkillDefinitions: 1,
        RuntimeExecutionEligibleSkills: 1,
        CommandRuntimeMutationEnabled: true,
        ServerResultSerializerEnabled: true);
}
