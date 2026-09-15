namespace God2.ServerV2.Application;

public sealed record CharacterListEntry(
    long CharacterId,
    long AccountId,
    string Name,
    string? ClassCode,
    string? GenderCode,
    string? LifeSkillCode,
    int? Level,
    string? AppearanceCode,
    long? MapId,
    int? PositionX,
    int? PositionY,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastPlayedAtUtc,
    long RuntimeVersion = 0,
    string ConcurrencyToken = "");

public interface ICharacterListRepository
{
    ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
        long accountId,
        CancellationToken cancellationToken);
}

public sealed class EmptyCharacterListRepository :
    ICharacterListRepository
{
    public ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<CharacterListEntry>>([]);
    }
}

public sealed class CharacterListService
{
    private readonly ICharacterListRepository _repository;

    public CharacterListService(ICharacterListRepository repository)
    {
        _repository = repository ??
            throw new ArgumentNullException(nameof(repository));
    }

    public async ValueTask<IReadOnlyList<CharacterListEntry>> GetAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        var characters = await _repository.ListByAccountAsync(
            accountId,
            cancellationToken);

        if (characters.Any(character => character.AccountId != accountId))
        {
            throw new InvalidOperationException(
                "The character repository returned a row owned by another account.");
        }

        if (characters
            .GroupBy(character => character.CharacterId)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The character repository returned duplicate character IDs.");
        }

        return characters
            .OrderBy(character => character.CreatedAtUtc)
            .ThenBy(character => character.CharacterId)
            .ToArray();
    }
}
