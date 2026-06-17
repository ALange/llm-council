namespace LlmCouncil.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateConversationRequest;

public record SendMessageRequest(string Content, bool FinalOnly = false);

// ── Storage / persistence models ─────────────────────────────────────────────

public class Conversation
{
    public string Id { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string Title { get; set; } = "New Conversation";
    public List<object> Messages { get; set; } = [];
}

public class ConversationMetadata
{
    public string Id { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string Title { get; set; } = "New Conversation";
    public int MessageCount { get; set; }
}

// ── Council stage models ──────────────────────────────────────────────────────

public class Stage1Result
{
    public string Model { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}

public class Stage2Result
{
    public string Model { get; set; } = string.Empty;
    public string Ranking { get; set; } = string.Empty;
    public List<string> ParsedRanking { get; set; } = [];
}

public class Stage3Result
{
    public string Model { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
}

public class AggregateRanking
{
    public string Model { get; set; } = string.Empty;
    public double AverageRank { get; set; }
    public int RankingsCount { get; set; }
}

public class CouncilMetadata
{
    public Dictionary<string, string> LabelToModel { get; set; } = [];
    public List<AggregateRanking> AggregateRankings { get; set; } = [];
}

// ── API response ──────────────────────────────────────────────────────────────

public class MessageResponse
{
    public List<Stage1Result> Stage1 { get; set; } = [];
    public List<Stage2Result> Stage2 { get; set; } = [];
    public Stage3Result Stage3 { get; set; } = new();
    public CouncilMetadata? Metadata { get; set; }
}

// ── OpenRouter API shapes ─────────────────────────────────────────────────────

public record OpenRouterMessage(string Role, string Content);

public class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = [];
}

public class OpenRouterResponse
{
    public List<OpenRouterChoice> Choices { get; set; } = [];
}

public class OpenRouterChoice
{
    public OpenRouterChoiceMessage Message { get; set; } = new();
}

public class OpenRouterChoiceMessage
{
    public string? Content { get; set; }
    public object? ReasoningDetails { get; set; }
}

public class ModelResponse
{
    public string? Content { get; set; }
    public object? ReasoningDetails { get; set; }
}
