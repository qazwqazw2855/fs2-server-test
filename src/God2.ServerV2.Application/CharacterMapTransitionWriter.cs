namespace God2.ServerV2.Application;

public readonly record struct CharacterMapTransitionWriteRequest(
    long CharacterId,
    long MapId,
    int PositionX,
    int PositionY,
    long ExpectedRuntimeVersion,
    string ExpectedConcurrencyToken);

public readonly record struct CharacterMapTransitionWriteResult(
    bool Updated,
    long RuntimeVersion,
    string ConcurrencyToken)
{
    public static CharacterMapTransitionWriteResult Conflict =>
        new(false, 0, string.Empty);

    public static CharacterMapTransitionWriteResult Success(
        long runtimeVersion,
        string concurrencyToken) =>
        new(true, runtimeVersion, concurrencyToken);
}

public interface ICharacterMapTransitionWriter
{
    ValueTask<CharacterMapTransitionWriteResult> TryUpdateAsync(
        CharacterMapTransitionWriteRequest request,
        CancellationToken cancellationToken);
}
