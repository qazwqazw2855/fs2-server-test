namespace God2.ClassicServer.Domain;

public readonly record struct AccountId
{
    public AccountId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Account id must be positive.");
        }

        Value = value;
    }

    public long Value { get; }
}

public readonly record struct CharacterId
{
    public CharacterId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Character id must be positive.");
        }

        Value = value;
    }

    public long Value { get; }
}

public readonly record struct MapId
{
    public MapId(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Map id must be positive.");
        }

        Value = value;
    }

    public int Value { get; }
}

public sealed record WorldPosition(MapId MapId, int X, int Y);

public sealed record Account
{
    public Account(AccountId id, string loginName)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new ArgumentException("Login name is required.", nameof(loginName));
        }

        Id = id;
        LoginName = loginName;
    }

    public AccountId Id { get; }

    public string LoginName { get; }
}

public sealed record Character
{
    public Character(CharacterId id, AccountId accountId, string name, WorldPosition position)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Character name is required.", nameof(name));
        }

        Id = id;
        AccountId = accountId;
        Name = name;
        Position = position;
    }

    public CharacterId Id { get; }

    public AccountId AccountId { get; }

    public string Name { get; }

    public WorldPosition Position { get; }
}

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

public interface IDomainRule
{
    string Code { get; }

    bool IsSatisfied();
}
