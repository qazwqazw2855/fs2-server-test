using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RoadMapM6InventoryReadinessTests
{
    public static TheoryData<RoadMapM6InventoryReadinessInput, string> BrokenEdges => new()
    {
        { Ready() with { M5OfficialBattleLoopComplete = false }, "M5 official Battle/Skill core loop" },
        { Ready() with { ProductionInventoryAuthorityConnected = false }, "Production MariaDB inventory authority composition" },
        { Ready() with { RuntimeCatalogItemCount = 0 }, "MariaDB runtime item catalog" },
        { Ready() with { RuntimeCatalogFatalIssueCount = 1 }, "MariaDB runtime item catalog" },
        { Ready() with { MariaDbVersionCompareAndSwapEnabled = false }, "MariaDB inventory CAS/exactly-once transaction" },
        { Ready() with { MariaDbExactlyOnceEnabled = false }, "MariaDB inventory CAS/exactly-once transaction" },
        { Ready() with { RestartReloadIdentityStable = false }, "Inventory restart/relogin reconstruction" },
        { Ready() with { EvidenceCompleteMonsterRewardProfiles = 0 }, "Evidence-complete Monster reward profile" },
        { Ready() with { DefaultDisabledZeroViolations = 1 }, "DefaultDisabledZero drop safety" },
        { Ready() with { ProductionEnabledDropEntries = 0 }, "Production Monster drop/reward policy" },
        { Ready() with { ProductionBattleRewardPolicyEnabled = false }, "Production Monster drop/reward policy" },
        { Ready() with { OfficialInventorySerializerEnabled = false }, "Official Client inventory serializer" },
        { Ready() with { OfficialClientInventoryAcceptancePassed = false }, "Official Client inventory acceptance" }
    };

    [Theory]
    [MemberData(nameof(BrokenEdges))]
    public void Evaluate_fails_closed_at_first_missing_m6_edge(
        RoadMapM6InventoryReadinessInput input,
        string expectedBrokenNode)
    {
        var result = RoadMapM6InventoryReadiness.Evaluate(input);

        Assert.False(result.Ready);
        Assert.Equal(expectedBrokenNode, result.FirstBrokenNode);
    }

    [Fact]
    public void Evaluate_passes_only_complete_official_client_persistence_loop()
    {
        var result = RoadMapM6InventoryReadiness.Evaluate(Ready());

        Assert.True(result.Ready);
        Assert.Empty(result.FirstBrokenNode);
    }

    [Fact]
    public void Current_unproven_reward_and_inventory_wire_capabilities_remain_disabled()
    {
        Assert.True(RoadMapM6InventoryCapabilities.MariaDbVersionCompareAndSwapEnabled);
        Assert.True(RoadMapM6InventoryCapabilities.MariaDbExactlyOnceEnabled);
        Assert.False(RoadMapM6InventoryCapabilities.ProductionBattleRewardPolicyEnabled);
        Assert.False(RoadMapM6InventoryCapabilities.OfficialInventorySerializerEnabled);
    }

    private static RoadMapM6InventoryReadinessInput Ready() =>
        new(
            M5OfficialBattleLoopComplete: true,
            ProductionInventoryAuthorityConnected: true,
            RuntimeCatalogItemCount: 1,
            RuntimeCatalogFatalIssueCount: 0,
            MariaDbVersionCompareAndSwapEnabled: true,
            MariaDbExactlyOnceEnabled: true,
            RestartReloadIdentityStable: true,
            EvidenceCompleteMonsterRewardProfiles: 1,
            ProductionEnabledDropEntries: 1,
            DefaultDisabledZeroViolations: 0,
            ProductionBattleRewardPolicyEnabled: true,
            OfficialInventorySerializerEnabled: true,
            OfficialClientInventoryAcceptancePassed: true);
}
