using System.Text.Json;
using God2.RecoveredDatabaseImporter;

namespace God2.RecoveredDatabaseImporter.Tests;

public sealed class SemanticOracleTests
{
    [Fact]
    public void Compare_PropertyOrderOnly_IsSemanticallyEquivalent()
    {
        using var expected = JsonDocument.Parse("""{"player":{"hp":10,"mp":5},"tick":7}""");
        using var actual = JsonDocument.Parse("""{"tick":7,"player":{"mp":5,"hp":10}}""");

        var result = SemanticOracle.Compare(expected.RootElement, actual.RootElement, "fixture/replay.json", "Runtime.State");

        Assert.True(result.Equivalent);
        Assert.Equal(result.ExpectedHash, result.ActualHash);
        Assert.Null(result.Counterexample);
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("100", "1e2")]
    [InlineData("-0", "0.000")]
    [InlineData("0.0100", "1e-2")]
    public void Compare_EquivalentJsonNumberSpellings_AreSemanticallyEquivalent(string expectedNumber, string actualNumber)
    {
        using var expected = JsonDocument.Parse($"{{\"value\":{expectedNumber}}}");
        using var actual = JsonDocument.Parse($"{{\"value\":{actualNumber}}}");

        var result = SemanticOracle.Compare(expected.RootElement, actual.RootElement, "fixture/numbers.json", "Runtime.Formula");

        Assert.True(result.Equivalent);
        Assert.Equal(result.ExpectedHash, result.ActualHash);
    }

    [Fact]
    public void Compare_ValueMismatch_EmitsMinimalCounterexample()
    {
        using var expected = JsonDocument.Parse("""{"player":{"hp":10,"mp":5},"tick":7}""");
        using var actual = JsonDocument.Parse("""{"player":{"hp":9,"mp":5},"tick":7}""");

        var result = SemanticOracle.Compare(expected.RootElement, actual.RootElement, "fixture/replay.json", "Battle.DamageFormula");

        Assert.False(result.Equivalent);
        Assert.NotEqual(result.ExpectedHash, result.ActualHash);
        var counterexample = Assert.IsType<OracleCounterexample>(result.Counterexample);
        Assert.Equal("fixture/replay.json", counterexample.SourceEvidence);
        Assert.Equal("10", counterexample.ExpectedState);
        Assert.Equal("9", counterexample.ActualState);
        Assert.Equal("Battle.DamageFormula", counterexample.ResponsibleSubsystem);
        Assert.Equal("$.player.hp", counterexample.FirstDivergencePath);
    }

    [Fact]
    public void Compare_MissingArrayElement_ReportsExactIndex()
    {
        using var expected = JsonDocument.Parse("""{"events":[{"id":1},{"id":2}]}""");
        using var actual = JsonDocument.Parse("""{"events":[{"id":1}]}""");

        var result = SemanticOracle.Compare(expected.RootElement, actual.RootElement, "fixture/trace.jsonl", "Protocol.Dispatch");

        var counterexample = Assert.IsType<OracleCounterexample>(result.Counterexample);
        Assert.Equal("$.events[1]", counterexample.FirstDivergencePath);
        Assert.Equal("<missing>", counterexample.ActualState);
    }
}
