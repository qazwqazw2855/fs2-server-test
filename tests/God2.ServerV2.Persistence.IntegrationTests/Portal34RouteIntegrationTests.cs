using God2.ServerV2.Application;
using God2.ServerV2.Persistence;
using God2.ServerV2.Protocol;

namespace God2.ServerV2.Persistence.IntegrationTests;

public sealed class Portal34RouteIntegrationTests
{
    [Theory]
    [InlineData(3, 130139698L, 284, 452, 192354557L, 52, 184, 0, 43)]
    [InlineData(4, 192354557L, 51, 185, 130139698L, 283, 453, 43, 0)]
    public async Task Route_ResolvesAndSerializesVerifiedDestination(
        long portalId, long sourceMapId, int sourceX, int sourceY,
        long destinationMapId, int destinationX, int destinationY,
        int sourceClientMap, int destinationClientMap)
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
            await repository.ListBySourceMapAsync(sourceMapId, CancellationToken.None),
            entry => entry.PortalId == portalId);

        Assert.Equal((sourceMapId, sourceX, sourceY, 8),
            (route.SourceMapId, route.SourceX, route.SourceY, route.SourceRadius));
        Assert.Equal(("god2-opt-6b127086e0c0", (ushort)sourceClientMap, (byte)2),
            (route.SourceClientBuildId, route.SourceClientMapId, route.SourceClientAreaId));
        Assert.Equal((destinationMapId, destinationX, destinationY),
            (route.DestinationMapId, route.DestinationX, route.DestinationY));
        Assert.Equal(("god2-opt-6b127086e0c0", (ushort)destinationClientMap, (byte)2),
            (route.DestinationClientBuildId, route.DestinationClientMapId,
             route.DestinationClientAreaId));

        var service = new PortalRouteService(repository);
        var resolved = await service.ResolveAsync(
            sourceMapId, sourceX, sourceY, CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal(portalId, resolved.PortalId);

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
        Assert.Equal((byte)2, decoded.Value.AreaId);
        Assert.Equal((ushort)destinationClientMap, decoded.Value.ClientMapId);
        Assert.Equal((ushort)destinationX, decoded.Value.X);
        Assert.Equal((ushort)destinationY, decoded.Value.Y);
    }
}
