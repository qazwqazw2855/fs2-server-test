using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;

namespace God2.ServerV2.Application.Tests;

public sealed class QuestAcceptanceDefinitionTests
{
    private static QuestAcceptanceDefinition Definition() =>
        new(
            123, false, 1, null, "FixtureOnly:acceptance",
            [
                new QuestObjectiveProgress(
                    2, QuestObjectiveKind.InteractNpc, 200, 1, 0),
                new QuestObjectiveProgress(
                    1, QuestObjectiveKind.DefeatMonster, 100, 2, 0)
            ],
            [
                new QuestRewardEntry(2, "Item", 253231541, 1),
                new QuestRewardEntry(1, "Item", 253231541, 1)
            ]);

    [Fact]
    public void Fingerprints_match_persisted_json_and_progress_normalization()
    {
        var result = QuestAcceptanceDefinitionValidator.Prepare(Definition());
        var objectives =
            JsonSerializer.Deserialize<QuestObjectiveProgress[]>(
                result.ObjectivesJson);
        Assert.NotNull(objectives);
        Assert.Equal(1, objectives[0].ObjectiveId);
        Assert.All(objectives, x => Assert.Equal(0, x.CurrentCount));

        var normalized = JsonSerializer.Serialize(
            objectives.Select(x => x with { CurrentCount = 0 }).ToArray());

        Assert.Equal(Hash(normalized), result.ObjectivesFingerprint);
        Assert.Equal(Hash(result.RewardJson), result.RewardFingerprint);
    }

    [Fact]
    public void Input_order_does_not_change_definition_identity()
    {
        var original = Definition();
        var reordered = original with
        {
            Objectives = original.Objectives.Reverse().ToArray(),
            Rewards = original.Rewards.Reverse().ToArray()
        };

        Assert.Equal(
            QuestAcceptanceDefinitionValidator.Prepare(original),
            QuestAcceptanceDefinitionValidator.Prepare(reordered));
    }

    [Fact]
    public void Changing_reward_or_eligibility_changes_definition_identity()
    {
        var original = Definition();
        var baseline = QuestAcceptanceDefinitionValidator.Prepare(original);

        Assert.NotEqual(baseline.DefinitionFingerprint,
            QuestAcceptanceDefinitionValidator.Prepare(
                original with { MinimumLevel = 2 }).DefinitionFingerprint);

        Assert.NotEqual(baseline.DefinitionFingerprint,
            QuestAcceptanceDefinitionValidator.Prepare(
                original with
                {
                    Rewards =
                    [
                        original.Rewards[0] with { Quantity = 2 },
                        original.Rewards[1]
                    ]
                }).DefinitionFingerprint);
    }

    [Fact]
    public void Repeatable_definition_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                Definition() with { Repeatable = true }));
    }

    [Fact]
    public void Nonzero_initial_progress_is_rejected()
    {
        var definition = Definition();
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                definition with
                {
                    Objectives =
                    [
                        definition.Objectives[0] with { CurrentCount = 1 }
                    ]
                }));
    }

    [Fact]
    public void Duplicate_objectives_are_rejected()
    {
        var definition = Definition();
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                definition with
                {
                    Objectives =
                    [
                        definition.Objectives[0],
                        definition.Objectives[0]
                    ]
                }));
    }

    [Fact]
    public void Empty_rewards_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                Definition() with { Rewards = [] }));
    }

    [Fact]
    public void Mixed_currency_reward_is_rejected()
    {
        var definition = Definition();
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                definition with
                {
                    Rewards =
                    [
                        definition.Rewards[0] with { Currency = 1 }
                    ]
                }));
    }

    [Fact]
    public void Invalid_level_range_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            QuestAcceptanceDefinitionValidator.Prepare(
                Definition() with { MinimumLevel = 10, MaximumLevel = 5 }));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
