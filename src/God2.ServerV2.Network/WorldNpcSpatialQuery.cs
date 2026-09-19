using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public readonly record struct WorldNpcSpatialResult(
    WorldPresence Presence,
    NpcSnapshotEntry Npc,
    int DeltaX,
    int DeltaY,
    int ChebyshevDistance);

public sealed class WorldNpcSpatialQuery
{
    private readonly WorldPresenceRegistry _worldPresences;
    private readonly WorldNpcRegistry _worldNpcs;

    public WorldNpcSpatialQuery(
        WorldPresenceRegistry worldPresences,
        WorldNpcRegistry worldNpcs)
    {
        _worldPresences = worldPresences ??
            throw new ArgumentNullException(nameof(worldPresences));
        _worldNpcs = worldNpcs ??
            throw new ArgumentNullException(nameof(worldNpcs));
    }

    public bool TryResolve(
        long connectionId,
        uint clientEntityHandle,
        out WorldNpcSpatialResult result)
    {
        if (!_worldPresences.TryGetByConnection(
                connectionId,
                out var presence) ||
            presence is null ||
            presence.Character.MapId is not long mapId ||
            presence.Character.PositionX is not int playerX ||
            presence.Character.PositionY is not int playerY)
        {
            result = default;
            return false;
        }

        if (!_worldNpcs.TryGetByClientEntityHandle(
                mapId,
                clientEntityHandle,
                out var npc) ||
            npc is null ||
            npc.PositionX is not int npcX ||
            npc.PositionY is not int npcY)
        {
            result = default;
            return false;
        }

        var deltaX = Math.Abs(playerX - npcX);
        var deltaY = Math.Abs(playerY - npcY);

        result = new WorldNpcSpatialResult(
            presence,
            npc,
            deltaX,
            deltaY,
            Math.Max(deltaX, deltaY));

        return true;
    }
}
