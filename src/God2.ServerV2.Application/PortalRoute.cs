namespace God2.ServerV2.Application;

public sealed record PortalRouteEntry(
    long PortalId,
    string Name,
    long SourceMapId,
    int SourceX,
    int SourceY,
    int SourceRadius,
    string SourceClientBuildId,
    ushort SourceClientMapId,
    byte SourceClientAreaId,
    long DestinationMapId,
    int DestinationX,
    int DestinationY,
    string DestinationClientBuildId,
    ushort DestinationClientMapId,
    byte DestinationClientAreaId);

public interface IPortalRouteRepository
{
    ValueTask<IReadOnlyList<PortalRouteEntry>> ListBySourceMapAsync(
        long sourceMapId,
        CancellationToken cancellationToken);
}

public sealed class EmptyPortalRouteRepository :
    IPortalRouteRepository
{
    public ValueTask<IReadOnlyList<PortalRouteEntry>> ListBySourceMapAsync(
        long sourceMapId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<PortalRouteEntry>>([]);
    }
}

public sealed class PortalRouteService
{
    private readonly IPortalRouteRepository _repository;

    public PortalRouteService(IPortalRouteRepository repository)
    {
        _repository = repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async ValueTask<PortalRouteEntry?> ResolveAsync(
        long sourceMapId,
        int positionX,
        int positionY,
        CancellationToken cancellationToken)
    {
        if (sourceMapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceMapId));
        }

        var entries = await _repository.ListBySourceMapAsync(
            sourceMapId,
            cancellationToken);

        if (entries.Any(entry => entry.SourceMapId != sourceMapId))
        {
            throw new InvalidOperationException(
                "The portal repository returned a route from another source map.");
        }

        if (entries
            .GroupBy(entry => entry.PortalId)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The portal repository returned duplicate portal IDs.");
        }

        var matches = entries
            .Where(entry =>
            {
                if (entry.SourceRadius < 0)
                {
                    throw new InvalidOperationException(
                        "The portal repository returned a negative source radius.");
                }

                var deltaX = Math.Abs((long)positionX - entry.SourceX);
                var deltaY = Math.Abs((long)positionY - entry.SourceY);

                return Math.Max(deltaX, deltaY) <= entry.SourceRadius;
            })
            .OrderBy(entry => entry.PortalId)
            .ToArray();

        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "The authoritative position matches multiple portal routes.")
        };
    }
}
