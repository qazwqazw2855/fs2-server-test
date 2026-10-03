using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

// Supplied by reviewed server content, never by a client request.
// A null distance limit means the interaction policy is not approved.
public sealed record MerchantInteractionBinding(
    long MerchantId,
    long NpcId,
    long SpawnId,
    long MapId,
    uint ClientEntityHandle,
    string ClientBuildId,
    int? MaximumDistance,
    string EvidenceReference,
    bool Enabled);

// Read from current authoritative world presence.
public sealed record MerchantInteractionPlayer(
    long CharacterId,
    long MapId,
    int? PositionX,
    int? PositionY);

public enum MerchantInteractionStatus
{
    Allowed,
    BindingBlocked,
    InteractionConflict,
    CharacterMismatch,
    MapMismatch,
    NpcMismatch,
    ClientBuildMismatch,
    PositionUnknown,
    OutOfRange,
    WorldPresenceMissing,
    NpcMissing,
    StateChanged
}

public static class MerchantInteractionPolicy
{
    // Checks supplied snapshots only. It does not reserve the interaction
    // or authorize a later asynchronous transaction.
    public static MerchantInteractionStatus Check(
        long merchantId,
        Guid expectedInteractionId,
        NpcInteractionSession interaction,
        MerchantInteractionPlayer player,
        NpcSnapshotEntry npc,
        MerchantInteractionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(binding);

        if (merchantId <= 0 || !binding.Enabled ||
            binding.MerchantId != merchantId ||
            binding.NpcId <= 0 || binding.SpawnId <= 0 ||
            binding.MapId <= 0 || binding.ClientEntityHandle == 0 ||
            string.IsNullOrWhiteSpace(binding.ClientBuildId) ||
            string.IsNullOrWhiteSpace(binding.EvidenceReference) ||
            binding.MaximumDistance is not int maximumDistance ||
            maximumDistance < 0)
            return MerchantInteractionStatus.BindingBlocked;

        if (expectedInteractionId == Guid.Empty ||
            interaction.InteractionId != expectedInteractionId)
            return MerchantInteractionStatus.InteractionConflict;

        if (player.CharacterId <= 0 ||
            interaction.CharacterId != player.CharacterId)
            return MerchantInteractionStatus.CharacterMismatch;

        if (player.MapId != binding.MapId ||
            interaction.MapId != binding.MapId ||
            npc.MapId != binding.MapId)
            return MerchantInteractionStatus.MapMismatch;

        if (interaction.SpawnId != binding.SpawnId ||
            interaction.ClientEntityHandle != binding.ClientEntityHandle ||
            npc.SpawnId != binding.SpawnId ||
            npc.NpcId != binding.NpcId ||
            npc.ClientEntityHandle != binding.ClientEntityHandle)
            return MerchantInteractionStatus.NpcMismatch;

        if (!string.Equals(npc.ClientBuildId, binding.ClientBuildId,
                StringComparison.Ordinal))
            return MerchantInteractionStatus.ClientBuildMismatch;

        if (player.PositionX is not int playerX ||
            player.PositionY is not int playerY ||
            npc.PositionX is not int npcX ||
            npc.PositionY is not int npcY)
            return MerchantInteractionStatus.PositionUnknown;

        var distance = Math.Max(
            Math.Abs((long)playerX - npcX),
            Math.Abs((long)playerY - npcY));

        return distance <= maximumDistance
            ? MerchantInteractionStatus.Allowed
            : MerchantInteractionStatus.OutOfRange;
    }
}
