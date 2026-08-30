using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PostgresScheduledTaskLeaseManagerTests
{
    [TestMethod]
    public async Task GetStatusesAsync_DerivesCrossReplicaStateFromPersistedLease()
    {
        var now = DateTimeOffset.UtcNow;
        var lastCompleted = now.AddMinutes(-2);
        var repository = new Mock<IScheduledTaskLeaseRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetStatesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                CancellationToken.None))
            .ReturnsAsync(
            [
                new ScheduledTaskLeaseState(
                    "remote-running",
                    "instance-b",
                    now.AddMinutes(5),
                    now.AddMinutes(-1),
                    lastCompleted),
                new ScheduledTaskLeaseState(
                    "cooldown",
                    "instance-b",
                    now.AddMinutes(5),
                    now.AddMinutes(-2),
                    now.AddMinutes(-1)),
                new ScheduledTaskLeaseState(
                    "expired-incomplete",
                    "instance-b",
                    now.AddMinutes(-1),
                    now.AddMinutes(-2),
                    null),
                new ScheduledTaskLeaseState(
                    "ownerless",
                    null,
                    now.AddMinutes(5),
                    now.AddMinutes(-1),
                    null)
            ]);
        using var services = new ServiceCollection()
            .AddScoped<IScheduledTaskLeaseRepository>(_ => repository.Object)
            .BuildServiceProvider();
        var manager = new PostgresScheduledTaskLeaseManager(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PostgresScheduledTaskLeaseManager>.Instance);

        var statuses = await manager.GetStatusesAsync(
            ["remote-running", "cooldown", "expired-incomplete", "ownerless", "not-started"],
            CancellationToken.None);

        Assert.IsTrue(statuses["remote-running"].IsRunning);
        Assert.AreEqual(lastCompleted, statuses["remote-running"].LastRunAt);
        Assert.IsFalse(statuses["cooldown"].IsRunning);
        Assert.AreEqual(now.AddMinutes(-1), statuses["cooldown"].LastRunAt);
        Assert.IsFalse(statuses["expired-incomplete"].IsRunning);
        Assert.IsNull(statuses["expired-incomplete"].LastRunAt);
        Assert.IsFalse(statuses["ownerless"].IsRunning);
        Assert.IsFalse(statuses["not-started"].IsRunning);
        Assert.IsNull(statuses["not-started"].LastRunAt);
    }
}
