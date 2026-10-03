using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace God2.ServerV2.Application;

// Loaded by trusted content infrastructure, never from a client request.
// Eligibility and source approval must be checked before registration.
public sealed record QuestAcceptanceDefinition(
    long QuestId,
    bool Repeatable,
    int MinimumLevel,
    int? MaximumLevel,
    string EvidenceReference,
    IReadOnlyList<QuestObjectiveProgress> Objectives,
    IReadOnlyList<QuestRewardEntry> Rewards);

public sealed record QuestAcceptanceSnapshot(
    long QuestId,
    int MinimumLevel,
    int? MaximumLevel,
    string DefinitionFingerprint,
    string ObjectivesFingerprint,
    string RewardFingerprint,
    string EvidenceReference,
    string ObjectivesJson,
    string RewardJson);

public static class QuestAcceptanceDefinitionValidator
{
    public static QuestAcceptanceSnapshot Prepare(
        QuestAcceptanceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.QuestId <= 0 ||
            definition.MinimumLevel < 1 ||
            definition.MaximumLevel is < 1 ||
            definition.MaximumLevel is int maximum &&
                maximum < definition.MinimumLevel)
            throw new InvalidDataException("Invalid quest identity or levels.");

        if (definition.Repeatable)
            throw new InvalidDataException(
                "Repeatable quest acceptance is not supported.");

        if (string.IsNullOrWhiteSpace(definition.EvidenceReference) ||
            definition.EvidenceReference.Length > 500)
            throw new InvalidDataException("Invalid quest evidence reference.");

        if (definition.Objectives is null ||
            definition.Objectives.Count == 0 ||
            definition.Rewards is null ||
            definition.Rewards.Count == 0)
            throw new InvalidDataException(
                "Complete objectives and item rewards are required.");

        var objectiveIds = new HashSet<long>();
        foreach (var objective in definition.Objectives)
        {
            if (objective is null ||
                objective.ObjectiveId <= 0 ||
                !objectiveIds.Add(objective.ObjectiveId) ||
                !Enum.IsDefined(objective.Kind) ||
                objective.TargetId <= 0 ||
                objective.RequiredCount <= 0 ||
                objective.CurrentCount != 0)
                throw new InvalidDataException(
                    "Invalid initial quest objective.");
        }

        var rewardIds = new HashSet<long>();
        foreach (var reward in definition.Rewards)
        {
            if (reward is null ||
                reward.RewardId <= 0 ||
                !rewardIds.Add(reward.RewardId) ||
                reward.RewardType != "Item" ||
                reward.ItemId is not long item ||
                item <= 0 || item > int.MaxValue ||
                reward.Quantity is not int quantity || quantity <= 0 ||
                reward.Experience is not null ||
                reward.Currency is not null ||
                !string.IsNullOrEmpty(reward.SelectionGroup))
                throw new InvalidDataException(
                    "Invalid or unsupported quest reward.");
        }

        // Persist these exact strings; reward verification hashes raw JSON.
        var objectivesJson = JsonSerializer.Serialize(
            definition.Objectives.OrderBy(x => x.ObjectiveId).ToArray());
        var rewardJson = JsonSerializer.Serialize(
            definition.Rewards.OrderBy(x => x.RewardId).ToArray());

        var objectivesFingerprint = Hash(objectivesJson);
        var rewardFingerprint = Hash(rewardJson);

        var definitionJson = JsonSerializer.Serialize(new
        {
            Format = "God2.ServerV2.QuestAcceptance/1",
            definition.QuestId,
            definition.Repeatable,
            definition.MinimumLevel,
            definition.MaximumLevel,
            definition.EvidenceReference,
            ObjectivesFingerprint = objectivesFingerprint,
            RewardFingerprint = rewardFingerprint
        });

        return new QuestAcceptanceSnapshot(
            definition.QuestId,
            definition.MinimumLevel,
            definition.MaximumLevel,
            Hash(definitionJson),
            objectivesFingerprint,
            rewardFingerprint,
            definition.EvidenceReference,
            objectivesJson,
            rewardJson);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
