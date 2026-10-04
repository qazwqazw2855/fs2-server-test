using God2.ServerV2.Application;
using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class MerchantInteractionBindingProviderTests
{
    [Fact]
    public async Task Resolve_projects_authority_without_promoting_blocked_state()
    {
        var authority = new MerchantInteractionAuthority(
            MerchantId: 170015954,
            NpcId: 170015081,
            SpawnId: 170153954,
            MapId: 170015007,
            ClientEntityHandle: 3954,
            ClientBuildId: "god2-opt-6b127086e0c0",
            EvidenceReference: "",
            MaximumDistance: null,
            Enabled: false);

        var provider = new MerchantInteractionBindingProvider(
            new StubRepository(authority));

        var binding = await provider.ResolveAsync(
            170153954,
            170015007,
            "god2-opt-6b127086e0c0",
            3954,
            CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal(authority.MerchantId, binding.MerchantId);
        Assert.Equal(authority.NpcId, binding.NpcId);
        Assert.Equal(authority.SpawnId, binding.SpawnId);
        Assert.Equal(authority.MapId, binding.MapId);
        Assert.Equal(
            authority.ClientEntityHandle,
            binding.ClientEntityHandle);
        Assert.Equal(authority.ClientBuildId, binding.ClientBuildId);
        Assert.Equal(
            authority.EvidenceReference,
            binding.EvidenceReference);
        Assert.Null(binding.MaximumDistance);
        Assert.False(binding.Enabled);
    }

    [Fact]
    public async Task Resolve_returns_null_when_authority_is_missing()
    {
        var provider = new MerchantInteractionBindingProvider(
            new StubRepository(null));

        var binding = await provider.ResolveAsync(
            170153954,
            170015007,
            "god2-opt-6b127086e0c0",
            3954,
            CancellationToken.None);

        Assert.Null(binding);
    }

    private sealed class StubRepository(
        MerchantInteractionAuthority? result) :
        IMerchantInteractionAuthorityRepository
    {
        public ValueTask<MerchantInteractionAuthority?> ResolveAsync(
            long spawnId,
            long mapId,
            string clientBuildId,
            uint clientEntityHandle,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }
}
