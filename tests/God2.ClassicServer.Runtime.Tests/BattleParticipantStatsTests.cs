using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class BattleParticipantStatsTests
{
    [Fact]
    public void Factory_converts_all_four_separated_entity_types()
    {
        var character = BattleParticipantStatsFactory.FromCharacter(Empty<CharacterBattleStatSource>(11));
        var pet = BattleParticipantStatsFactory.FromCharacterPet(Empty<CharacterPetBattleStatSource>(22));
        var immortal = BattleParticipantStatsFactory.FromCharacterImmortal(Empty<CharacterImmortalBattleStatSource>(33));
        var monster = BattleParticipantStatsFactory.FromMonster(Empty<MonsterBattleStatSource>(44));

        Assert.Equal((BattleParticipantEntityType.Character, 11L), (character.EntityType, character.EntityId));
        Assert.Equal((BattleParticipantEntityType.CharacterPet, 22L), (pet.EntityType, pet.EntityId));
        Assert.Equal((BattleParticipantEntityType.CharacterImmortal, 33L), (immortal.EntityType, immortal.EntityId));
        Assert.Equal((BattleParticipantEntityType.Monster, 44L), (monster.EntityType, monster.EntityId));
    }

    [Fact]
    public void Base_and_bonus_preserve_unknown_and_add_known_values()
    {
        var source = Empty<CharacterBattleStatSource>(7) with
        {
            StrengthBase = 10,
            StrengthBonus = 5,
            SpeedBase = null,
            SpeedBonus = null
        };

        var result = BattleParticipantStatsFactory.FromCharacter(source);

        Assert.Equal(15, result.Strength);
        Assert.Null(result.Speed);
        Assert.Null(result.Metal);
    }

    private static T Empty<T>(long identity) where T : class
    {
        var constructor = typeof(T).GetConstructors().Single();
        var arguments = new object?[constructor.GetParameters().Length];
        arguments[0] = identity;
        return (T)constructor.Invoke(arguments);
    }
}
