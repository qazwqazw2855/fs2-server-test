namespace God2.ServerV2.Application;

public readonly record struct CharacterPositionWriteRequest(
    long CharacterId,
    int PositionX,
    int PositionY,
    long ExpectedRuntimeVersion,
    string ExpectedConcurrencyToken);

public readonly record struct CharacterPositionWriteResult(
    bool Updated,
    long RuntimeVersion,
    string ConcurrencyToken)
{
    public static CharacterPositionWriteResult Conflict =>
        new(false, 0, string.Empty);

    public static CharacterPositionWriteResult Success(
        long runtimeVersion,
        string concurrencyToken) =>
        new(true, runtimeVersion, concurrencyToken);
}

public interface ICharacterPositionWriter
{
    ValueTask<CharacterPositionWriteResult> TryUpdateAsync(
        CharacterPositionWriteRequest request,
        CancellationToken cancellationToken);
}
