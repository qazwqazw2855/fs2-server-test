using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class Portal2RouteIntegrationTests
{
    [Fact]
    public async Task Portal2_IsLoadedAndResolvesFromVerifiedSourcePosition()
    {
        if (Environment.GetEnvironmentVariable("GOD2_RUN_DB_INTEGRATION") != "1")
            throw new InvalidOperationException("DB integration test must be explicitly enabled.");

        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Missing {name}");

        var options = new MariaDbAuthenticationOptions(
            Required("GOD2_DB_HOST"),
            int.Parse(Required("GOD2_DB_PORT")),
            Required("GOD2_DB_USER"),
            Required("GOD2_DB_PASSWORD"));

        var repository = new MariaDbPortalRouteRepository(options);
        var route = Assert.Single(
            await repository.ListBySourceMapAsync(557790525, CancellationToken.None),
            entry => entry.PortalId == 2);

        Assert.Equal(557790525, route.SourceMapId);
        Assert.Equal((16, 20, 1),
            (route.SourceX, route.SourceY, route.SourceRadius));
        Assert.Equal(("god2-opt-6b127086e0c0", (ushort)19, (byte)4),
            (route.SourceClientBuildId, route.SourceClientMapId, route.SourceClientAreaId));

        Assert.Equal(1675308248, route.DestinationMapId);
        Assert.Equal((196, 139), (route.DestinationX, route.DestinationY));
        Assert.Equal(("god2-opt-6b127086e0c0", (ushort)3, (byte)4),
            (route.DestinationClientBuildId, route.DestinationClientMapId,
             route.DestinationClientAreaId));

        var resolved = await new PortalRouteService(repository).ResolveAsync(
            557790525, 16, 20, CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.PortalId);

        var outside = await new PortalRouteService(repository).ResolveAsync(
            557790525, 18, 20, CancellationToken.None);
        Assert.Null(outside);

        var encoded = OfficialPortalWireCodec.SerializeVerifiedClientDestination(
            route.DestinationClientMapId,
            route.DestinationClientAreaId,
            route.DestinationX,
            route.DestinationY);
        Assert.True(encoded.Succeeded, encoded.FailureCode);
        Assert.NotNull(encoded.Value);

        var decoded = OfficialPortalWireCodec.DecodeResult(
            encoded.Value.PreludeDecodedFrame.Span,
            encoded.Value.MapTransitionDecodedFrame.Span);
        Assert.True(decoded.Succeeded, decoded.FailureCode);
        Assert.NotNull(decoded.Value);
        Assert.Equal((byte)4, decoded.Value.AreaId);
        Assert.Equal((ushort)3, decoded.Value.ClientMapId);
        Assert.Equal((ushort)196, decoded.Value.X);
        Assert.Equal((ushort)139, decoded.Value.Y);
    }
}
