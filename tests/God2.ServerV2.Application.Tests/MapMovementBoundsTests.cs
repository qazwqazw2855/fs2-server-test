using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class MapMovementBoundsTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(503, 503, true)]
    [InlineData(249, 246, true)]
    [InlineData(504, 246, false)]
    [InlineData(249, -1, false)]
    public void Map_bounds_include_edges_and_reject_outside(
        int x, int y, bool expected)
    {
        var bounds = new MapMovementBounds(
            170015000, 0, 503, 0, 503);

        Assert.Equal(expected, bounds.Contains(x, y));
    }
}
