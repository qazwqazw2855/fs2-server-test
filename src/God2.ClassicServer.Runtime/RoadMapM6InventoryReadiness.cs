namespace God2.ClassicServer.Runtime;

public sealed record RoadMapM6InventoryReadinessInput(
    bool M5OfficialBattleLoopComplete,
    bool ProductionInventoryAuthorityConnected,
    int RuntimeCatalogItemCount,
    int RuntimeCatalogFatalIssueCount,
    bool MariaDbVersionCompareAndSwapEnabled,
    bool MariaDbExactlyOnceEnabled,
    bool RestartReloadIdentityStable,
    int EvidenceCompleteMonsterRewardProfiles,
    int ProductionEnabledDropEntries,
    int DefaultDisabledZeroViolations,
    bool ProductionBattleRewardPolicyEnabled,
    bool OfficialInventorySerializerEnabled,
    bool OfficialClientInventoryAcceptancePassed);

public sealed record RoadMapM6InventoryReadinessResult(bool Ready, string FirstBrokenNode);

public static class RoadMapM6InventoryReadiness
{
    public static RoadMapM6InventoryReadinessResult Evaluate(RoadMapM6InventoryReadinessInput input)
    {
        if (!input.M5OfficialBattleLoopComplete)
        {
            return Blocked("M5 official Battle/Skill core loop");
        }

        if (!input.ProductionInventoryAuthorityConnected)
        {
            return Blocked("Production MariaDB inventory authority composition");
        }

        if (input.RuntimeCatalogItemCount <= 0 || input.RuntimeCatalogFatalIssueCount != 0)
        {
            return Blocked("MariaDB runtime item catalog");
        }

        if (!input.MariaDbVersionCompareAndSwapEnabled || !input.MariaDbExactlyOnceEnabled)
        {
            return Blocked("MariaDB inventory CAS/exactly-once transaction");
        }

        if (!input.RestartReloadIdentityStable)
        {
            return Blocked("Inventory restart/relogin reconstruction");
        }

        if (input.EvidenceCompleteMonsterRewardProfiles <= 0)
        {
            return Blocked("Evidence-complete Monster reward profile");
        }

        if (input.DefaultDisabledZeroViolations != 0)
        {
            return Blocked("DefaultDisabledZero drop safety");
        }

        if (input.ProductionEnabledDropEntries <= 0 || !input.ProductionBattleRewardPolicyEnabled)
        {
            return Blocked("Production Monster drop/reward policy");
        }

        if (!input.OfficialInventorySerializerEnabled)
        {
            return Blocked("Official Client inventory serializer");
        }

        return !input.OfficialClientInventoryAcceptancePassed
            ? Blocked("Official Client inventory acceptance")
            : new RoadMapM6InventoryReadinessResult(true, string.Empty);
    }

    private static RoadMapM6InventoryReadinessResult Blocked(string firstBrokenNode) =>
        new(false, firstBrokenNode);
}

public static class RoadMapM6InventoryCapabilities
{
    public const bool MariaDbVersionCompareAndSwapEnabled = true;
    public const bool MariaDbExactlyOnceEnabled = true;
    public const bool ProductionBattleRewardPolicyEnabled = false;
    public const bool OfficialInventorySerializerEnabled = false;
}
