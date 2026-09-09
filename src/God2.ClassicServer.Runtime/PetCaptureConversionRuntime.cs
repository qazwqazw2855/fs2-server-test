using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed record PetBaseAttributes(
    long MaximumHp,
    long MaximumMp,
    int Constitution,
    int Strength,
    int Intelligence,
    int Speed,
    int Metal,
    int Wood,
    int Water,
    int Fire,
    int Earth);

public enum MonsterNameColor
{
    White,
    Yellow,
    Red,
    Unknown
}

public enum MonsterCaptureEligibility
{
    Unknown,
    Capturable,
    Blocked
}

public sealed record WildMonsterCaptureSource(
    long MonsterId,
    int EncounterLevel,
    MonsterNameColor NameColor,
    bool IsQuestMonster,
    bool IsFormationBoss,
    MonsterCaptureEligibility CaptureEligibility,
    PetBaseAttributes CombatAttributes);

public sealed record CapturablePetTemplate(
    int PetTemplateId,
    PetBaseAttributes PlayerPetAttributesAtCaptureLevel);

public sealed record CapturedPetState(
    int PetTemplateId,
    long WildSourceMonsterId,
    int Level,
    PetBaseAttributes Attributes);

public sealed class PetCaptureConversionEngine
{
    public OperationResult<CapturedPetState> Capture(
        WildMonsterCaptureSource wildMonster,
        CapturablePetTemplate petTemplate)
    {
        if (wildMonster.MonsterId <= 0 || wildMonster.EncounterLevel <= 0 || petTemplate.PetTemplateId <= 0)
        {
            return OperationResult<CapturedPetState>.Failure(
                "pet.capture.invalid_source",
                "A valid wild monster and battle-pet template are required.",
                nameof(PetCaptureConversionEngine));
        }
        if (wildMonster.NameColor == MonsterNameColor.Yellow)
        {
            return Failure("pet.capture.yellow_name_blocked", "Yellow-name monsters cannot be captured.");
        }
        if (wildMonster.NameColor == MonsterNameColor.Red)
        {
            return Failure("pet.capture.red_name_blocked", "Red-name monsters cannot be captured.");
        }
        if (wildMonster.IsQuestMonster)
        {
            return Failure("pet.capture.quest_monster_blocked", "Quest monsters cannot be captured.");
        }
        if (wildMonster.IsFormationBoss)
        {
            return Failure("pet.capture.formation_boss_blocked", "Formation bosses cannot be captured.");
        }
        if (wildMonster.NameColor != MonsterNameColor.White)
        {
            return Failure("pet.capture.name_color_unverified", "Only verified white-name monsters can be captured.");
        }
        if (wildMonster.CaptureEligibility != MonsterCaptureEligibility.Capturable)
        {
            return Failure("pet.capture.eligibility_unverified", "Monster capture eligibility is not verified.");
        }

        return OperationResult<CapturedPetState>.Success(new(
            petTemplate.PetTemplateId,
            wildMonster.MonsterId,
            wildMonster.EncounterLevel,
            petTemplate.PlayerPetAttributesAtCaptureLevel));
    }

    private static OperationResult<CapturedPetState> Failure(string code, string message) =>
        OperationResult<CapturedPetState>.Failure(code, message, nameof(PetCaptureConversionEngine));
}
