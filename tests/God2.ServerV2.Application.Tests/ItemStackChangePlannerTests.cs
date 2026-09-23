using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class ItemStackChangePlannerTests
{
    [Theory]
    [InlineData(50, 1, 49, false)]
    [InlineData(50, 20, 2, true)]
    [InlineData(1, 1, 0, true)]
    public void Preview_checks_free_slots_before_lowering_limit(
        int quantity,
        int newMaximum,
        int expectedAdditional,
        bool expectedFits)
    {
        var inventory = new CharacterInventorySnapshot(
            Guid.NewGuid(),
            1,
            32,
            1,
            1,
            "Clean",
            [new CharacterInventorySlot(0, 253231541, quantity)]);

        var preview = ItemStackChangePlanner.Preview(
            inventory, 253231541, newMaximum);

        Assert.Equal(expectedAdditional, preview.AdditionalSlotsNeeded);
        Assert.Equal(31, preview.FreeSlots);
        Assert.Equal(expectedFits, preview.Fits);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 2)]
    public void Preview_rejects_duplicate_or_out_of_range_slots(
        int firstSlot,
        int secondSlot)
    {
        var inventory = new CharacterInventorySnapshot(
            Guid.NewGuid(),
            1,
            2,
            1,
            1,
            "Clean",
            [
                new CharacterInventorySlot(firstSlot, 253231541, 1),
                new CharacterInventorySlot(secondSlot, 987654321, 1)
            ]);

        Assert.Throws<InvalidDataException>(() =>
            ItemStackChangePlanner.Preview(inventory, 253231541, 20));
    }

    [Fact]
    public void Preview_rejects_invalid_unrelated_slot()
    {
        var inventory = new CharacterInventorySnapshot(
            Guid.NewGuid(),
            1,
            2,
            1,
            1,
            "Clean",
            [
                new CharacterInventorySlot(0, 253231541, 1),
                new CharacterInventorySlot(1, 987654321, 0)
            ]);

        Assert.Throws<InvalidDataException>(() =>
            ItemStackChangePlanner.Preview(inventory, 253231541, 20));
    }
}
