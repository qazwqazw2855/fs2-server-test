using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Contracts;

namespace God2.ClassicServer.Application.Startup;

public sealed class CompositeRuntimeCacheBuilder : IRuntimeCacheBuilder
{
    private readonly IReadOnlyList<IRuntimeCacheBuilder> _builders;

    public CompositeRuntimeCacheBuilder(params IRuntimeCacheBuilder[] builders)
    {
        _builders = builders ?? throw new ArgumentNullException(nameof(builders));
        if (_builders.Count == 0 || _builders.Any(static builder => builder is null))
        {
            throw new ArgumentException("At least one runtime cache builder is required.", nameof(builders));
        }
    }

    public async Task<OperationResult> BuildAsync(
        IReadOnlyList<StaticDataLoadCount> staticData,
        CancellationToken cancellationToken)
    {
        foreach (var builder in _builders)
        {
            var result = await builder.BuildAsync(staticData, cancellationToken);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return OperationResult.Success;
    }
}
