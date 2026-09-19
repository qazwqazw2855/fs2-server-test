using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class ItemStackMergePolicyTests
{
    [Theory]
    [InlineData("Unknown", "{}", false)]
    [InlineData("Bound", "{}", false)]
    [InlineData("Unbound", """{"upgrade":1}""", false)]
    [InlineData("Unbound", "{}", true)]
    public void Merge_requires_known_unbound_plain_items(
        string bindState,
        string metadata,
        bool expected)
    {
        var rule = new ItemStackRule(253231541, "肉塊", 20, true);
        var source = new CharacterInventorySlot(
            0, 253231541, 3, bindState, metadata);
        var target = new CharacterInventorySlot(
            1, 253231541, 4, "Unbound", "{}");

        Assert.Equal(
            expected,
            ItemStackMergePolicy.CanMerge(rule, source, target));
    }

    [Fact]
    public void Merge_rejects_quantity_above_limit()
    {
        var rule = new ItemStackRule(253231541, "肉塊", 20, true);
        var source = new CharacterInventorySlot(
            0, 253231541, 19, "Unbound", "{}");
        var target = new CharacterInventorySlot(
            1, 253231541, 2, "Unbound", "{}");

        Assert.False(ItemStackMergePolicy.CanMerge(
            rule, source, target));
    }
}
