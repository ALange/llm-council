using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmCouncil.Models;
using Microsoft.Extensions.Options;

namespace LlmCouncil.Services;

/// <summary>
/// Wraps the OpenRouter REST API, mirroring backend/openrouter.py.
/// </summary>
public class OpenRouterService(HttpClient httpClient, IOptions<CouncilOptions> options, ILogger<OpenRouterService> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CouncilOptions _options = options.Value;

    /// <summary>
    /// Query a single model via configured endpoint. Returns null on failure (graceful degradation).
    /// </summary>
    public async Task<ModelResponse?> QueryConfiguredModelAsync(
        RuntimeCouncilModel configuredModel,
        IEnumerable<OpenRouterMessage> messages,
        TimeSpan? timeout = null)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, configuredModel.ChatCompletionsUrl);
            if (!string.IsNullOrWhiteSpace(configuredModel.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuredModel.ApiKey);
            }

            var body = new OpenRouterRequest
            {
                Model = configuredModel.ModelId,
                Messages = messages.ToList(),
            };

            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var cts = timeout.HasValue
                ? new CancellationTokenSource(timeout.Value)
                : new CancellationTokenSource(TimeSpan.FromSeconds(120));

            var response = await httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson, _jsonOptions);

            var message = data?.Choices.FirstOrDefault()?.Message;
            if (message is null) return null;

            return new ModelResponse
            {
                Content = message.Content,
                ReasoningDetails = message.ReasoningDetails,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying configured model {Model}", configuredModel.DisplayName);
            return null;
        }
    }

    /// <summary>
    /// Query multiple configured models in parallel.
    /// </summary>
    public async Task<List<(RuntimeCouncilModel Model, ModelResponse? Response)>> QueryConfiguredModelsParallelAsync(
        IEnumerable<RuntimeCouncilModel> models,
        IEnumerable<OpenRouterMessage> messages)
    {
        var modelList = models.ToList();
        var msgList = messages.ToList();

        var tasks = modelList.Select(m => QueryConfiguredModelAsync(m, msgList));
        var results = await Task.WhenAll(tasks);

        return modelList.Zip(results, (model, response) => (model, response)).ToList();
    }

    /// <summary>
    /// Backward-compatible query path for legacy env-based configuration.
    /// </summary>
    public async Task<ModelResponse?> QueryModelAsync(
        string model,
        IEnumerable<OpenRouterMessage> messages,
        TimeSpan? timeout = null)
    {
        var configured = new RuntimeCouncilModel
        {
            Key = model,
            ModelId = model,
            DisplayName = model,
            ChatCompletionsUrl = GetChatCompletionsUrl(),
            ApiKey = GetApiKey(),
            EndpointId = "legacy",
            EndpointName = "Legacy",
        };

        return await QueryConfiguredModelAsync(configured, messages, timeout);
    }

    /// <summary>
    /// Backward-compatible path for legacy env-based configuration.
    /// </summary>
    public async Task<Dictionary<string, ModelResponse?>> QueryModelsParallelAsync(
        IEnumerable<string> models,
        IEnumerable<OpenRouterMessage> messages)
    {
        var modelList = models.ToList();
        var msgList = messages.ToList();

        var tasks = modelList.Select(m => QueryModelAsync(m, msgList));
        var results = await Task.WhenAll(tasks);

        return modelList.Zip(results)
                        .ToDictionary(pair => pair.First, pair => pair.Second);
    }

    private string GetChatCompletionsUrl()
    {
        return IsLiteLlmProvider()
            ? _options.LiteLlmApiUrl
            : _options.OpenRouterApiUrl;
    }

    private string GetApiKey()
    {
        return IsLiteLlmProvider()
            ? _options.LiteLlmApiKey
            : _options.OpenRouterApiKey;
    }

    private bool IsLiteLlmProvider()
    {
        return string.Equals(_options.LlmProvider, "LiteLLM", StringComparison.OrdinalIgnoreCase);
    }
}
