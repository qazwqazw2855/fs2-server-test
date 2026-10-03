namespace God2.ServerV2.Network;

public sealed class MerchantInteractionResolver
{
    private readonly NpcInteractionSessionRegistry _interactions;
    private readonly WorldPresenceRegistry _presences;
    private readonly WorldNpcRegistry _npcs;

    public MerchantInteractionResolver(
        NpcInteractionSessionRegistry interactions,
        WorldPresenceRegistry presences,
        WorldNpcRegistry npcs)
    {
        _interactions = interactions ??
            throw new ArgumentNullException(nameof(interactions));
        _presences = presences ??
            throw new ArgumentNullException(nameof(presences));
        _npcs = npcs ??
            throw new ArgumentNullException(nameof(npcs));
    }

    // Binding must come from reviewed server content.
    // The result is not a transaction lease or a reusable authorization token.
    public MerchantInteractionStatus Check(
        long connectionId,
        Guid expectedInteractionId,
        long merchantId,
        MerchantInteractionBinding binding)
    {
        if (connectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        ArgumentNullException.ThrowIfNull(binding);

        if (expectedInteractionId == Guid.Empty ||
            !_interactions.TryGetByConnection(connectionId, out var interaction) ||
            interaction!.InteractionId != expectedInteractionId)
            return MerchantInteractionStatus.InteractionConflict;

        if (!_presences.TryGetByConnection(connectionId, out var presence) ||
            presence is null)
            return MerchantInteractionStatus.WorldPresenceMissing;

        if (presence.Character.MapId is not long mapId || mapId <= 0)
            return MerchantInteractionStatus.MapMismatch;

        if (!_npcs.TryGetByClientEntityHandle(
                mapId, interaction.ClientEntityHandle, out var npc) ||
            npc is null)
            return MerchantInteractionStatus.NpcMissing;

        var player = new MerchantInteractionPlayer(
            presence.Character.CharacterId,
            mapId,
            presence.Character.PositionX,
            presence.Character.PositionY);

        var status = MerchantInteractionPolicy.Check(
            merchantId, expectedInteractionId,
            interaction, player, npc, binding);

        if (status != MerchantInteractionStatus.Allowed)
            return status;

        // Reject changes observed during this check. Changes after returning
        // still require serialization at the command execution boundary.
        if (!_interactions.IsCurrent(connectionId, expectedInteractionId) ||
            !_presences.TryGetByConnection(connectionId, out var currentPresence) ||
            !ReferenceEquals(presence, currentPresence) ||
            !_npcs.TryGetByClientEntityHandle(
                mapId, interaction.ClientEntityHandle, out var currentNpc) ||
            !ReferenceEquals(npc, currentNpc))
            return MerchantInteractionStatus.StateChanged;

        return MerchantInteractionStatus.Allowed;
    }
}
