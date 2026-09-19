using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class PortalRouteServiceTests
{
    [Fact]
    public async Task Resolves_exact_verified_portal_position()
    {
        var service = new PortalRouteService(
            new StubRepository([
                Entry()
            ]));

        var route = await service.ResolveAsync(
            170015000,
            249,
            246,
            CancellationToken.None);

        Assert.NotNull(route);
        Assert.Equal(170015007, route.DestinationMapId);
        Assert.Equal(48, route.DestinationX);
        Assert.Equal(81, route.DestinationY);
        Assert.Equal((ushort)7, route.DestinationClientMapId);
        Assert.Equal((byte)15, route.DestinationClientAreaId);
    }

    [Fact]
    public async Task Resolves_position_inside_source_radius()
    {
        var service = new PortalRouteService(
            new StubRepository([
                Entry() with { SourceRadius = 2 }
            ]));

        var route = await service.ResolveAsync(
            170015000,
            251,
            244,
            CancellationToken.None);

        Assert.NotNull(route);
        Assert.Equal(170015007, route.DestinationMapId);
    }

    [Fact]
    public async Task Returns_null_when_no_portal_matches_position()
    {
        var service = new PortalRouteService(
            new StubRepository([
                Entry()
            ]));

        var route = await service.ResolveAsync(
            170015000,
            248,
            246,
            CancellationToken.None);

        Assert.Null(route);
    }

    [Fact]
    public async Task Rejects_ambiguous_portal_matches()
    {
        var service = new PortalRouteService(
            new StubRepository([
                Entry(),
                Entry() with
                {
                    PortalId = 170015008,
                    SourceRadius = 1
                }
            ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ResolveAsync(
                170015000,
                249,
                246,
                CancellationToken.None));
    }

    private static PortalRouteEntry Entry() =>
        new(
            170015007,
            "Observed Stage 3 Route",
            170015000,
            249,
            246,
            0,
            "god2-opt-6b127086e0c0",
            0,
            15,
            170015007,
            48,
            81,
            "god2-opt-6b127086e0c0",
            7,
            15);

    private sealed class StubRepository(
        IReadOnlyList<PortalRouteEntry> entries) :
        IPortalRouteRepository
    {
        public ValueTask<IReadOnlyList<PortalRouteEntry>>
            ListBySourceMapAsync(
                long sourceMapId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(entries);
        }
    }
}
