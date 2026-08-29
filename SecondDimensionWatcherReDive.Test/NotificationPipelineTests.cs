using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Utils.Notifications;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class NotificationPipelineTests
{
    [TestMethod]
    public async Task PublishAsync_SubscribedEvent_EnqueuesStableOutboxEnvelope()
    {
        var repository = new Mock<INotificationOutboxRepository>();
        repository.Setup(candidate => candidate.EnqueueAsync(
                It.IsAny<NotificationOutboxMessage>(), CancellationToken.None))
            .ReturnsAsync(true);
        using var services = new ServiceCollection()
            .AddScoped(_ => repository.Object)
            .BuildServiceProvider();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Notifications:Webhook:Enabled"] = "true",
            ["Notifications:Events"] = "ReleaseMatched,DownloadCompleted"
        });
        var publisher = new NotificationPublisher(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            Mock.Of<ILogger<NotificationPublisher>>());

        await publisher.PublishAsync(new NotificationEvent(
            NotificationEventType.ReleaseMatched,
            "release:stable",
            "Matched",
            "Anime title",
            "/todo?focus=automation:id"), CancellationToken.None);

        repository.Verify(candidate => candidate.EnqueueAsync(
            It.Is<NotificationOutboxMessage>(message =>
                message.DeduplicationKey == "release:stable"
                && message.Type == NotificationEventType.ReleaseMatched
                && message.Status == NotificationDeliveryStatus.Pending
                && message.AttemptCount == 0),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task PublishAsync_PersistenceFailure_DoesNotEscapeIntoCoreOperation()
    {
        var repository = new Mock<INotificationOutboxRepository>();
        repository.Setup(candidate => candidate.EnqueueAsync(
                It.IsAny<NotificationOutboxMessage>(), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        using var services = new ServiceCollection()
            .AddScoped(_ => repository.Object)
            .BuildServiceProvider();
        var publisher = new NotificationPublisher(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(new Dictionary<string, string?>
            {
                ["Notifications:Webhook:Enabled"] = "true",
                ["Notifications:Events"] = "IncidentOpened"
            }),
            Mock.Of<ILogger<NotificationPublisher>>());

        await publisher.PublishAsync(new NotificationEvent(
            NotificationEventType.IncidentOpened,
            "incident:stable",
            "Incident",
            "Detail",
            "/incidents"), CancellationToken.None);
    }

    [TestMethod]
    public async Task DeliverBatchAsync_Success_SendsIdempotencyHeaderAndMarksDelivered()
    {
        var message = Message();
        var repository = new Mock<INotificationOutboxRepository>();
        repository.Setup(candidate => candidate.ClaimDueAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(), CancellationToken.None))
            .ReturnsAsync([message]);
        string? eventId = null;
        string? body = null;
        var handler = new DelegateHandler(async request =>
        {
            eventId = request.Headers.GetValues("X-SDW-Event-Id").Single();
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var service = DeliveryService(repository.Object, handler);
        var count = await service.DeliverBatchAsync(CancellationToken.None);

        Assert.AreEqual(1, count);
        Assert.AreEqual(message.Id.ToString("D"), eventId);
        StringAssert.Contains(body!, "\"deepLink\":\"/todo?focus=automation:item\"");
        repository.Verify(candidate => candidate.MarkDeliveredAsync(
            message.Id, It.IsAny<DateTimeOffset>(), CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeliverBatchAsync_ServerFailure_RecordsRetryWithoutThrowing()
    {
        var message = Message();
        var repository = new Mock<INotificationOutboxRepository>();
        repository.Setup(candidate => candidate.ClaimDueAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(), CancellationToken.None))
            .ReturnsAsync([message]);
        var service = DeliveryService(repository.Object,
            new DelegateHandler(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await service.DeliverBatchAsync(CancellationToken.None);

        repository.Verify(candidate => candidate.MarkFailedAsync(
            message.Id,
            1,
            It.IsAny<DateTimeOffset>(),
            It.Is<DateTimeOffset?>(next => next.HasValue),
            "HTTP 503",
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeliverBatchAsync_NetworkFailure_DoesNotLogSecretWebhookUrl()
    {
        var message = Message();
        var repository = new Mock<INotificationOutboxRepository>();
        repository.Setup(candidate => candidate.ClaimDueAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(), CancellationToken.None))
            .ReturnsAsync([message]);
        const string SecretUrl = "https://hooks.example.test/sdw?token=never-log-this";
        var logger = new CollectingLogger<NotificationDeliveryBackgroundService>();
        var service = DeliveryService(
            repository.Object,
            new DelegateHandler(_ => throw new HttpRequestException(SecretUrl)),
            logger,
            SecretUrl);

        await service.DeliverBatchAsync(CancellationToken.None);

        Assert.IsTrue(logger.Entries.Any(entry => entry.Contains(
            nameof(HttpRequestException), StringComparison.Ordinal)));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Contains(
            "never-log-this", StringComparison.Ordinal)));
    }

    private static NotificationDeliveryBackgroundService DeliveryService(
        INotificationOutboxRepository repository,
        HttpMessageHandler handler,
        ILogger<NotificationDeliveryBackgroundService>? logger = null,
        string endpoint = "https://hooks.example.test/sdw")
    {
        var scopeServices = new ServiceCollection()
            .AddScoped(_ => repository)
            .BuildServiceProvider();
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(candidate => candidate.CreateClient("NotificationWebhook"))
            .Returns(new HttpClient(handler));
        return new NotificationDeliveryBackgroundService(
            scopeServices.GetRequiredService<IServiceScopeFactory>(),
            clients.Object,
            Configuration(new Dictionary<string, string?>
            {
                ["Notifications:Webhook:Enabled"] = "true",
                ["Notifications:Webhook:Url"] = endpoint,
                ["Notifications:QuietHours:TimeZone"] = "UTC"
            }),
            logger ?? Mock.Of<ILogger<NotificationDeliveryBackgroundService>>());
    }

    private static NotificationOutboxMessage Message() => new(
        Guid.NewGuid(),
        "release:stable",
        NotificationEventType.ReleaseMatched,
        "Matched",
        "Anime title",
        "/todo?focus=automation:item",
        "{\"animationId\":\"item\"}",
        DateTimeOffset.UtcNow,
        NotificationDeliveryStatus.Pending,
        0,
        DateTimeOffset.UtcNow,
        null,
        null,
        null);

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            if (exception is not null)
                Entries.Add(exception.ToString());
        }
    }
}
