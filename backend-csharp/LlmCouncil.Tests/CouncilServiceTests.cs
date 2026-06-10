using LlmCouncil.Models;
using LlmCouncil.Services;

namespace LlmCouncil.Tests;

/// <summary>
/// Unit tests for CouncilService helper methods that do NOT require network access.
/// </summary>
public class CouncilServiceTests
{
    // ── ParseRankingFromText ──────────────────────────────────────────────────

    [Fact]
    public void ParseRankingFromText_WithFinalRankingSection_ExtractsOrderedLabels()
    {
        var text = """
            Response A provides detail but misses context.
            Response B is accurate.
            Response C is comprehensive.

            FINAL RANKING:
            1. Response C
            2. Response A
            3. Response B
            """;

        var result = CouncilService.ParseRankingFromText(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("Response C", result[0]);
        Assert.Equal("Response A", result[1]);
        Assert.Equal("Response B", result[2]);
    }

    [Fact]
    public void ParseRankingFromText_WithoutFinalRankingHeader_FallsBackToInOrderMatches()
    {
        var text = "I think Response B is best, then Response A, finally Response C.";

        var result = CouncilService.ParseRankingFromText(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("Response B", result[0]);
        Assert.Equal("Response A", result[1]);
        Assert.Equal("Response C", result[2]);
    }

    [Fact]
    public void ParseRankingFromText_EmptyString_ReturnsEmptyList()
    {
        var result = CouncilService.ParseRankingFromText(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseRankingFromText_RankingSectionWithNoLabels_ReturnsEmpty()
    {
        var text = "FINAL RANKING:\nJust some text with no labels.";
        var result = CouncilService.ParseRankingFromText(text);
        Assert.Empty(result);
    }

    // ── CalculateAggregateRankings ────────────────────────────────────────────

    [Fact]
    public void CalculateAggregateRankings_TwoModels_CorrectAverageAndOrder()
    {
        var labelToModel = new Dictionary<string, string>
        {
            ["Response A"] = "model-alpha",
            ["Response B"] = "model-beta",
        };

        // Both evaluators agree: A is #1, B is #2
        var stage2Results = new List<Stage2Result>
        {
            new()
            {
                Model = "evaluator-1",
                Ranking = "FINAL RANKING:\n1. Response A\n2. Response B",
                ParsedRanking = ["Response A", "Response B"],
            },
            new()
            {
                Model = "evaluator-2",
                Ranking = "FINAL RANKING:\n1. Response A\n2. Response B",
                ParsedRanking = ["Response A", "Response B"],
            },
        };

        var result = CouncilService.CalculateAggregateRankings(stage2Results, labelToModel);

        Assert.Equal(2, result.Count);
        Assert.Equal("model-alpha", result[0].Model);
        Assert.Equal(1.0, result[0].AverageRank);
        Assert.Equal(2, result[0].RankingsCount);
        Assert.Equal("model-beta", result[1].Model);
        Assert.Equal(2.0, result[1].AverageRank);
    }

    [Fact]
    public void CalculateAggregateRankings_DisagreementAmongEvaluators_AveragesCorrectly()
    {
        var labelToModel = new Dictionary<string, string>
        {
            ["Response A"] = "model-alpha",
            ["Response B"] = "model-beta",
        };

        var stage2Results = new List<Stage2Result>
        {
            // evaluator-1 prefers A
            new()
            {
                Model = "evaluator-1",
                Ranking = "FINAL RANKING:\n1. Response A\n2. Response B",
                ParsedRanking = ["Response A", "Response B"],
            },
            // evaluator-2 prefers B
            new()
            {
                Model = "evaluator-2",
                Ranking = "FINAL RANKING:\n1. Response B\n2. Response A",
                ParsedRanking = ["Response B", "Response A"],
            },
        };

        var result = CouncilService.CalculateAggregateRankings(stage2Results, labelToModel);

        // Both should have average rank of 1.5
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(1.5, r.AverageRank));
    }

    [Fact]
    public void CalculateAggregateRankings_EmptyInputs_ReturnsEmptyList()
    {
        var result = CouncilService.CalculateAggregateRankings([], []);
        Assert.Empty(result);
    }

    // ── Label generation (A, B, C, …) ────────────────────────────────────────

    [Theory]
    [InlineData(0, 'A')]
    [InlineData(1, 'B')]
    [InlineData(25, 'Z')]
    public void CharArithmetic_ProducesExpectedLetters(int index, char expected)
    {
        var label = (char)('A' + index);
        Assert.Equal(expected, label);
    }
}
