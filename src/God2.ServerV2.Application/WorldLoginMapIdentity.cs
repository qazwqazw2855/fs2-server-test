namespace God2.ServerV2.Application;

public sealed record WorldLoginMapIdentity(
    long MapId,
    string ClientBuildId,
    ushort ClientMapId,
    byte ClientAreaId,
    int MinimumX,
    int MaximumX,
    int MinimumY,
    int MaximumY)
{
    public bool MatchesBuildAndContains(string clientBuildId, int x, int y) =>
        string.Equals(ClientBuildId, clientBuildId, StringComparison.Ordinal) &&
        Contains(x, y);

    public bool Contains(int x, int y) =>
        x >= MinimumX && x <= MaximumX && y >= MinimumY && y <= MaximumY;
}

public interface IWorldLoginMapIdentityRepository
{
    ValueTask<WorldLoginMapIdentity?> GetByMapAsync(
        long mapId, CancellationToken cancellationToken);
}
