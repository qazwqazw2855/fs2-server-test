using God2.AdvancedHeadlessVerification;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class AdvancedFinalFreezeGateTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var scenario in AdvancedFinalFreezeGate.RequiredNegativeScenarios())
        {
            yield return [scenario.Id, scenario.Input, scenario.ExpectedFreezeReady];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Gate_is_fail_closed_for_required_negative_scenarios(
        string id,
        AdvancedFinalFreezeGateInput input,
        bool expectedReady)
    {
        var actual = AdvancedFinalFreezeGate.Evaluate(input);

        Assert.Equal(expectedReady, actual.FreezeReady);
        Assert.Equal(expectedReady ? AdvancedFinalFreezeGate.PassedStatus : AdvancedFinalFreezeGate.BlockedStatus, actual.FinalStatus);
        if (expectedReady)
        {
            Assert.StartsWith("21_", id, StringComparison.Ordinal);
            Assert.Empty(actual.Blockers);
        }
        else
        {
            Assert.NotEmpty(actual.Blockers);
        }
    }
}
