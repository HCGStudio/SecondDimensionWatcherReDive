using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Owns PostgreSQL setup and inspection for chat-action repository integration tests without
/// exposing the EF context outside the repository implementation boundary.
/// </summary>
internal sealed class ChatActionRepositoryPostgreSqlTestFixture(string connectionString)
{
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .Options;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS "FailChatActionAudit" ON "ChatActionAudits";
            DROP FUNCTION IF EXISTS fail_chat_action_audit();
            TRUNCATE TABLE "ChatActionAudits", "ChatPendingActions", "ChatMessages", "ChatConversations"
                RESTART IDENTITY CASCADE;
            """,
            cancellationToken);
    }

    public async Task<ChatActionTestSeed> SeedAsync(
        string? initialToolResult,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var now = DateTimeOffset.UtcNow;
        var conversationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var toolCallId = "call-" + Guid.NewGuid().ToString("N");
        var tokenHash = new string('A', 64);
        var parameterHash = new string('B', 64);
        context.ChatConversations.Add(new Models.ChatConversation
        {
            Id = conversationId,
            Title = "Chat action repository integration test",
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);

        var repository = new ChatActionRepository(context);
        await repository.AddAsync(
            new PendingChatActionDraft(
                actionId,
                conversationId,
                userId,
                toolCallId,
                "test_tool",
                ToolRiskLevel.Mutating,
                "protected-parameters",
                parameterHash,
                "protected-token",
                tokenHash,
                "value=1",
                "Change one test value.",
                true,
                now,
                now.AddMinutes(15)),
            cancellationToken);

        if (initialToolResult is not null)
        {
            var chatRepository = new ChatRepository(context);
            await chatRepository.AddMessageAsync(
                conversationId,
                new ChatMessageRecord(
                    Guid.NewGuid(),
                    "tool",
                    initialToolResult,
                    null,
                    toolCallId,
                    "test_tool",
                    0,
                    now),
                cancellationToken);
        }

        return new ChatActionTestSeed(
            actionId, conversationId, userId, toolCallId, tokenHash, parameterHash);
    }

    public async Task<ChatActionClaimResult> ClaimAsync(
        ChatActionTestSeed seed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatActionRepository(context);
        return await repository.TryClaimForExecutionAsync(
            seed.ActionId,
            seed.ConversationId,
            seed.UserId,
            seed.ApprovalTokenHash,
            seed.ParameterHash,
            false,
            now,
            cancellationToken);
    }

    public async Task<bool> CompleteAsync(
        ChatActionTestSeed seed,
        bool succeeded,
        string toolResultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatActionRepository(context);
        return await repository.CompleteExecutionAsync(
            seed.ActionId,
            succeeded,
            toolResultJson,
            succeeded ? "Approved tool execution succeeded." : null,
            succeeded ? null : "Approved tool returned a failure.",
            completedAt,
            cancellationToken);
    }

    public async Task<int> RecoverAsync(
        ChatActionTestSeed seed,
        DateTimeOffset executionStartedBefore,
        string toolResultJson,
        string errorSummary,
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatActionRepository(context);
        return await repository.RecoverAbandonedExecutionsAsync(
            seed.ConversationId,
            seed.UserId,
            executionStartedBefore,
            toolResultJson,
            errorSummary,
            recoveredAt,
            cancellationToken);
    }

    public async Task AddToolMessageAsync(
        ChatActionTestSeed seed,
        string content,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatRepository(context);
        await repository.AddMessageAsync(
            seed.ConversationId,
            new ChatMessageRecord(
                Guid.NewGuid(),
                "tool",
                content,
                null,
                seed.ToolCallId,
                "test_tool",
                0,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<PendingChatAction?> GetActionAsync(
        ChatActionTestSeed seed,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatActionRepository(context);
        return await repository.FindAsync(
            seed.ActionId, seed.ConversationId, seed.UserId, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatActionAuditEntry>> GetAuditAsync(
        ChatActionTestSeed seed,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatActionRepository(context);
        return await repository.GetAuditAsync(
            seed.ConversationId, seed.UserId, cancellationToken);
    }

    public async Task<string?> GetToolMessageAsync(
        ChatActionTestSeed seed,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ChatRepository(context);
        var messages = await repository.GetMessagesAsync(seed.ConversationId, cancellationToken);
        return messages.SingleOrDefault(message =>
            message.Role == "tool" && message.ToolCallId == seed.ToolCallId)?.Content;
    }

    public async Task<string?> GetStoredToolMessageAsync(
        ChatActionTestSeed seed,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.ChatMessages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == seed.ConversationId
                && message.Role == "tool"
                && message.ToolCallId == seed.ToolCallId)
            .Select(message => message.Content)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task EnableAuditFailureAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION fail_chat_action_audit()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'injected chat action audit failure';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "FailChatActionAudit"
            BEFORE INSERT ON "ChatActionAudits"
            FOR EACH ROW EXECUTE FUNCTION fail_chat_action_audit();
            """,
            cancellationToken);
    }

    public async Task DisableAuditFailureAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS "FailChatActionAudit" ON "ChatActionAudits";
            DROP FUNCTION IF EXISTS fail_chat_action_audit();
            """,
            cancellationToken);
    }
}

internal sealed record ChatActionTestSeed(
    Guid ActionId,
    Guid ConversationId,
    Guid UserId,
    string ToolCallId,
    string ApprovalTokenHash,
    string ParameterHash);
