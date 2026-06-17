using System.Text;
using System.Text.Json;
using LlmCouncil.Models;
using LlmCouncil.Services;
using Microsoft.AspNetCore.Mvc;

namespace LlmCouncil.Controllers;

[ApiController]
[Route("api/conversations")]
public class ConversationsController(
    StorageService storage,
    CouncilService council,
    CouncilConfigurationService configurationService,
    ILogger<ConversationsController> logger) : ControllerBase
{
    // ── GET /api/conversations ────────────────────────────────────────────────

    [HttpGet]
    public ActionResult<List<ConversationMetadata>> ListConversations()
        => storage.ListConversations();

    // ── POST /api/conversations ───────────────────────────────────────────────

    [HttpPost]
    public ActionResult<Conversation> CreateConversation()
    {
        var id = Guid.NewGuid().ToString();
        var conversation = storage.CreateConversation(id);
        return conversation;
    }

    // ── GET /api/conversations/{id} ───────────────────────────────────────────

    [HttpGet("{conversationId}")]
    public ActionResult<Conversation> GetConversation(string conversationId)
    {
        var conversation = storage.GetConversation(conversationId);
        if (conversation is null) return NotFound(new { detail = "Conversation not found" });
        return conversation;
    }

    // ── POST /api/conversations/{id}/message ─────────────────────────────────

    [HttpPost("{conversationId}/message")]
    public async Task<ActionResult<MessageResponse>> SendMessage(
        string conversationId,
        [FromBody] SendMessageRequest request)
    {
        var conversation = storage.GetConversation(conversationId);
        if (conversation is null) return NotFound(new { detail = "Conversation not found" });

        var isFirstMessage = conversation.Messages.Count == 0;
        storage.AddUserMessage(conversationId, request.Content);

        if (isFirstMessage)
        {
            var title = await council.GenerateConversationTitleAsync(request.Content);
            storage.UpdateConversationTitle(conversationId, title);
        }

        var (stage1, stage2, stage3, metadata) = await council.RunFullCouncilAsync(request.Content);
        storage.AddAssistantMessage(conversationId, stage1, stage2, stage3);

        return new MessageResponse
        {
            Stage1 = request.FinalOnly ? [] : stage1,
            Stage2 = request.FinalOnly ? [] : stage2,
            Stage3 = stage3,
            Metadata = request.FinalOnly ? null : metadata,
        };
    }

    // ── POST /api/conversations/{id}/message/stream ───────────────────────────

    [HttpPost("{conversationId}/message/stream")]
    public async Task SendMessageStream(
        string conversationId,
        [FromBody] SendMessageRequest request)
    {
        var conversation = storage.GetConversation(conversationId);
        if (conversation is null)
        {
            Response.StatusCode = 404;
            return;
        }

        var isFirstMessage = conversation.Messages.Count == 0;
        var runtimeConfiguration = configurationService.GetRuntimeConfiguration();

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        async Task SendEventAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var line = $"data: {json}\n\n";
            await Response.WriteAsync(line);
            await Response.Body.FlushAsync();
        }

        try
        {
            storage.AddUserMessage(conversationId, request.Content);

            // Launch title generation in parallel for the first message
            Task<string>? titleTask = isFirstMessage
                ? council.GenerateConversationTitleAsync(request.Content)
                : null;

            // Stage 1
            await SendEventAsync(new { type = "stage1_start" });
            var stage1Results = await council.Stage1CollectResponsesAsync(request.Content, runtimeConfiguration);
            if (!request.FinalOnly)
                await SendEventAsync(new { type = "stage1_complete", data = stage1Results });

            // Stage 2
            await SendEventAsync(new { type = "stage2_start" });
            var (stage2Results, labelToModel) = await council.Stage2CollectRankingsAsync(request.Content, stage1Results, runtimeConfiguration);
            var aggregateRankings = CouncilService.CalculateAggregateRankings(stage2Results, labelToModel);
            if (!request.FinalOnly)
                await SendEventAsync(new
                {
                    type = "stage2_complete",
                    data = stage2Results,
                    metadata = new
                    {
                        labelToModel,
                        aggregateRankings,
                    },
                });

            // Stage 3
            await SendEventAsync(new { type = "stage3_start" });
            var stage3Result = await council.Stage3SynthesizeFinalAsync(request.Content, stage1Results, stage2Results, runtimeConfiguration);
            await SendEventAsync(new { type = "stage3_complete", data = stage3Result });

            // Title
            if (titleTask is not null)
            {
                var title = await titleTask;
                storage.UpdateConversationTitle(conversationId, title);
                await SendEventAsync(new { type = "title_complete", data = new { title } });
            }

            storage.AddAssistantMessage(conversationId, stage1Results, stage2Results, stage3Result);
            await SendEventAsync(new { type = "complete" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in streaming message for conversation {ConversationId}",
                conversationId.Replace('\n', '_').Replace('\r', '_'));
            await SendEventAsync(new { type = "error", message = ex.Message });
        }
    }
}
