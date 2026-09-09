using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed record PetFourthSkillLearningRequest(
    long PetInstanceId,
    int PetLevel,
    bool FourthSlotOccupied,
    long LearningItemId,
    long? TaughtSkillId,
    string TaughtSkillNameZhTw);

public sealed record PetFourthSkillLearningResult(
    int SlotIndex,
    long LearningItemId,
    long? TaughtSkillId,
    string TaughtSkillNameZhTw,
    bool ConsumeLearningItem);

public sealed class PetFourthSkillEngine
{
    public const int FourthSkillSlot = 4;
    public const int RequiredPetLevel = 60;

    public OperationResult<PetFourthSkillLearningResult> Evaluate(PetFourthSkillLearningRequest request)
    {
        if (request.PetInstanceId <= 0 || request.LearningItemId <= 0 || string.IsNullOrWhiteSpace(request.TaughtSkillNameZhTw))
        {
            return Failure("pet.fourth_skill.invalid_request", "The pet, learning item and taught skill are required.");
        }
        if (request.PetLevel < RequiredPetLevel)
        {
            return Failure("pet.fourth_skill.level_too_low", "The battle pet must reach level 60 before learning its fourth skill.");
        }
        if (request.FourthSlotOccupied)
        {
            return Failure("pet.fourth_skill.slot_occupied", "The fourth battle-pet skill slot is already occupied.");
        }

        return OperationResult<PetFourthSkillLearningResult>.Success(new(
            FourthSkillSlot,
            request.LearningItemId,
            request.TaughtSkillId,
            request.TaughtSkillNameZhTw.Trim(),
            true));
    }

    private static OperationResult<PetFourthSkillLearningResult> Failure(string code, string message) =>
        OperationResult<PetFourthSkillLearningResult>.Failure(code, message, nameof(PetFourthSkillEngine));
}
