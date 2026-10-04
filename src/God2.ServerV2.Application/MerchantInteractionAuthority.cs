namespace God2.ServerV2.Application;

// Formal server-authority projection for binding an observed merchant NPC
// to a merchant definition. Interaction distance remains unapproved until
// independently supported by evidence.
public sealed record MerchantInteractionAuthority(
    long MerchantId,
    long NpcId,
    long SpawnId,
    long MapId,
    uint ClientEntityHandle,
    string ClientBuildId,
    string EvidenceReference,
    int? MaximumDistance,
    bool Enabled);

public interface IMerchantInteractionAuthorityRepository
{
    ValueTask<MerchantInteractionAuthority?> ResolveAsync(
        long spawnId,
        long mapId,
        string clientBuildId,
        uint clientEntityHandle,
        CancellationToken cancellationToken);
}
