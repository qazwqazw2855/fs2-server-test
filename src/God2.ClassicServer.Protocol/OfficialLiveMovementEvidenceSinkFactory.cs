namespace God2.ClassicServer.Protocol;

public static class OfficialLiveMovementEvidenceSinkFactory
{
    public static IOfficialLiveMovementEvidenceSink CreateJsonlOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NullOfficialLiveMovementEvidenceSink.Instance;
        }

        return new JsonlOfficialLiveMovementEvidenceSink(path);
    }
}
