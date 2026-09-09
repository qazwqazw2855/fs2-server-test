namespace God2.ClassicServer.Runtime;

public enum BattleParticipantEntityType
{
    Character,
    CharacterPet,
    CharacterImmortal,
    Monster
}

/// <summary>
/// 統一的回合制戰鬥數值投影。來源資料庫仍維持角色、戰寵、神仙與怪物各自正規化的資料表。
/// NULL 代表未知，轉換過程不得把未知值當成 0。
/// </summary>
public sealed record BattleParticipantStats(
    BattleParticipantEntityType EntityType,
    long EntityId,
    int? Level,
    long? CurrentHp,
    long? MaximumHp,
    long? CurrentMp,
    long? MaximumMp,
    int? Strength,
    int? Constitution,
    int? Intelligence,
    int? Speed,
    int? Metal,
    int? Wood,
    int? Water,
    int? Fire,
    int? Earth,
    int? PhysicalAttack,
    int? PhysicalDefense,
    int? MagicAttack,
    int? MagicDefense);

public sealed record CharacterBattleStatSource(
    long CharacterId, int? Level, long? CurrentHp, long? MaximumHp, long? CurrentMp, long? MaximumMp,
    int? StrengthBase, int? StrengthBonus, int? ConstitutionBase, int? ConstitutionBonus,
    int? IntelligenceBase, int? IntelligenceBonus, int? SpeedBase, int? SpeedBonus,
    int? MetalBase, int? MetalBonus, int? WoodBase, int? WoodBonus, int? WaterBase, int? WaterBonus,
    int? FireBase, int? FireBonus, int? EarthBase, int? EarthBonus,
    int? PhysicalAttackBase, int? PhysicalAttackBonus, int? PhysicalDefenseBase, int? PhysicalDefenseBonus,
    int? MagicAttackBase, int? MagicAttackBonus, int? MagicDefenseBase, int? MagicDefenseBonus);

public sealed record CharacterPetBattleStatSource(
    long PetInstanceId, int? Level, long? CurrentHp, long? MaximumHp, long? CurrentMp, long? MaximumMp,
    int? StrengthBase, int? StrengthBonus, int? ConstitutionBase, int? ConstitutionBonus,
    int? IntelligenceBase, int? IntelligenceBonus, int? SpeedBase, int? SpeedBonus,
    int? MetalBase, int? MetalBonus, int? WoodBase, int? WoodBonus, int? WaterBase, int? WaterBonus,
    int? FireBase, int? FireBonus, int? EarthBase, int? EarthBonus,
    int? PhysicalAttackBase, int? PhysicalAttackBonus, int? PhysicalDefenseBase, int? PhysicalDefenseBonus,
    int? MagicAttackBase, int? MagicAttackBonus, int? MagicDefenseBase, int? MagicDefenseBonus);

public sealed record CharacterImmortalBattleStatSource(
    long ImmortalInstanceId, int? Level, long? CurrentHp, long? MaximumHp, long? CurrentMp, long? MaximumMp,
    int? StrengthBase, int? StrengthBonus, int? ConstitutionBase, int? ConstitutionBonus,
    int? IntelligenceBase, int? IntelligenceBonus, int? SpeedBase, int? SpeedBonus,
    int? MetalBase, int? MetalBonus, int? WoodBase, int? WoodBonus, int? WaterBase, int? WaterBonus,
    int? FireBase, int? FireBonus, int? EarthBase, int? EarthBonus,
    int? PhysicalAttackBase, int? PhysicalAttackBonus, int? PhysicalDefenseBase, int? PhysicalDefenseBonus,
    int? MagicAttackBase, int? MagicAttackBonus, int? MagicDefenseBase, int? MagicDefenseBonus);

public sealed record MonsterBattleStatSource(
    long MonsterId, int? Level, long? MaximumHp, long? MaximumMp,
    int? Strength, int? Constitution, int? Intelligence, int? Speed,
    int? Metal, int? Wood, int? Water, int? Fire, int? Earth,
    int? PhysicalAttack, int? PhysicalDefense, int? MagicAttack, int? MagicDefense);

public static class BattleParticipantStatsFactory
{
    public static BattleParticipantStats FromCharacter(CharacterBattleStatSource value) =>
        FromSeparated(BattleParticipantEntityType.Character, value.CharacterId, value.Level, value.CurrentHp, value.MaximumHp,
            value.CurrentMp, value.MaximumMp, value.StrengthBase, value.StrengthBonus, value.ConstitutionBase,
            value.ConstitutionBonus, value.IntelligenceBase, value.IntelligenceBonus, value.SpeedBase, value.SpeedBonus,
            value.MetalBase, value.MetalBonus, value.WoodBase, value.WoodBonus, value.WaterBase, value.WaterBonus,
            value.FireBase, value.FireBonus, value.EarthBase, value.EarthBonus, value.PhysicalAttackBase,
            value.PhysicalAttackBonus, value.PhysicalDefenseBase, value.PhysicalDefenseBonus, value.MagicAttackBase,
            value.MagicAttackBonus, value.MagicDefenseBase, value.MagicDefenseBonus);

    public static BattleParticipantStats FromCharacterWithActiveImmortal(
        CharacterBattleStatSource value,
        OfficialImmortalInheritableAttributes activeImmortal) =>
        FromCharacter(OfficialImmortalAttributeInheritance.ApplyToCharacter(value, activeImmortal));

    public static BattleParticipantStats FromCharacterPet(CharacterPetBattleStatSource value) =>
        FromSeparated(BattleParticipantEntityType.CharacterPet, value.PetInstanceId, value.Level, value.CurrentHp, value.MaximumHp,
            value.CurrentMp, value.MaximumMp, value.StrengthBase, value.StrengthBonus, value.ConstitutionBase,
            value.ConstitutionBonus, value.IntelligenceBase, value.IntelligenceBonus, value.SpeedBase, value.SpeedBonus,
            value.MetalBase, value.MetalBonus, value.WoodBase, value.WoodBonus, value.WaterBase, value.WaterBonus,
            value.FireBase, value.FireBonus, value.EarthBase, value.EarthBonus, value.PhysicalAttackBase,
            value.PhysicalAttackBonus, value.PhysicalDefenseBase, value.PhysicalDefenseBonus, value.MagicAttackBase,
            value.MagicAttackBonus, value.MagicDefenseBase, value.MagicDefenseBonus);

    public static BattleParticipantStats FromCharacterImmortal(CharacterImmortalBattleStatSource value) =>
        FromSeparated(BattleParticipantEntityType.CharacterImmortal, value.ImmortalInstanceId, value.Level, value.CurrentHp, value.MaximumHp,
            value.CurrentMp, value.MaximumMp, value.StrengthBase, value.StrengthBonus, value.ConstitutionBase,
            value.ConstitutionBonus, value.IntelligenceBase, value.IntelligenceBonus, value.SpeedBase, value.SpeedBonus,
            value.MetalBase, value.MetalBonus, value.WoodBase, value.WoodBonus, value.WaterBase, value.WaterBonus,
            value.FireBase, value.FireBonus, value.EarthBase, value.EarthBonus, value.PhysicalAttackBase,
            value.PhysicalAttackBonus, value.PhysicalDefenseBase, value.PhysicalDefenseBonus, value.MagicAttackBase,
            value.MagicAttackBonus, value.MagicDefenseBase, value.MagicDefenseBonus);

    public static BattleParticipantStats FromMonster(MonsterBattleStatSource value) =>
        new(BattleParticipantEntityType.Monster, value.MonsterId, value.Level, value.MaximumHp, value.MaximumHp,
            value.MaximumMp, value.MaximumMp, value.Strength, value.Constitution, value.Intelligence, value.Speed,
            value.Metal, value.Wood, value.Water, value.Fire, value.Earth, value.PhysicalAttack, value.PhysicalDefense,
            value.MagicAttack, value.MagicDefense);

    private static int? Add(int? left, int? right) =>
        left is null && right is null ? null : checked((left ?? 0) + (right ?? 0));

    private static BattleParticipantStats FromSeparated(
        BattleParticipantEntityType type, long id, int? level, long? currentHp, long? maximumHp, long? currentMp, long? maximumMp,
        int? strengthBase, int? strengthBonus, int? constitutionBase, int? constitutionBonus,
        int? intelligenceBase, int? intelligenceBonus, int? speedBase, int? speedBonus,
        int? metalBase, int? metalBonus, int? woodBase, int? woodBonus, int? waterBase, int? waterBonus,
        int? fireBase, int? fireBonus, int? earthBase, int? earthBonus,
        int? physicalAttackBase, int? physicalAttackBonus, int? physicalDefenseBase, int? physicalDefenseBonus,
        int? magicAttackBase, int? magicAttackBonus, int? magicDefenseBase, int? magicDefenseBonus) =>
        new(type, id, level, currentHp, maximumHp, currentMp, maximumMp,
            Add(strengthBase, strengthBonus), Add(constitutionBase, constitutionBonus),
            Add(intelligenceBase, intelligenceBonus), Add(speedBase, speedBonus),
            Add(metalBase, metalBonus), Add(woodBase, woodBonus), Add(waterBase, waterBonus),
            Add(fireBase, fireBonus), Add(earthBase, earthBonus), Add(physicalAttackBase, physicalAttackBonus),
            Add(physicalDefenseBase, physicalDefenseBonus), Add(magicAttackBase, magicAttackBonus),
            Add(magicDefenseBase, magicDefenseBonus));
}
