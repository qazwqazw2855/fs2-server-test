using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbCombatRewardPolicy : ICombatRewardPolicy
{
    private readonly MariaDbStaticDataLoader _loader;

    public MariaDbCombatRewardPolicy(MariaDbStaticDataLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.ContentBacked;

    public CombatRewardPlan Resolve(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        long expectedInventoryVersion,
        DateTimeOffset now)
    {
        var policy = new CatalogBackedCombatRewardPolicy(
            _loader.PublishedSnapshot.DropTables.Values
                .Where(drop => drop.MonsterId is not null && drop.ItemId is not null)
                .Select(drop => new CombatDropRewardDefinition(
                    drop.Id,
                    drop.MonsterId!.Value,
                    drop.ItemId!.Value,
                    drop.MinimumQuantity,
                    drop.MaximumQuantity,
                    drop.DropRate ?? 0m,
                    drop.IsGuaranteed,
                    CombatPolicyStatus.ContentBacked,
                    drop.Source)));
        return policy.Resolve(death, definition, expectedInventoryVersion, now);
    }
}
