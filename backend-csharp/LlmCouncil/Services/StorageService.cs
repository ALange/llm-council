using System.Text.Json;
using LlmCouncil.Models;
using Microsoft.Extensions.Options;

namespace LlmCouncil.Services;

/// <summary>
/// JSON-file-backed conversation storage, mirroring backend/storage.py.
/// </summary>
public class StorageService(IOptions<CouncilOptions> options, ILogger<StorageService> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _dataDir = options.Value.DataDir;

    private void EnsureDataDir() => Directory.CreateDirectory(_dataDir);

    private string ConversationPath(string id) => Path.Combine(_dataDir, $"{id}.json");

    public Conversation CreateConversation(string id)
    {
        EnsureDataDir();
        var conversation = new Conversation
        {
            Id = id,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            Title = "New Conversation",
            Messages = [],
        };
        SaveConversation(conversation);
        return conversation;
    }

    public Conversation? GetConversation(string id)
    {
        var path = ConversationPath(id);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Conversation>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read conversation {Id}", id);
            return null;
        }
    }

    public void SaveConversation(Conversation conversation)
    {
        EnsureDataDir();
        var path = ConversationPath(conversation.Id);
        var json = JsonSerializer.Serialize(conversation, _jsonOptions);
        File.WriteAllText(path, json);
    }

    public List<ConversationMetadata> ListConversations()
    {
        EnsureDataDir();
        var result = new List<ConversationMetadata>();

        foreach (var file in Directory.GetFiles(_dataDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<Conversation>(json, _jsonOptions);
                if (data is null) continue;

                result.Add(new ConversationMetadata
                {
                    Id = data.Id,
                    CreatedAt = data.CreatedAt,
                    Title = data.Title,
                    MessageCount = data.Messages.Count,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping unreadable conversation file {File}", file);
            }
        }

        result.Sort((a, b) => string.Compare(b.CreatedAt, a.CreatedAt, StringComparison.Ordinal));
        return result;
    }

    public void AddUserMessage(string conversationId, string content)
    {
        var conversation = GetConversation(conversationId)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found");

        conversation.Messages.Add(new Dictionary<string, object>
        {
            ["role"] = "user",
            ["content"] = content,
        });
        SaveConversation(conversation);
    }

    public void AddAssistantMessage(
        string conversationId,
        object stage1,
        object stage2,
        object stage3)
    {
        var conversation = GetConversation(conversationId)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found");

        conversation.Messages.Add(new Dictionary<string, object>
        {
            ["role"] = "assistant",
            ["stage1"] = stage1,
            ["stage2"] = stage2,
            ["stage3"] = stage3,
        });
        SaveConversation(conversation);
    }

    public void UpdateConversationTitle(string conversationId, string title)
    {
        var conversation = GetConversation(conversationId)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found");

        conversation.Title = title;
        SaveConversation(conversation);
    }
}
