using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class OfficialAttributeGrowthTests
{
    [Theory]
    [InlineData(GameplayClass.Swordsman, 2, 3, 0, 1)]
    [InlineData(GameplayClass.Taoist, 1, 0, 3, 2)]
    [InlineData(GameplayClass.Pharmacist, 2, 0, 2, 2)]
    [InlineData(GameplayClass.Warlock, 1, 1, 1, 3)]
    public void Character_rules_match_official_automatic_allocation(
        GameplayClass @class,
        int constitution,
        int strength,
        int intelligence,
        int speed)
    {
        var rule = OfficialAttributeGrowthCatalog.CreateDefault().CharacterRule(@class);

        Assert.Equal(new PrimaryAttributePoints(constitution, strength, intelligence, speed), rule.AutomaticPerLevel);
        Assert.Equal(6, rule.AutomaticPerLevel.Total);
        Assert.Equal(4, rule.ManualPointsPerLevel);
    }

    [Fact]
    public void Pet_budget_matches_official_point_totals_without_inventing_distribution()
    {
        var budget = OfficialAttributeGrowthCatalog.CreateDefault().PetBudget;

        Assert.Equal(4, budget.AutomaticPointsPerLevel);
        Assert.Equal(1, budget.ManualPointsPerLevel);
    }

    [Fact]
    public void Multi_level_award_applies_automatic_growth_and_manual_points_once()
    {
        var runtime = new CharacterProgressionRuntime(
            1,
            GameplayClass.Swordsman,
            ConservativeProgressionCatalog.CreateDefault());

        var result = runtime.AwardExperience("level-to-three", 400);
        var replay = runtime.AwardExperience("level-to-three", 400);

        Assert.Equal(3, result.CurrentLevel);
        Assert.Equal(2, result.LevelsGained);
        Assert.Equal(new PrimaryAttributePoints(4, 6, 0, 2), result.Snapshot.AutomaticAttributeGrowth);
        Assert.Equal(8, result.Snapshot.UnspentAttributePoints);
        Assert.Equal(ExperienceAwardResultCode.DuplicateCompleted, replay.ResultCode);
        Assert.Equal(result.Snapshot.AutomaticAttributeGrowth, replay.Snapshot.AutomaticAttributeGrowth);
        Assert.Equal(8, replay.Snapshot.UnspentAttributePoints);
    }

    [Fact]
    public void Every_class_receives_four_manual_points_for_one_level()
    {
        foreach (var @class in Enum.GetValues<GameplayClass>())
        {
            var runtime = new CharacterProgressionRuntime(
                100 + (int)@class,
                @class,
                ConservativeProgressionCatalog.CreateDefault());

            var result = runtime.AwardExperience($"{@class}-level-two", 100);

            Assert.Equal(2, result.CurrentLevel);
            Assert.Equal(4, result.Snapshot.UnspentAttributePoints);
            Assert.Equal(6, result.Snapshot.AutomaticAttributeGrowth!.Total);
        }
    }
}
