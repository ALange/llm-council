using System.Text.Json;
using LlmCouncil.Models;
using Microsoft.Extensions.Options;

namespace LlmCouncil.Services;

public class CouncilConfigurationService(
    IOptions<CouncilOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<CouncilConfigurationService> logger)
{
    private static readonly JsonSerializerOptions _fileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly CouncilOptions _options = options.Value;

    public async Task<CouncilConfigurationResponse> GetConfigurationAsync()
    {
        var config = LoadOrCreateConfig();
        var discoveredModels = await DiscoverModelsAsync(config.Endpoints);

        return new CouncilConfigurationResponse
        {
            Config = config,
            DiscoveredModels = discoveredModels,
        };
    }

    public async Task<CouncilConfigurationResponse> SaveConfigurationAsync(CouncilPortalConfig config)
    {
        var sanitized = Sanitize(config);
        SaveConfig(sanitized);
        return await GetConfigurationAsync();
    }

    public RuntimeCouncilConfiguration GetRuntimeConfiguration()
    {
        var config = LoadOrCreateConfig();
        var endpointMap = config.Endpoints
            .Where(e => e.Enabled)
            .ToDictionary(e => e.Id, e => e);

        var councilModels = config.CouncilModelKeys
            .Select(k => ResolveRuntimeModel(k, endpointMap))
            .Where(m => m is not null)
            .Cast<RuntimeCouncilModel>()
            .ToList();

        RuntimeCouncilModel? chairman = null;
        if (!string.IsNullOrWhiteSpace(config.ChairmanModelKey))
        {
            chairman = ResolveRuntimeModel(config.ChairmanModelKey, endpointMap);
        }

        chairman ??= councilModels.FirstOrDefault();

        return new RuntimeCouncilConfiguration
        {
            CouncilModels = councilModels,
            ChairmanModel = chairman,
        };
    }

    private CouncilPortalConfig LoadOrCreateConfig()
    {
        var path = GetConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<CouncilPortalConfig>(json, _fileJsonOptions);
                if (loaded is not null)
                    return Sanitize(loaded);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read council config, rebuilding defaults");
            }
        }

        var defaults = BuildDefaultConfig();
        SaveConfig(defaults);
        return defaults;
    }

    private CouncilPortalConfig BuildDefaultConfig()
    {
        var endpointId = "default-openrouter";
        var defaultModelsUrl = ToModelsUrl(_options.OpenRouterApiUrl);

        var config = new CouncilPortalConfig
        {
            Endpoints =
            [
                new EndpointConfiguration
                {
                    Id = endpointId,
                    Name = "OpenRouter",
                    ModelsUrl = defaultModelsUrl,
                    ApiKey = _options.OpenRouterApiKey,
                    Enabled = true,
                },
            ],
            CouncilModelKeys = _options.CouncilModels
                .Select(modelId => BuildModelKey(endpointId, modelId))
                .ToList(),
            ChairmanModelKey = string.IsNullOrWhiteSpace(_options.ChairmanModel)
                ? string.Empty
                : BuildModelKey(endpointId, _options.ChairmanModel),
        };

        return Sanitize(config);
    }

    private void SaveConfig(CouncilPortalConfig config)
    {
        var path = GetConfigPath();
        var json = JsonSerializer.Serialize(config, _fileJsonOptions);
        File.WriteAllText(path, json);
    }

    private string GetConfigPath() => _options.CouncilConfigPath;

    private static CouncilPortalConfig Sanitize(CouncilPortalConfig config)
    {
        var endpoints = config.Endpoints
            .Select(e => new EndpointConfiguration
            {
                Id = string.IsNullOrWhiteSpace(e.Id) ? Guid.NewGuid().ToString("N") : e.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(e.Name) ? "Endpoint" : e.Name.Trim(),
                ModelsUrl = e.ModelsUrl.Trim(),
                ApiKey = e.ApiKey.Trim(),
                Enabled = e.Enabled,
            })
            .Where(e => !string.IsNullOrWhiteSpace(e.ModelsUrl))
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .ToList();

        var keys = config.CouncilModelKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new CouncilPortalConfig
        {
            Endpoints = endpoints,
            CouncilModelKeys = keys,
            ChairmanModelKey = config.ChairmanModelKey?.Trim() ?? string.Empty,
        };
    }

    public async Task<List<DiscoveredModel>> DiscoverModelsAsync(IEnumerable<EndpointConfiguration> endpoints)
    {
        var tasks = endpoints
            .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ModelsUrl))
            .Select(DiscoverModelsFromEndpointAsync);

        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .GroupBy(m => m.Key)
            .Select(g => g.First())
            .OrderBy(m => m.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<DiscoveredModel>> DiscoverModelsFromEndpointAsync(EndpointConfiguration endpoint)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.ModelsUrl);
            if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return ParseModels(endpoint, content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover models from endpoint {EndpointId}", endpoint.Id);
            return [];
        }
    }

    private static List<DiscoveredModel> ParseModels(EndpointConfiguration endpoint, string payload)
    {
        using var doc = JsonDocument.Parse(payload);

        JsonElement modelArray;
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Array)
        {
            modelArray = dataElement;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            modelArray = doc.RootElement;
        }
        else
        {
            return [];
        }

        var models = new List<DiscoveredModel>();
        foreach (var item in modelArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            if (!item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                continue;

            var modelId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(modelId))
                continue;

            var displayName = modelId;
            if (item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                var name = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    displayName = name;
            }

            models.Add(new DiscoveredModel
            {
                Key = BuildModelKey(endpoint.Id, modelId),
                ModelId = modelId,
                DisplayName = displayName,
                EndpointId = endpoint.Id,
                EndpointName = endpoint.Name,
            });
        }

        return models;
    }

    private static RuntimeCouncilModel? ResolveRuntimeModel(
        string key,
        Dictionary<string, EndpointConfiguration> endpointMap)
    {
        if (!TryParseModelKey(key, out var endpointId, out var modelId))
            return null;

        if (!endpointMap.TryGetValue(endpointId, out var endpoint))
            return null;

        return new RuntimeCouncilModel
        {
            Key = key,
            ModelId = modelId,
            DisplayName = $"{modelId} ({endpoint.Name})",
            EndpointId = endpoint.Id,
            EndpointName = endpoint.Name,
            ChatCompletionsUrl = ToChatCompletionsUrl(endpoint.ModelsUrl),
            ApiKey = endpoint.ApiKey,
        };
    }

    public static string BuildModelKey(string endpointId, string modelId)
        => $"{endpointId}::{modelId}";

    public static bool TryParseModelKey(string key, out string endpointId, out string modelId)
    {
        endpointId = string.Empty;
        modelId = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        var idx = key.IndexOf("::", StringComparison.Ordinal);
        if (idx <= 0 || idx >= key.Length - 2)
            return false;

        endpointId = key[..idx];
        modelId = key[(idx + 2)..];
        return !string.IsNullOrWhiteSpace(endpointId) && !string.IsNullOrWhiteSpace(modelId);
    }

    public static string ToChatCompletionsUrl(string modelsUrl)
    {
        if (string.IsNullOrWhiteSpace(modelsUrl)) return modelsUrl;

        var trimmed = modelsUrl.TrimEnd('/');
        if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/models".Length] + "/chat/completions";

        return trimmed + "/chat/completions";
    }

    private static string ToModelsUrl(string chatCompletionsUrl)
    {
        if (string.IsNullOrWhiteSpace(chatCompletionsUrl)) return chatCompletionsUrl;

        var trimmed = chatCompletionsUrl.TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/chat/completions".Length] + "/models";

        return trimmed + "/models";
    }
}
