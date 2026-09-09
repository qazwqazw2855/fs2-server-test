using God2.ClassicServer.Domain;

namespace God2.ClassicServer.Domain.Tests;

public sealed class DomainFoundationTests
{
    [Fact]
    public void Character_requires_name()
    {
        var accountId = new AccountId(1);
        var characterId = new CharacterId(1);
        var position = new WorldPosition(new MapId(1), 100, 100);

        Assert.Throws<ArgumentException>(() => new Character(characterId, accountId, string.Empty, position));
    }
}
