using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.External;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat;

[ApiController]
[Route("api/chat")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed partial class ChatController(
    IChatRepository chatRepository,
    IServiceScopeFactory scopeFactory,
    IServiceProvider serviceProvider,
    ILogger<ChatController> logger) : ControllerBase
{

    [HttpGet("status")]
    public ChatStatusResponse GetStatus()
    {
        var aiEngine = serviceProvider.GetService<IAIEngine>();
        var status = serviceProvider.GetService<IAIEngineStatus>();
        var provider = status?.Name
                       ?? serviceProvider.GetService<IConfiguration>()?["AI:Provider"];
        var enabled = status?.IsConfigured ?? aiEngine is not null;
        LogStatusCheck(provider, enabled);
        return new ChatStatusResponse(enabled, provider);
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var aiEngine = serviceProvider.GetService<IAIEngine>();
        var status = serviceProvider.GetService<IAIEngineStatus>();
        if (aiEngine is null || status is { IsConfigured: false })
            return StatusCode(503);

        try
        {
            var models = await aiEngine.GetAvailableModelsAsync(cancellationToken);
            LogModelsFetched(models.Count);
            return Ok(models);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogModelsFetchFailed(ex);
            return Ok(Array.Empty<AIModel>());
        }
    }

    [HttpGet("conversations")]
    public async Task<IReadOnlyList<ChatConversationSummary>> GetConversations(
        CancellationToken cancellationToken)
    {
        return await chatRepository.GetConversationsAsync(cancellationToken);
    }

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> GetConversation(Guid id, CancellationToken cancellationToken)
    {
        var detail = await chatRepository.GetConversationWithMessagesAsync(id, cancellationToken);
        if (detail is null)
        {
            LogConversationNotFound(id);
            return NotFound();
        }
        return Ok(detail);
    }

    [HttpPost("conversations")]
    public async Task<ChatConversationSummary> CreateConversation(
        [FromBody] CreateConversationRequest? request,
        CancellationToken cancellationToken)
    {
        var conv = await chatRepository.CreateConversationAsync(request?.Title, cancellationToken);
        LogConversationCreated(conv.Id, request?.Title);
        return conv;
    }

    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await chatRepository.DeleteConversationAsync(id, cancellationToken);
        if (deleted)
            LogConversationDeleted(id);
        else
            LogConversationNotFound(id);
        return deleted ? Ok() : NotFound();
    }

    [HttpPatch("conversations/{id:guid}")]
    public async Task<IActionResult> UpdateConversationTitle(
        Guid id,
        [FromBody] UpdateConversationRequest request,
        CancellationToken cancellationToken)
    {
        await chatRepository.UpdateConversationTitleAsync(id, request.Title, cancellationToken);
        return Ok();
    }

    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<IResult> SendMessage(
        Guid id,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var aiEngine = serviceProvider.GetService<IAIEngine>();
        var status = serviceProvider.GetService<IAIEngineStatus>();
        if (aiEngine is null || status is { IsConfigured: false })
            return TypedResults.StatusCode(503);

        var conversation = await chatRepository.GetConversationWithMessagesAsync(id, cancellationToken);
        if (conversation is null)
        {
            LogConversationNotFound(id);
            return TypedResults.NotFound();
        }

        // Get current message count for ordering
        var messageOrder = await chatRepository.GetMessageCountAsync(id, cancellationToken);

        // Save user message
        var userMessage = new ChatMessageRecord(
            Guid.NewGuid(), "user", request.Content, null, null, null,
            messageOrder, DateTimeOffset.Now);
        await chatRepository.AddMessageAsync(id, userMessage, cancellationToken);
        messageOrder++;

        LogUserMessageReceived(id, messageOrder - 1, request.Content.Length);

        // Snapshot whether this conversation already had any assistant message before this turn.
        // Used after the first assistant message is saved to trigger one-time auto title generation.
        var hadPriorAssistant = conversation.Messages.Any(m => m.Role == "assistant");
        var titleEligible = ConversationTitleGenerator.IsAutoTitleEligible(conversation.Title);

        // Build message history for AI
        var messages = BuildMessagesFromHistory(conversation.Messages, request.Content);
        LogHistoryBuilt(id, messages.Count);

        var toolExecutor = new ToolExecutorBuilder(serviceProvider)
            .AddTool<QueryAnimationsTool>()
            .AddTool<ManageFeedsTool>()
            .AddTool<QuerySeasonTool>()
            .AddTool<SubscribeBangumiTool>()
            .AddTool<ManageTasksTool>()
            .AddTool<ManageDownloadsTool>()
            .AddTool<QueryFilesTool>()
            .Build();

        var chatOptions = new ChatOptions
        {
            ToolExecutor = toolExecutor,
            MaxToolRounds = 8,
            Model = request.Model
        };

        LogStreamingStarted(id, request.Model);

        return TypedResults.ServerSentEvents(
            StreamChatEvents(aiEngine, messages, chatOptions, id, messageOrder,
                request.Content, !hadPriorAssistant && titleEligible, request.Model,
                cancellationToken));
    }

    private async IAsyncEnumerable<SseItem<string>> StreamChatEvents(
        IAIEngine aiEngine,
        List<IMessage> messages,
        ChatOptions chatOptions,
        Guid conversationId,
        int messageOrder,
        string firstUserMessage,
        bool autoTitleEligible,
        string? model,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SseItem<string>>();

        // Producer: runs AI chat streaming in background, writes SSE items to channel.
        // Keep the task and await it during iterator disposal so a disconnected request cannot
        // release this controller's scoped repository before tool-call audit records are saved.
        var producer = ProduceChatEventsAsync(
            aiEngine, messages, chatOptions, conversationId, messageOrder,
            firstUserMessage, autoTitleEligible, model,
            channel.Writer, cancellationToken);

        try
        {
            // Consumer: yield items from channel as SSE events
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            // Do not use the canceled request token here. The producer receives it directly,
            // performs bounded engine cleanup, and persists accumulated messages before returning.
            await producer;
        }
    }

    private async Task ProduceChatEventsAsync(
        IAIEngine aiEngine,
        List<IMessage> messages,
        ChatOptions chatOptions,
        Guid conversationId,
        int messageOrder,
        string firstUserMessage,
        bool autoTitleEligible,
        string? model,
        ChannelWriter<SseItem<string>> writer,
        CancellationToken cancellationToken)
    {
        var currentText = new StringBuilder();
        var currentToolCalls = new Dictionary<string, (string Name, StringBuilder Args)>();
        var currentToolResults = new List<ChatMessageRecord>();
        var messagesToSave = new List<ChatMessageRecord>();
        var hasToolResults = false;
        string? firstAssistantContentForTitle = null;

        try
        {
            await foreach (var update in aiEngine.ChatAsync(messages, chatOptions, cancellationToken))
            {
                switch (update)
                {
                    case TextDelta textDelta:
                        // Text after tool results means a new segment — flush the previous one
                        if (hasToolResults)
                        {
                            LogSegmentFlushed(conversationId, currentToolCalls.Count, currentToolResults.Count);
                            FlushSegment(messagesToSave, currentText, currentToolCalls,
                                currentToolResults, ref messageOrder);
                            hasToolResults = false;
                        }
                        currentText.Append(textDelta.Text);
                        await writer.WriteAsync(
                            new SseItem<string>(
                                JsonSerializer.Serialize(new SseTextDelta(textDelta.Text),
                                    ChatJsonSerializerContext.Default.SseTextDelta),
                                "text_delta"),
                            cancellationToken);
                        break;

                    case ToolCallBegin toolCallBegin:
                        // A new call after results belongs to the next model round. Persist the prior
                        // call/result segment first so sequential dependencies are not flattened into
                        // one apparent parallel batch in conversation history.
                        if (hasToolResults)
                        {
                            LogSegmentFlushed(conversationId, currentToolCalls.Count, currentToolResults.Count);
                            FlushSegment(messagesToSave, currentText, currentToolCalls,
                                currentToolResults, ref messageOrder);
                            hasToolResults = false;
                        }

                        LogToolCallBegin(conversationId, toolCallBegin.Name, toolCallBegin.Id);
                        currentToolCalls[toolCallBegin.Id] = (toolCallBegin.Name, new StringBuilder());
                        await WriteToolAuditEventAsync(writer,
                            new SseItem<string>(
                                JsonSerializer.Serialize(new SseToolCallBegin(toolCallBegin.Id, toolCallBegin.Name),
                                    ChatJsonSerializerContext.Default.SseToolCallBegin),
                                "tool_call_begin"),
                            cancellationToken);
                        break;

                    case ToolCallDelta toolCallDelta:
                        if (currentToolCalls.TryGetValue(toolCallDelta.Id, out var builder))
                            builder.Args.Append(toolCallDelta.ArgumentsDelta);
                        await WriteToolAuditEventAsync(writer,
                            new SseItem<string>(
                                JsonSerializer.Serialize(new SseToolCallDelta(toolCallDelta.Id, toolCallDelta.ArgumentsDelta),
                                    ChatJsonSerializerContext.Default.SseToolCallDelta),
                                "tool_call_delta"),
                            cancellationToken);
                        break;

                    case ToolResultUpdate toolResult:
                        var toolName = currentToolCalls.TryGetValue(toolResult.ToolCallId, out var tcBuilder)
                            ? tcBuilder.Name
                            : null;
                        var resultText = toolResult.Result.GetRawText();
                        LogToolResult(conversationId, toolName, toolResult.ToolCallId, resultText.Length);
                        currentToolResults.Add(new ChatMessageRecord(
                            Guid.NewGuid(), "tool", resultText, null,
                            toolResult.ToolCallId, toolName,
                            0, DateTimeOffset.Now)); // Order assigned during flush
                        hasToolResults = true;
                        await WriteToolAuditEventAsync(writer,
                            new SseItem<string>(
                                JsonSerializer.Serialize(new SseToolResult(toolResult.ToolCallId, toolName ?? "", resultText),
                                    ChatJsonSerializerContext.Default.SseToolResult),
                                "tool_result"),
                            cancellationToken);
                        break;

                    case Finished finished:
                        LogStreamFinished(conversationId, finished.StopReason);
                        await writer.WriteAsync(
                            new SseItem<string>(
                                JsonSerializer.Serialize(new SseFinished(finished.StopReason),
                                    ChatJsonSerializerContext.Default.SseFinished),
                                "finished"),
                            cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogClientDisconnected(conversationId);
        }
        catch (Exception ex)
        {
            LogStreamingError(ex, conversationId);
            await writer.WriteAsync(
                new SseItem<string>(
                    JsonSerializer.Serialize(new SseError(ex.Message),
                        ChatJsonSerializerContext.Default.SseError),
                    "error"),
                CancellationToken.None);
        }

        // Save all accumulated segments to DB
        try
        {
            FlushSegment(messagesToSave, currentText, currentToolCalls,
                currentToolResults, ref messageOrder);

            if (messagesToSave.Count > 0)
            {
                await chatRepository.AddMessagesAsync(conversationId, messagesToSave, CancellationToken.None);
                LogMessagesSaved(conversationId, messagesToSave.Count);

                // Capture data needed for the post-stream auto-title task.
                if (autoTitleEligible)
                {
                    var firstAssistant = messagesToSave
                        .FirstOrDefault(m => m.Role == "assistant" && !string.IsNullOrWhiteSpace(m.Content));
                    if (firstAssistant is not null)
                        firstAssistantContentForTitle = firstAssistant.Content;
                }
            }
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex, conversationId);
        }
        finally
        {
            writer.Complete();
        }

        // Title generation runs detached from the SSE response: the client's sendMessage()
        // resolves as soon as writer.Complete() above closes the stream. The title call has
        // its own DI scope (the request scope is about to dispose) and a hard timeout so a
        // stalled provider can never hang the conversation.
        if (firstAssistantContentForTitle is not null)
        {
            _ = RunAutoTitleAsync(conversationId, firstUserMessage, firstAssistantContentForTitle, model);
        }
    }

    private static async Task WriteToolAuditEventAsync(
        ChannelWriter<SseItem<string>> writer,
        SseItem<string> item,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Continue consuming tool audit updates after the client disconnects. The in-memory
            // call/result records are flushed to the repository when the engine propagates the
            // cancellation; only delivery to the disconnected SSE client is skipped.
        }
    }

    private async Task RunAutoTitleAsync(
        Guid conversationId, string firstUserMessage, string firstAssistantMessage, string? model)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var generator = scope.ServiceProvider.GetRequiredService<IConversationTitleGenerator>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await generator.TryAutoTitleAsync(
                conversationId, firstUserMessage, firstAssistantMessage, model, cts.Token);
        }
        catch (Exception ex)
        {
            LogAutoTitleTaskFailed(ex, conversationId);
        }
    }

    private static void FlushSegment(
        List<ChatMessageRecord> messagesToSave,
        StringBuilder currentText,
        Dictionary<string, (string Name, StringBuilder Args)> currentToolCalls,
        List<ChatMessageRecord> currentToolResults,
        ref int messageOrder)
    {
        if (currentText.Length == 0 && currentToolCalls.Count == 0)
            return;

        string? toolCallsJson = null;
        if (currentToolCalls.Count > 0)
        {
            var toolCalls = currentToolCalls.Select(kv =>
                new ToolCallRecord(kv.Key, kv.Value.Name, kv.Value.Args.ToString()));
            toolCallsJson = JsonSerializer.Serialize(toolCalls,
                ChatJsonSerializerContext.Default.IEnumerableToolCallRecord);
        }

        messagesToSave.Add(new ChatMessageRecord(
            Guid.NewGuid(), "assistant", currentText.ToString(), toolCallsJson,
            null, null, messageOrder++, DateTimeOffset.Now));

        foreach (var toolResult in currentToolResults)
        {
            messagesToSave.Add(toolResult with { Order = messageOrder++ });
        }

        currentText.Clear();
        currentToolCalls.Clear();
        currentToolResults.Clear();
    }

    private static List<IMessage> BuildMessagesFromHistory(
        IReadOnlyList<ChatMessageRecord> history, string newUserMessage)
    {
        var messages = new List<IMessage>
        {
            new SystemMessage(ChatSystemPrompt.Build())
        };

        foreach (var msg in history)
        {
            switch (msg.Role)
            {
                case "user":
                    messages.Add(new UserMessage(msg.Content ?? ""));
                    break;

                case "assistant":
                {
                    IReadOnlyList<ToolCall>? toolCalls = null;
                    if (msg.ToolCallsJson is not null)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(msg.ToolCallsJson);
                            toolCalls = doc.RootElement.EnumerateArray()
                                .Select(tc => new ToolCall(
                                    tc.GetProperty("id").GetString() ?? "",
                                    tc.GetProperty("name").GetString() ?? "",
                                    tc.GetProperty("arguments").GetString() ?? ""))
                                .ToList();
                        }
                        catch
                        {
                            // Skip malformed tool calls
                        }
                    }

                    messages.Add(new AssistantMessage(msg.Content, toolCalls));
                    break;
                }

                case "tool":
                    if (msg.ToolCallId is not null)
                        messages.Add(new ToolResultMessage(msg.ToolCallId, msg.Content ?? ""));
                    break;
            }
        }

        messages.Add(new UserMessage(newUserMessage));
        return messages;
    }

    // --- LoggerMessage definitions ---

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Status check: provider={Provider}, enabled={Enabled}")]
    private partial void LogStatusCheck(string? provider, bool enabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Fetched {Count} available models")]
    private partial void LogModelsFetched(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Chat] Failed to fetch available models")]
    private partial void LogModelsFetchFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Chat] Conversation {ConversationId} not found")]
    private partial void LogConversationNotFound(Guid conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Created conversation {ConversationId}, title={Title}")]
    private partial void LogConversationCreated(Guid conversationId, string? title);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Deleted conversation {ConversationId}")]
    private partial void LogConversationDeleted(Guid conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: received user message (order={Order}, length={Length})")]
    private partial void LogUserMessageReceived(Guid conversationId, int order, int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: built history with {MessageCount} messages")]
    private partial void LogHistoryBuilt(Guid conversationId, int messageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: starting streaming, model={Model}")]
    private partial void LogStreamingStarted(Guid conversationId, string? model);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: segment flushed ({ToolCallCount} tool calls, {ToolResultCount} tool results)")]
    private partial void LogSegmentFlushed(Guid conversationId, int toolCallCount, int toolResultCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: tool call begin {ToolName} (id={ToolCallId})")]
    private partial void LogToolCallBegin(Guid conversationId, string toolName, string toolCallId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: tool result for {ToolName} (id={ToolCallId}, resultLength={ResultLength})")]
    private partial void LogToolResult(Guid conversationId, string? toolName, string toolCallId, int resultLength);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: stream finished, stopReason={StopReason}")]
    private partial void LogStreamFinished(Guid conversationId, string? stopReason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: client disconnected")]
    private partial void LogClientDisconnected(Guid conversationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Chat] Conversation {ConversationId}: error during streaming")]
    private partial void LogStreamingError(Exception ex, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: saved {Count} messages to database")]
    private partial void LogMessagesSaved(Guid conversationId, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Chat] Conversation {ConversationId}: failed to save messages to database")]
    private partial void LogSaveFailed(Exception ex, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Chat] Conversation {ConversationId}: detached auto-title task failed")]
    private partial void LogAutoTitleTaskFailed(Exception ex, Guid conversationId);

}
