using System.Text;
using System.Text.RegularExpressions;
using LlmCouncil.Models;
using Microsoft.Extensions.Options;

namespace LlmCouncil.Services;

/// <summary>
/// Orchestrates the 3-stage council process, mirroring backend/council.py.
/// </summary>
public partial class CouncilService(OpenRouterService openRouter, IOptions<CouncilOptions> options, ILogger<CouncilService> logger)
{
    private readonly CouncilOptions _options = options.Value;

    // ── Stage 1 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collect individual responses from all council models in parallel.
    /// </summary>
    public async Task<List<Stage1Result>> Stage1CollectResponsesAsync(string userQuery)
    {
        var messages = new[] { new OpenRouterMessage("user", userQuery) };
        var responses = await openRouter.QueryModelsParallelAsync(_options.CouncilModels, messages);

        return responses
            .Where(kv => kv.Value is not null)
            .Select(kv => new Stage1Result
            {
                Model = kv.Key,
                Response = kv.Value!.Content ?? string.Empty,
            })
            .ToList();
    }

    // ── Stage 2 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Each council model ranks the anonymised responses from Stage 1.
    /// Returns the ranking results and the label→model mapping used for de-anonymisation.
    /// </summary>
    public async Task<(List<Stage2Result> Rankings, Dictionary<string, string> LabelToModel)>
        Stage2CollectRankingsAsync(string userQuery, List<Stage1Result> stage1Results)
    {
        // Assign anonymous labels: Response A, Response B, …
        var labels = Enumerable.Range(0, stage1Results.Count)
                               .Select(i => ((char)('A' + i)).ToString())
                               .ToList();

        var labelToModel = labels
            .Zip(stage1Results)
            .ToDictionary(p => $"Response {p.First}", p => p.Second.Model);

        // Build the responses text block
        var responsesText = new StringBuilder();
        for (int i = 0; i < stage1Results.Count; i++)
        {
            responsesText.AppendLine($"Response {labels[i]}:");
            responsesText.AppendLine(stage1Results[i].Response);
            responsesText.AppendLine();
        }

        var rankingPrompt = $"""
            You are evaluating different responses to the following question:

            Question: {userQuery}

            Here are the responses from different models (anonymized):

            {responsesText}
            Your task:
            1. First, evaluate each response individually. For each response, explain what it does well and what it does poorly.
            2. Then, at the very end of your response, provide a final ranking.

            IMPORTANT: Your final ranking MUST be formatted EXACTLY as follows:
            - Start with the line "FINAL RANKING:" (all caps, with colon)
            - Then list the responses from best to worst as a numbered list
            - Each line should be: number, period, space, then ONLY the response label (e.g., "1. Response A")
            - Do not add any other text or explanations in the ranking section

            Example of the correct format for your ENTIRE response:

            Response A provides good detail on X but misses Y...
            Response B is accurate but lacks depth on Z...
            Response C offers the most comprehensive answer...

            FINAL RANKING:
            1. Response C
            2. Response A
            3. Response B

            Now provide your evaluation and ranking:
            """;

        var messages = new[] { new OpenRouterMessage("user", rankingPrompt) };
        var responses = await openRouter.QueryModelsParallelAsync(_options.CouncilModels, messages);

        var stage2Results = responses
            .Where(kv => kv.Value is not null)
            .Select(kv =>
            {
                var fullText = kv.Value!.Content ?? string.Empty;
                return new Stage2Result
                {
                    Model = kv.Key,
                    Ranking = fullText,
                    ParsedRanking = ParseRankingFromText(fullText),
                };
            })
            .ToList();

        return (stage2Results, labelToModel);
    }

    // ── Stage 3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Chairman model synthesises a final answer from all stage results.
    /// </summary>
    public async Task<Stage3Result> Stage3SynthesizeFinalAsync(
        string userQuery,
        List<Stage1Result> stage1Results,
        List<Stage2Result> stage2Results)
    {
        var stage1Text = string.Join("\n\n", stage1Results.Select(r =>
            $"Model: {r.Model}\nResponse: {r.Response}"));

        var stage2Text = string.Join("\n\n", stage2Results.Select(r =>
            $"Model: {r.Model}\nRanking: {r.Ranking}"));

        var chairmanPrompt = $"""
            You are the Chairman of an LLM Council. Multiple AI models have provided responses to a user's question, and then ranked each other's responses.

            Original Question: {userQuery}

            STAGE 1 - Individual Responses:
            {stage1Text}

            STAGE 2 - Peer Rankings:
            {stage2Text}

            Your task as Chairman is to synthesize all of this information into a single, comprehensive, accurate answer to the user's original question. Consider:
            - The individual responses and their insights
            - The peer rankings and what they reveal about response quality
            - Any patterns of agreement or disagreement

            Provide a clear, well-reasoned final answer that represents the council's collective wisdom:
            """;

        var messages = new[] { new OpenRouterMessage("user", chairmanPrompt) };
        var response = await openRouter.QueryModelAsync(_options.ChairmanModel, messages);

        if (response is null)
        {
            logger.LogError("Chairman model {Model} failed to respond", _options.ChairmanModel);
            return new Stage3Result
            {
                Model = _options.ChairmanModel,
                Response = "Error: Unable to generate final synthesis.",
            };
        }

        return new Stage3Result
        {
            Model = _options.ChairmanModel,
            Response = response.Content ?? string.Empty,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the "FINAL RANKING:" section from a model's ranking response.
    /// </summary>
    public static List<string> ParseRankingFromText(string rankingText)
    {
        const string header = "FINAL RANKING:";
        if (rankingText.Contains(header))
        {
            var afterHeader = rankingText[(rankingText.IndexOf(header, StringComparison.Ordinal) + header.Length)..];

            // Try numbered list: "1. Response A"
            var numbered = NumberedResponseRegex().Matches(afterHeader);
            if (numbered.Count > 0)
                return numbered.Select(m => ResponseLabelRegex().Match(m.Value).Value).ToList();

            // Fallback: any "Response X" in order
            return ResponseLabelRegex().Matches(afterHeader).Select(m => m.Value).ToList();
        }

        // Final fallback: scan full text
        return ResponseLabelRegex().Matches(rankingText).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// Computes average rank position for each model across all evaluations.
    /// </summary>
    public static List<AggregateRanking> CalculateAggregateRankings(
        List<Stage2Result> stage2Results,
        Dictionary<string, string> labelToModel)
    {
        var modelPositions = new Dictionary<string, List<int>>();

        foreach (var ranking in stage2Results)
        {
            var parsed = ParseRankingFromText(ranking.Ranking);
            for (int i = 0; i < parsed.Count; i++)
            {
                var label = parsed[i];
                if (!labelToModel.TryGetValue(label, out var modelName)) continue;

                if (!modelPositions.TryGetValue(modelName, out var positions))
                {
                    positions = [];
                    modelPositions[modelName] = positions;
                }
                positions.Add(i + 1);
            }
        }

        return modelPositions
            .Select(kv => new AggregateRanking
            {
                Model = kv.Key,
                AverageRank = Math.Round(kv.Value.Average(), 2),
                RankingsCount = kv.Value.Count,
            })
            .OrderBy(a => a.AverageRank)
            .ToList();
    }

    /// <summary>
    /// Generate a short title for the first message of a conversation.
    /// </summary>
    public async Task<string> GenerateConversationTitleAsync(string userQuery)
    {
        var prompt = $"""
            Generate a very short title (3-5 words maximum) that summarizes the following question.
            The title should be concise and descriptive. Do not use quotes or punctuation in the title.

            Question: {userQuery}

            Title:
            """;

        var messages = new[] { new OpenRouterMessage("user", prompt) };
        var response = await openRouter.QueryModelAsync("google/gemini-2.5-flash", messages, TimeSpan.FromSeconds(30));

        if (response?.Content is null) return "New Conversation";

        var title = response.Content.Trim().Trim('"', '\'');
        return title.Length > 50 ? title[..47] + "..." : title;
    }

    /// <summary>
    /// Run the full 3-stage council process.
    /// </summary>
    public async Task<(List<Stage1Result> Stage1, List<Stage2Result> Stage2, Stage3Result Stage3, CouncilMetadata Metadata)>
        RunFullCouncilAsync(string userQuery)
    {
        var stage1Results = await Stage1CollectResponsesAsync(userQuery);

        if (stage1Results.Count == 0)
        {
            return ([], [], new Stage3Result
            {
                Model = "error",
                Response = "All models failed to respond. Please try again.",
            }, new CouncilMetadata());
        }

        var (stage2Results, labelToModel) = await Stage2CollectRankingsAsync(userQuery, stage1Results);
        var aggregateRankings = CalculateAggregateRankings(stage2Results, labelToModel);
        var stage3Result = await Stage3SynthesizeFinalAsync(userQuery, stage1Results, stage2Results);

        var metadata = new CouncilMetadata
        {
            LabelToModel = labelToModel,
            AggregateRankings = aggregateRankings,
        };

        return (stage1Results, stage2Results, stage3Result, metadata);
    }

    [GeneratedRegex(@"\d+\.\s*Response [A-Z]")]
    private static partial Regex NumberedResponseRegex();

    [GeneratedRegex(@"Response [A-Z]")]
    private static partial Regex ResponseLabelRegex();
}
