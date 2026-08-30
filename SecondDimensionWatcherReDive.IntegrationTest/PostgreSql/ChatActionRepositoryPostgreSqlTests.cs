using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
using Testcontainers.PostgreSql;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

[TestClass]
[DoNotParallelize]
public sealed class ChatActionRepositoryPostgreSqlTests
{
    private const string ApprovalRequiredResult =
        "{\"result\":{\"approval_required\":true}}";
    private const string SuccessfulResult =
        "{\"result\":{\"changed\":true},\"is_success\":true}";
    private const string InterruptedResult =
        "{\"error\":\"execution interrupted\",\"is_success\":false}";
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_chat_action_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static ChatActionRepositoryPostgreSqlTestFixture Fixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new ChatActionRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
        await Fixture.InitializeAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetDatabaseAsync() => await Fixture.ResetAsync(CancellationToken.None);

    [TestMethod]
    public async Task ClaimAndAuditAreCommittedAtomicallyWhenAuditInsertFails()
    {
        var seed = await Fixture.SeedAsync(null, CancellationToken.None);
        await Fixture.EnableAuditFailureAsync(CancellationToken.None);

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                Fixture.ClaimAsync(seed, DateTimeOffset.UtcNow, CancellationToken.None));
        }
        finally
        {
            await Fixture.DisableAuditFailureAsync(CancellationToken.None);
        }

        var action = await Fixture.GetActionAsync(seed, CancellationToken.None);
        var audit = await Fixture.GetAuditAsync(seed, CancellationToken.None);
        Assert.IsNotNull(action);
        Assert.AreEqual(ChatActionState.Pending, action.State);
        Assert.HasCount(1, audit);
        Assert.AreEqual(ChatActionAuditEvent.Requested, audit[0].Event);
    }

    [TestMethod]
    public async Task CompletionPersistsResultAndReconcilesExistingConversationHistory()
    {
        var seed = await Fixture.SeedAsync(ApprovalRequiredResult, CancellationToken.None);
        var claim = await Fixture.ClaimAsync(seed, DateTimeOffset.UtcNow, CancellationToken.None);

        var completed = await Fixture.CompleteAsync(
            seed, true, SuccessfulResult, DateTimeOffset.UtcNow, CancellationToken.None);

        var action = await Fixture.GetActionAsync(seed, CancellationToken.None);
        var message = await Fixture.GetToolMessageAsync(seed, CancellationToken.None);
        var storedMessage = await Fixture.GetStoredToolMessageAsync(seed, CancellationToken.None);
        var audit = await Fixture.GetAuditAsync(seed, CancellationToken.None);
        Assert.AreEqual(ChatActionClaimOutcome.Claimed, claim.Outcome);
        Assert.IsTrue(completed);
        Assert.IsNotNull(action);
        Assert.AreEqual(ChatActionState.Succeeded, action.State);
        Assert.AreEqual(SuccessfulResult, action.ToolResultJson);
        Assert.AreEqual(SuccessfulResult, message);
        Assert.AreEqual(SuccessfulResult, storedMessage);
        Assert.AreEqual(1, audit.Count(entry =>
            entry.Event == ChatActionAuditEvent.ExecutionSucceeded));
    }

    [TestMethod]
    public async Task CompletionBeforeMessageInsertIsReconciledWhenHistoryIsSaved()
    {
        var seed = await Fixture.SeedAsync(null, CancellationToken.None);
        await Fixture.ClaimAsync(seed, DateTimeOffset.UtcNow, CancellationToken.None);
        await Fixture.CompleteAsync(
            seed, true, SuccessfulResult, DateTimeOffset.UtcNow, CancellationToken.None);

        await Fixture.AddToolMessageAsync(
            seed, ApprovalRequiredResult, CancellationToken.None);

        Assert.AreEqual(
            SuccessfulResult,
            await Fixture.GetToolMessageAsync(seed, CancellationToken.None));
        Assert.AreEqual(
            SuccessfulResult,
            await Fixture.GetStoredToolMessageAsync(seed, CancellationToken.None));
    }

    [TestMethod]
    public async Task StaleExecutionRecoversOnceToAuditedFailureAndUpdatesHistory()
    {
        var seed = await Fixture.SeedAsync(ApprovalRequiredResult, CancellationToken.None);
        var startedAt = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(10));
        await Fixture.ClaimAsync(seed, startedAt, CancellationToken.None);

        var recovered = await Fixture.RecoverAsync(
            seed,
            DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(3)),
            InterruptedResult,
            "Execution owner stopped; outcome is unknown.",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var recoveredAgain = await Fixture.RecoverAsync(
            seed,
            DateTimeOffset.UtcNow,
            InterruptedResult,
            "Execution owner stopped; outcome is unknown.",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var action = await Fixture.GetActionAsync(seed, CancellationToken.None);
        var message = await Fixture.GetToolMessageAsync(seed, CancellationToken.None);
        var storedMessage = await Fixture.GetStoredToolMessageAsync(seed, CancellationToken.None);
        var audit = await Fixture.GetAuditAsync(seed, CancellationToken.None);
        Assert.AreEqual(1, recovered);
        Assert.AreEqual(0, recoveredAgain);
        Assert.IsNotNull(action);
        Assert.AreEqual(ChatActionState.Failed, action.State);
        Assert.AreEqual(InterruptedResult, action.ToolResultJson);
        Assert.AreEqual(InterruptedResult, message);
        Assert.AreEqual(InterruptedResult, storedMessage);
        Assert.AreEqual(1, audit.Count(entry =>
            entry.Event == ChatActionAuditEvent.ExecutionFailed));
    }
}
