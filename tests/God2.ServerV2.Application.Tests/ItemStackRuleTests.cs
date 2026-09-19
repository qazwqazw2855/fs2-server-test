using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class ItemStackRuleTests
{
    [Theory]
    [InlineData(null, true, 1, false)]
    [InlineData(1, true, 1, false)]
    [InlineData(20, true, 20, true)]
    [InlineData(20, false, 20, false)]
    public void Stackability_follows_configured_limit_and_enabled_state(
        int? configuredLimit,
        bool enabled,
        int expectedLimit,
        bool expectedStackable)
    {
        var rule = new ItemStackRule(
            253231541, "肉塊", configuredLimit, enabled);

        Assert.Equal(expectedLimit, rule.EffectiveMaximumStack);
        Assert.Equal(expectedStackable, rule.IsStackable);
    }
}
