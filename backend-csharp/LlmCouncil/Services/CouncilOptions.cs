namespace LlmCouncil.Services;

/// <summary>
/// Configuration for the LLM Council, mirrors backend/config.py.
/// Values are read from appsettings.json / environment variables.
/// </summary>
public class CouncilOptions
{
    public const string SectionName = "Council";

    /// <summary>LLM provider to use: OpenRouter or LiteLLM.</summary>
    public string LlmProvider { get; set; } = "OpenRouter";

    /// <summary>OpenRouter API key.</summary>
    public string OpenRouterApiKey { get; set; } = string.Empty;

    /// <summary>Optional LiteLLM API key (if your LiteLLM instance requires auth).</summary>
    public string LiteLlmApiKey { get; set; } = string.Empty;

    /// <summary>Council member model identifiers.</summary>
    public List<string> CouncilModels { get; set; } =
    [
        "openai/gpt-5.1",
        "google/gemini-3-pro-preview",
        "anthropic/claude-sonnet-4.5",
        "x-ai/grok-4",
    ];

    /// <summary>Chairman model that synthesizes the final response.</summary>
    public string ChairmanModel { get; set; } = "google/gemini-3-pro-preview";

    /// <summary>OpenRouter chat completions endpoint.</summary>
    public string OpenRouterApiUrl { get; set; } = "https://openrouter.ai/api/v1/chat/completions";

    /// <summary>LiteLLM chat completions endpoint.</summary>
    public string LiteLlmApiUrl { get; set; } = "http://localhost:4000/v1/chat/completions";

    /// <summary>Directory where conversation JSON files are stored.</summary>
    public string DataDir { get; set; } = "data/conversations";
}
