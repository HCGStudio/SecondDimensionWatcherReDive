using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat;

internal sealed record ApprovalRequiredPayload(
    bool ApprovalRequired,
    Guid ActionId,
    Guid ConversationId,
    string ToolCallId,
    string ToolName,
    ToolRiskLevel RiskLevel,
    string ParameterHash,
    string ParameterSummary,
    string ImpactSummary,
    bool IsReversible,
    DateTimeOffset ExpiresAt);

internal sealed record ApprovalRequiredToolResult(ApprovalRequiredPayload Result) : IToolResult
{
    object? IToolResult.Result => Result;
    public bool IsSuccess => true;
}

internal sealed record ChatActionDetails(
    Guid Id,
    Guid ConversationId,
    string ToolCallId,
    string ToolName,
    ToolRiskLevel RiskLevel,
    ChatActionState State,
    string ParameterHash,
    string ParameterSummary,
    string ImpactSummary,
    bool IsReversible,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? CompletedAt,
    string? ResultSummary,
    string? ErrorSummary,
    string? ToolResultJson,
    string? ApprovalToken);

internal sealed record ChatActionDecisionResult(
    ChatActionClaimOutcome Outcome,
    ChatActionDetails? Action = null,
    JsonElement? ToolResult = null);

internal interface IChatActionService
{
    Task<IToolResult> CreatePendingAsync(
        Guid conversationId,
        Guid userId,
        ToolCall toolCall,
        ChatToolActionPlan plan,
        CancellationToken cancellationToken);

    Task<ChatActionDetails?> GetAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatActionDetails>> GetForConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<ChatActionDecisionResult> ApproveAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalToken,
        string parameterHash,
        bool destructiveConfirmed,
        CancellationToken cancellationToken);

    Task<ChatActionRejectOutcome> RejectAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalToken,
        string parameterHash,
        CancellationToken cancellationToken);
}

internal sealed class ChatActionService : IChatActionService
{
    private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExecutionAbandonmentAge = ExecutionTimeout + TimeSpan.FromMinutes(1);
    private const string AbandonedExecutionSummary =
        "Execution owner stopped before recording completion; the side-effect outcome is unknown.";
    private static readonly string AbandonedToolResultJson = JsonSerializer.Serialize(
        new ToolFailureResult(
            "Approved tool execution was interrupted. Verify the current system state before retrying."),
        ToolJsonOptions.Options);
    private readonly IChatActionRepository _repository;
    private readonly IChatRawToolExecutorFactory _toolExecutorFactory;
    private readonly IDataProtector _parameterProtector;
    private readonly IDataProtector _tokenProtector;

    public ChatActionService(
        IChatActionRepository repository,
        IChatRawToolExecutorFactory toolExecutorFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _repository = repository;
        _toolExecutorFactory = toolExecutorFactory;
        _parameterProtector = dataProtectionProvider.CreateProtector(
            "SecondDimensionWatcherReDive.Chat.PendingAction.Parameters.v1");
        _tokenProtector = dataProtectionProvider.CreateProtector(
            "SecondDimensionWatcherReDive.Chat.PendingAction.ApprovalToken.v1");
    }

    public async Task<IToolResult> CreatePendingAsync(
        Guid conversationId,
        Guid userId,
        ToolCall toolCall,
        ChatToolActionPlan plan,
        CancellationToken cancellationToken)
    {
        string canonicalParameters;
        try
        {
            canonicalParameters = CanonicalizeParameters(toolCall.Arguments);
        }
        catch (JsonException)
        {
            return new ToolFailureResult(
                $"Tool '{toolCall.Name}' supplied malformed or ambiguous JSON arguments.");
        }

        var now = DateTimeOffset.UtcNow;
        var actionId = Guid.NewGuid();
        var approvalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var parameterHash = Hash(canonicalParameters);
        var tokenHash = Hash(approvalToken);
        var expiresAt = now.Add(ApprovalLifetime);
        var draft = new PendingChatActionDraft(
            actionId,
            conversationId,
            userId,
            toolCall.Id,
            toolCall.Name,
            plan.RiskLevel,
            _parameterProtector.Protect(canonicalParameters),
            parameterHash,
            _tokenProtector.Protect(approvalToken),
            tokenHash,
            Limit(plan.ParameterSummary, 1024),
            Limit(plan.ImpactSummary, 2048),
            plan.IsReversible,
            now,
            expiresAt);
        await _repository.AddAsync(draft, cancellationToken);

        return new ApprovalRequiredToolResult(new ApprovalRequiredPayload(
            true,
            actionId,
            conversationId,
            toolCall.Id,
            toolCall.Name,
            plan.RiskLevel,
            parameterHash,
            draft.ParameterSummary,
            draft.ImpactSummary,
            draft.IsReversible,
            expiresAt));
    }

    public async Task<ChatActionDetails?> GetAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await RecoverAbandonedExecutionsAsync(conversationId, userId, cancellationToken);
        var action = await _repository.FindAsync(
            actionId, conversationId, userId, cancellationToken);
        return action is null ? null : ToDetails(action);
    }

    public async Task<IReadOnlyList<ChatActionDetails>> GetForConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await RecoverAbandonedExecutionsAsync(conversationId, userId, cancellationToken);
        var actions = await _repository.GetForConversationAsync(
            conversationId, userId, cancellationToken);
        return actions.Select(ToDetails).ToList();
    }

    public async Task<ChatActionDecisionResult> ApproveAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalToken,
        string parameterHash,
        bool destructiveConfirmed,
        CancellationToken cancellationToken)
    {
        await RecoverAbandonedExecutionsAsync(conversationId, userId, cancellationToken);
        var claim = await _repository.TryClaimForExecutionAsync(
            actionId,
            conversationId,
            userId,
            Hash(approvalToken),
            parameterHash,
            destructiveConfirmed,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (claim.Outcome != ChatActionClaimOutcome.Claimed || claim.Action is null)
            return new(claim.Outcome, claim.Action is null ? null : ToDetails(claim.Action));

        // Once claimed, execution is deliberately detached from RequestAborted. A network
        // disconnect cannot turn a retry into a second side effect; the one-time database state
        // remains the authority and the bounded execution is audited to completion.
        IToolResult toolResult;
        try
        {
            var parameters = _parameterProtector.Unprotect(claim.Action.ProtectedParameters);
            if (!FixedTimeEquals(Hash(parameters), claim.Action.ParameterHash))
                throw new CryptographicException("The protected action parameters failed integrity validation.");

            using var timeout = new CancellationTokenSource(ExecutionTimeout);
            toolResult = await _toolExecutorFactory.Create().ExecuteAsync(
                new ToolCall(claim.Action.ToolCallId, claim.Action.ToolName, parameters),
                timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failedResult = JsonSerializer.SerializeToElement(
                new ToolFailureResult("Approved tool execution failed."),
                ToolJsonOptions.Options);
            await _repository.CompleteExecutionAsync(
                actionId,
                false,
                failedResult.GetRawText(),
                null,
                $"Execution raised {exception.GetType().Name}.",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            var failedAction = await _repository.FindAsync(
                actionId, conversationId, userId, CancellationToken.None);
            return new(
                ChatActionClaimOutcome.Claimed,
                failedAction is null ? null : ToDetails(failedAction),
                failedResult);
        }
        catch (OperationCanceledException)
        {
            var timedOutResult = JsonSerializer.SerializeToElement(
                new ToolFailureResult("Approved tool execution timed out."),
                ToolJsonOptions.Options);
            await _repository.CompleteExecutionAsync(
                actionId,
                false,
                timedOutResult.GetRawText(),
                null,
                "Execution exceeded its bounded timeout.",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            var failedAction = await _repository.FindAsync(
                actionId, conversationId, userId, CancellationToken.None);
            return new(
                ChatActionClaimOutcome.Claimed,
                failedAction is null ? null : ToDetails(failedAction),
                timedOutResult);
        }

        var succeeded = toolResult.IsSuccess;
        var serializedResult = JsonSerializer.SerializeToElement(
            toolResult, toolResult.GetType(), ToolJsonOptions.Options);
        var completed = await _repository.CompleteExecutionAsync(
            actionId,
            succeeded,
            serializedResult.GetRawText(),
            succeeded ? "Approved tool execution succeeded." : null,
            succeeded ? null : "Approved tool returned a failure.",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var completedAction = await _repository.FindAsync(
            actionId, conversationId, userId, CancellationToken.None);
        return new(
            ChatActionClaimOutcome.Claimed,
            completedAction is null ? null : ToDetails(completedAction),
            completed
                ? serializedResult
                : ParseToolResult(completedAction?.ToolResultJson) ?? serializedResult);
    }

    public async Task<ChatActionRejectOutcome> RejectAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalToken,
        string parameterHash,
        CancellationToken cancellationToken)
    {
        await RecoverAbandonedExecutionsAsync(conversationId, userId, cancellationToken);
        return await _repository.TryRejectAsync(
            actionId,
            conversationId,
            userId,
            Hash(approvalToken),
            parameterHash,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    internal static string CanonicalizeParameters(string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    writer.WriteStartObject();
                    var properties = element.EnumerateObject()
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .ToList();
                    for (var index = 1; index < properties.Count; index++)
                    {
                        if (string.Equals(properties[index - 1].Name, properties[index].Name,
                                StringComparison.Ordinal))
                            throw new JsonException("Duplicate JSON property names are not allowed.");
                    }
                    foreach (var property in properties)
                    {
                        writer.WritePropertyName(property.Name);
                        WriteCanonical(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                }
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private ChatActionDetails ToDetails(PendingChatAction action)
    {
        string? token = null;
        if (action.State == ChatActionState.Pending && action.ExpiresAt > DateTimeOffset.UtcNow)
        {
            try
            {
                token = _tokenProtector.Unprotect(action.ProtectedApprovalToken);
            }
            catch (CryptographicException)
            {
                // The action remains non-executable without a valid token. Do not invent or rotate
                // a token because that would weaken replay guarantees.
            }
        }

        return new(
            action.Id,
            action.ConversationId,
            action.ToolCallId,
            action.ToolName,
            action.RiskLevel,
            action.State,
            action.ParameterHash,
            action.ParameterSummary,
            action.ImpactSummary,
            action.IsReversible,
            action.CreatedAt,
            action.ExpiresAt,
            action.DecidedAt,
            action.CompletedAt,
            action.ResultSummary,
            action.ErrorSummary,
            action.ToolResultJson,
            token);
    }

    private async Task RecoverAbandonedExecutionsAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.RecoverAbandonedExecutionsAsync(
            conversationId,
            userId,
            now.Subtract(ExecutionAbandonmentAge),
            AbandonedToolResultJson,
            AbandonedExecutionSummary,
            now,
            cancellationToken);
    }

    private static JsonElement? ParseToolResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
