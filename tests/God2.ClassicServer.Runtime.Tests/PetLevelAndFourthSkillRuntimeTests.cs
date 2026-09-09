using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class PetLevelAndFourthSkillRuntimeTests
{
    [Fact]
    public void AcquiredPetStartsAtLevelOneEvenWhenWildEncounterWasHigher()
    {
        var runtime = new PetRuntime(
            100,
            [new PetDefinition(7, 38, 100, [], "UserConfirmed")]);

        var acquired = runtime.Execute(new PetCommand("acquire", PetCommandType.Acquire, 7));

        Assert.Equal(PetResultCode.Success, acquired.ResultCode);
        Assert.Equal(1, acquired.Snapshot.Pets[7].Level);
    }

    [Fact]
    public void CapturedWildPetRetainsEncounterLevel()
    {
        var runtime = new PetRuntime(
            100,
            [new PetDefinition(7, 38, 100, [], "BahamutVerified")]);

        var acquired = runtime.Execute(new PetCommand(
            "capture",
            PetCommandType.Acquire,
            7,
            AcquisitionOrigin: PetAcquisitionOrigin.CapturedWild,
            WildEncounterLevel: 38));

        Assert.Equal(PetResultCode.Success, acquired.ResultCode);
        Assert.Equal(38, acquired.Snapshot.Pets[7].Level);
    }

    [Fact]
    public void PlayerPetLevelIsCalculatedFromOneAndCappedAtNinetyNine()
    {
        Assert.Equal(1, PetLevelRules.ResolvePlayerLevel(0));
        Assert.Equal(2, PetLevelRules.ResolvePlayerLevel(100));
        Assert.Equal(3, PetLevelRules.ResolvePlayerLevel(500));
        Assert.Equal(99, PetLevelRules.ResolvePlayerLevel(long.MaxValue));
    }

    [Fact]
    public void PetVitalsUseThePublishedPerThirtyPointAttributeContributions()
    {
        var baseStats = PetBaseStatRules.Create();

        Assert.Equal(550, PetVitalRules.CalculateMaximumHp(baseStats));
        Assert.Equal(135, PetVitalRules.CalculateMaximumMp(baseStats));
        Assert.Equal(47, PetVitalRules.CalculatePhysicalAttack(baseStats));
        Assert.Equal(35, PetVitalRules.CalculatePhysicalDefense(baseStats));
        Assert.Equal(47, PetVitalRules.CalculateMagicAttack(baseStats));
        Assert.Equal(30, PetVitalRules.CalculateMagicDefense(baseStats));
        Assert.Equal(120, PetVitalRules.CalculateMaximumHp(baseStats with { Constitution = baseStats.Constitution + 30 }) - PetVitalRules.CalculateMaximumHp(baseStats));
        Assert.Equal(30, PetVitalRules.CalculateMaximumMp(baseStats with { Intelligence = baseStats.Intelligence + 30 }) - PetVitalRules.CalculateMaximumMp(baseStats));
    }

    [Fact]
    public void FourthSkillRequiresLevelSixtyAndConsumesLearningItem()
    {
        var engine = new PetFourthSkillEngine();
        var tooLow = engine.Evaluate(new(9, 59, false, 12011, null, "獸之精  刀一  破星斬。瞬斬"));
        var accepted = engine.Evaluate(new(9, 60, false, 12011, null, "獸之精  刀一  破星斬。瞬斬"));

        Assert.False(tooLow.Succeeded);
        Assert.Equal("pet.fourth_skill.level_too_low", tooLow.Error.Code);
        Assert.True(accepted.Succeeded);
        Assert.Equal(4, accepted.Value!.SlotIndex);
        Assert.True(accepted.Value.ConsumeLearningItem);
    }

    [Fact]
    public void FourthSkillCannotOverwriteAnOccupiedSlot()
    {
        var result = new PetFourthSkillEngine().Evaluate(
            new(9, 60, true, 12011, null, "獸之精  刀一  破星斬。瞬斬"));

        Assert.False(result.Succeeded);
        Assert.Equal("pet.fourth_skill.slot_occupied", result.Error.Code);
    }

    [Fact]
    public void CaptureReplacesWildLevelAndStatsWithPlayerPetTemplate()
    {
        var wildStats = new PetBaseAttributes(9_999, 5_000, 500, 600, 700, 800, 9, 8, 7, 6, 5);
        var petStats = new PetBaseAttributes(120, 6, 100, 50, 50, 100, 0, 0, 0, 0, 0);

        var captured = new PetCaptureConversionEngine().Capture(
            new WildMonsterCaptureSource(88, 38, MonsterNameColor.White, false, false, MonsterCaptureEligibility.Capturable, wildStats),
            new CapturablePetTemplate(7, petStats));

        Assert.True(captured.Succeeded);
        Assert.Equal(38, captured.Value!.Level);
        Assert.Equal(88, captured.Value.WildSourceMonsterId);
        Assert.Equal(petStats, captured.Value.Attributes);
        Assert.NotEqual(wildStats, captured.Value.Attributes);
    }

    [Theory]
    [InlineData(MonsterNameColor.Yellow, false, "pet.capture.yellow_name_blocked")]
    [InlineData(MonsterNameColor.Red, false, "pet.capture.red_name_blocked")]
    [InlineData(MonsterNameColor.White, true, "pet.capture.quest_monster_blocked")]
    public void YellowRedAndQuestMonstersCannotBeCaptured(
        MonsterNameColor color,
        bool isQuestMonster,
        string expectedCode)
    {
        var stats = new PetBaseAttributes(100, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0);

        var result = new PetCaptureConversionEngine().Capture(
            new WildMonsterCaptureSource(88, 10, color, isQuestMonster, false, MonsterCaptureEligibility.Capturable, stats),
            new CapturablePetTemplate(7, stats));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void UnknownCaptureEligibilityFailsClosed()
    {
        var stats = new PetBaseAttributes(100, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0);

        var result = new PetCaptureConversionEngine().Capture(
            new WildMonsterCaptureSource(88, 10, MonsterNameColor.White, false, false, MonsterCaptureEligibility.Unknown, stats),
            new CapturablePetTemplate(7, stats));

        Assert.False(result.Succeeded);
        Assert.Equal("pet.capture.eligibility_unverified", result.Error.Code);
    }

    [Fact]
    public void FormationBossCannotBeCaptured()
    {
        var stats = new PetBaseAttributes(100, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0);

        var result = new PetCaptureConversionEngine().Capture(
            new WildMonsterCaptureSource(88, 10, MonsterNameColor.White, false, true, MonsterCaptureEligibility.Capturable, stats),
            new CapturablePetTemplate(7, stats));

        Assert.False(result.Succeeded);
        Assert.Equal("pet.capture.formation_boss_blocked", result.Error.Code);
    }

    [Fact]
    public void UnknownNameColorFailsClosed()
    {
        var stats = new PetBaseAttributes(100, 10, 10, 10, 10, 10, 0, 0, 0, 0, 0);

        var result = new PetCaptureConversionEngine().Capture(
            new WildMonsterCaptureSource(88, 10, MonsterNameColor.Unknown, false, false, MonsterCaptureEligibility.Capturable, stats),
            new CapturablePetTemplate(7, stats));

        Assert.False(result.Succeeded);
        Assert.Equal("pet.capture.name_color_unverified", result.Error.Code);
    }
}
