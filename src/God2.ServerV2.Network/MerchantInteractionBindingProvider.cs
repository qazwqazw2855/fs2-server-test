using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public sealed class MerchantInteractionBindingProvider
{
    private readonly IMerchantInteractionAuthorityRepository _repository;

    public MerchantInteractionBindingProvider(
        IMerchantInteractionAuthorityRepository repository)
    {
        _repository = repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async ValueTask<MerchantInteractionBinding?> ResolveAsync(
        long spawnId,
        long mapId,
        string clientBuildId,
        uint clientEntityHandle,
        CancellationToken cancellationToken)
    {
        var authority = await _repository.ResolveAsync(
            spawnId,
            mapId,
            clientBuildId,
            clientEntityHandle,
            cancellationToken);

        if (authority is null)
            return null;

        return new MerchantInteractionBinding(
            authority.MerchantId,
            authority.NpcId,
            authority.SpawnId,
            authority.MapId,
            authority.ClientEntityHandle,
            authority.ClientBuildId,
            authority.MaximumDistance,
            authority.EvidenceReference,
            authority.Enabled);
    }
}
