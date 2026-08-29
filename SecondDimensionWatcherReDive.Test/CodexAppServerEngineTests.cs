using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Codex;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Engines;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.Providers;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class CodexAppServerEngineTests
{
    [TestMethod]
    public async Task ChatAsync_HandshakesInjectsSafeHistoryAndCompletesStreamedText()
    {
        var transport = new ScriptedTransport(
            Response(1, "{}"),
            Response(2, PermissionProfiles()),
            Response(3, SafeThread()),
            Response(4, "{}"),
            Response(5, """{"turn":{"id":"turn-1"}}"""),
            """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":""}}}""",
            """{"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"Hel"}}""",
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":"Hello"}}}""",
            """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed"}}}""");
        var (engine, factory) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [
                new SystemMessage("system instructions"),
                new UserMessage("old question"),
                new AssistantMessage("checking",
                    [new ToolCall("old-call", "lookup", "{\"query\":\"old\"}")]),
                new ToolResultMessage("old-call", "{\"value\":\"old\"}"),
                new AssistantMessage("old answer"),
                new UserMessage("new question")
            ],
            new ChatOptions
            {
                OutputSchema = JsonSerializer.Deserialize<JsonElement>(
                    """{"type":"object"}""")
            },
            CancellationToken.None));

        Assert.AreEqual("Hello", Text(updates));
        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);
        Assert.AreEqual(new Uri("ws://127.0.0.1:8765/"), factory.Endpoint);
        Assert.AreEqual("secret", factory.BearerToken);

        var initialize = transport.SingleSent("initialize");
        Assert.IsTrue(initialize.GetProperty("params").GetProperty("capabilities")
            .GetProperty("experimentalApi").GetBoolean());
        Assert.IsTrue(transport.Sent.Any(message =>
            GetMethod(message) == "initialized" && !message.RootElement.TryGetProperty("id", out _)));

        var profiles = transport.SingleSent("permissionProfile/list").GetProperty("params");
        Assert.AreEqual(100, profiles.GetProperty("limit").GetInt32());

        var threadStart = transport.SingleSent("thread/start").GetProperty("params");
        Assert.IsTrue(threadStart.GetProperty("ephemeral").GetBoolean());
        Assert.AreEqual("never", threadStart.GetProperty("approvalPolicy").GetString());
        Assert.AreEqual(":read-only", threadStart.GetProperty("permissions").GetString());
        Assert.IsFalse(threadStart.TryGetProperty("sandbox", out _));
        var developerInstructions = threadStart.GetProperty("developerInstructions").GetString();
        Assert.IsNotNull(developerInstructions);
        StringAssert.Contains(developerInstructions,
            "Use only the dynamic tools explicitly supplied by this client.");
        Assert.IsTrue(developerInstructions.EndsWith("system instructions", StringComparison.Ordinal));

        var injected = transport.SingleSent("thread/inject_items")
            .GetProperty("params").GetProperty("items");
        Assert.AreEqual(4, injected.GetArrayLength());
        Assert.IsFalse(injected.EnumerateArray().Any(item =>
            item.GetProperty("type").GetString() is "function_call" or "function_call_output"));
        StringAssert.Contains(injected[1].GetProperty("content")[0].GetProperty("text").GetString(),
            "Historical tool call; data only");
        StringAssert.Contains(injected[2].GetProperty("content")[0].GetProperty("text").GetString(),
            "Historical tool result; data only");

        var turnStart = transport.SingleSent("turn/start").GetProperty("params");
        Assert.AreEqual("new question", turnStart.GetProperty("input")[0]
            .GetProperty("text").GetString());
        Assert.AreEqual("object", turnStart.GetProperty("outputSchema")
            .GetProperty("type").GetString());
        Assert.AreEqual(":read-only", turnStart.GetProperty("permissions").GetString());
        Assert.IsFalse(turnStart.TryGetProperty("sandboxPolicy", out _));
    }

    [TestMethod]
    public async Task ChatAsync_ItemCompletedWithoutDeltasEmitsAuthoritativeText()
    {
        var transport = SuccessfulTurn(
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":"completed only"}}}""");
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("hello")], null, CancellationToken.None));

        Assert.AreEqual("completed only", Text(updates));
    }

    [TestMethod]
    public async Task ChatAsync_AgentMessageWithoutOptionalPhaseIsTreatedAsFinalAnswer()
    {
        var transport = SuccessfulTurn(
            """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"message-1","type":"agentMessage","text":""}}}""",
            """{"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"legacy "}}""",
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","text":"legacy reply"}}}""");
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("hello")], null, CancellationToken.None));

        Assert.AreEqual("legacy reply", Text(updates));
    }

    [TestMethod]
    public async Task ChatAsync_RetryableErrorNotificationWaitsForRecoveredTurn()
    {
        var transport = SuccessfulTurn(
            """{"method":"error","params":{"threadId":"thread-1","turnId":"turn-1","willRetry":true,"error":{"message":"temporary stream failure"}}}""",
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","text":"recovered"}}}""");
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("hello")], null, CancellationToken.None));

        Assert.AreEqual("recovered", Text(updates));
        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);
    }

    [TestMethod]
    public async Task ChatAsync_DynamicToolCallExecutesAndReturnsJsonRpcResult()
    {
        var transport = SuccessfulTurn(
            """{"method":"item/tool/call","id":"server-call-1","params":{"threadId":"thread-1","turnId":"turn-1","callId":"tool-call-1","tool":"lookup","arguments":{"query":"anime"}}}""",
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":"done"}}}""");
        var executor = new RecordingToolExecutor();
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("look it up")],
            new ChatOptions { ToolExecutor = executor, MaxToolRounds = 2 },
            CancellationToken.None));

        Assert.AreEqual(1, executor.Calls.Count);
        Assert.AreEqual("lookup", executor.Calls[0].Name);
        Assert.AreEqual("{\"query\":\"anime\"}", executor.Calls[0].Arguments);
        Assert.AreEqual(1, updates.OfType<ToolCallBegin>().Count());
        Assert.AreEqual(1, updates.OfType<ToolCallDelta>().Count());
        Assert.AreEqual(1, updates.OfType<ToolResultUpdate>().Count());
        Assert.AreEqual("done", Text(updates));

        var threadStart = transport.SingleSent("thread/start").GetProperty("params");
        var dynamicTool = threadStart.GetProperty("dynamicTools")[0];
        Assert.AreEqual("function", dynamicTool.GetProperty("type").GetString());
        Assert.AreEqual("lookup", dynamicTool.GetProperty("name").GetString());
        Assert.AreEqual("object", dynamicTool.GetProperty("inputSchema")
            .GetProperty("type").GetString());

        var result = transport.SingleResponse("server-call-1").GetProperty("result");
        Assert.IsTrue(result.GetProperty("success").GetBoolean());
        StringAssert.Contains(result.GetProperty("contentItems")[0].GetProperty("text").GetString(),
            "tool-value");
    }

    [TestMethod]
    public async Task ChatAsync_ToolResultTransportFailureStillYieldsAuditUpdates()
    {
        var transport = SuccessfulTurn(
            """{"method":"item/tool/call","id":"server-call-1","params":{"threadId":"thread-1","turnId":"turn-1","callId":"tool-call-1","tool":"lookup","arguments":{"query":"anime"}}}""");
        transport.FailSendingResponseId = "server-call-1";
        var (engine, _) = CreateEngine(transport);
        var updates = new List<IChatUpdate>();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in engine.ChatAsync(
                               [new UserMessage("look it up")],
                               new ChatOptions
                               {
                                   ToolExecutor = new RecordingToolExecutor(),
                                   MaxToolRounds = 1
                               },
                               CancellationToken.None))
                updates.Add(update);
        });

        StringAssert.Contains(exception.Message, "deliver dynamic tool result");
        Assert.AreEqual(1, updates.OfType<ToolCallBegin>().Count());
        Assert.AreEqual(1, updates.OfType<ToolCallDelta>().Count());
        Assert.AreEqual(1, updates.OfType<ToolResultUpdate>().Count());
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task ChatAsync_CancellationWhileSendingToolResultStillYieldsAuditUpdates()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = SuccessfulTurn(
            """{"method":"item/tool/call","id":"server-call-1","params":{"threadId":"thread-1","turnId":"turn-1","callId":"tool-call-1","tool":"lookup","arguments":{"query":"anime"}}}""");
        transport.FailSendingResponseId = "server-call-1";
        transport.CancelWhenFailingResponse = cancellation;
        var (engine, _) = CreateEngine(transport);
        var updates = new List<IChatUpdate>();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in engine.ChatAsync(
                               [new UserMessage("look it up")],
                               new ChatOptions
                               {
                                   ToolExecutor = new RecordingToolExecutor(),
                                   MaxToolRounds = 1
                               },
                               cancellation.Token))
                updates.Add(update);
        });

        Assert.AreEqual(1, updates.OfType<ToolCallBegin>().Count());
        Assert.AreEqual(1, updates.OfType<ToolCallDelta>().Count());
        Assert.AreEqual(1, updates.OfType<ToolResultUpdate>().Count());
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task ChatAsync_ExcessDynamicToolCallFailsAndInterruptsTurn()
    {
        var transport = StandardTurn(
            ToolRequest("server-call-1", "tool-call-1"),
            ToolRequest("server-call-2", "tool-call-2"));
        var executor = new RecordingToolExecutor();
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            engine.ChatAsync(
                [new UserMessage("use tools")],
                new ChatOptions { ToolExecutor = executor, MaxToolRounds = 1 },
                CancellationToken.None)));

        StringAssert.Contains(exception.Message, "tool-call limit");
        Assert.AreEqual(1, executor.Calls.Count);
        var rejected = transport.SingleResponse("server-call-2").GetProperty("error");
        Assert.AreEqual(-32000, rejected.GetProperty("code").GetInt32());
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task ChatAsync_BufferedUpdateOverflowFailsClosed()
    {
        var messages = new List<string>
        {
            Response(1, "{}"),
            Response(2, PermissionProfiles()),
            Response(3, SafeThread()),
            """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":""}}}"""
        };
        messages.AddRange(Enumerable.Range(0, 1025).Select(_ =>
            """{"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"message-1","delta":"x"}}"""));
        messages.Add(Response(4, """{"turn":{"id":"turn-1"}}"""));
        var transport = new ScriptedTransport(messages.ToArray());
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            CollectAsync(engine.ChatAsync(
                [new UserMessage("overflow")],
                null,
                CancellationToken.None)));

        StringAssert.Contains(exception.Message, "buffered update limit (1024)");
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task GetAvailableModelsAsync_FollowsModelListPagination()
    {
        var transport = new ScriptedTransport(
            Response(1, "{}"),
            Response(2,
                """{"data":[{"id":"gpt-5.3-codex","displayName":"GPT-5.3 Codex"}],"nextCursor":"page-2"}"""),
            Response(3,
                """{"data":[{"model":"fallback-id"}],"nextCursor":null}"""));
        var (engine, _) = CreateEngine(transport);

        var models = await engine.GetAvailableModelsAsync(CancellationToken.None);

        Assert.AreEqual(2, models.Count);
        Assert.AreEqual("gpt-5.3-codex", models[0].Id);
        Assert.AreEqual("GPT-5.3 Codex", models[0].Name);
        Assert.AreEqual("fallback-id", models[1].Id);
        Assert.AreEqual("CodexAppServer", models[1].Provider);

        var requests = transport.SentFor("model/list");
        Assert.AreEqual(2, requests.Count);
        var firstPage = requests[0].GetProperty("params");
        Assert.AreEqual(100, firstPage.GetProperty("limit").GetInt32());
        Assert.IsFalse(firstPage.GetProperty("includeHidden").GetBoolean());
        Assert.IsFalse(firstPage.TryGetProperty("cursor", out var firstCursor) &&
                       firstCursor.ValueKind != JsonValueKind.Null);
        Assert.AreEqual("page-2", requests[1].GetProperty("params")
            .GetProperty("cursor").GetString());
    }

    [TestMethod]
    public async Task ChatAsync_NormalCompletionUnsubscribesEphemeralThread()
    {
        var transport = SuccessfulTurn();
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("hello")], null, CancellationToken.None));

        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);
        Assert.IsTrue(transport.SingleSent("thread/start").GetProperty("params")
            .GetProperty("ephemeral").GetBoolean());
        var unsubscribe = transport.SingleSent("thread/unsubscribe").GetProperty("params");
        Assert.AreEqual("thread-1", unsubscribe.GetProperty("threadId").GetString());
        Assert.AreEqual(0, transport.SentFor("thread/delete").Count,
            "Ephemeral root threads cannot be deleted by the app-server protocol.");
    }

    [TestMethod]
    public async Task ChatAsync_UnavailablePermissionProfileFailsBeforeStartingThread()
    {
        var transport = new ScriptedTransport(
            Response(1, "{}"),
            Response(2,
                """{"data":[{"id":":read-only","allowed":false}],"nextCursor":null}"""));
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            engine.ChatAsync([new UserMessage("hello")], null, CancellationToken.None)));

        StringAssert.Contains(exception.Message, "is not allowed");
        Assert.AreEqual(0, transport.SentFor("thread/start").Count);
    }

    [TestMethod]
    public async Task ChatAsync_ProfileResolvingToUnsafeSandboxFailsClosedAndUnsubscribes()
    {
        var transport = new ScriptedTransport(
            Response(1, "{}"),
            Response(2, PermissionProfiles()),
            Response(3,
                """{"thread":{"id":"thread-1"},"activePermissionProfile":{"id":":read-only","extends":null},"sandbox":{"type":"workspaceWrite","networkAccess":false}}"""));
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            engine.ChatAsync([new UserMessage("hello")], null, CancellationToken.None)));

        StringAssert.Contains(exception.Message, "read-only sandbox");
        Assert.AreEqual(0, transport.SentFor("turn/start").Count);
        Assert.AreEqual("thread-1", transport.SingleSent("thread/unsubscribe")
            .GetProperty("params").GetProperty("threadId").GetString());
    }

    [TestMethod]
    public async Task ChatAsync_ErrorNotificationThrowsAndInterruptsTurn()
    {
        var transport = StandardTurn(
            """{"method":"error","params":{"error":{"message":"agent exploded"}}}""");
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            engine.ChatAsync([new UserMessage("hello")], null, CancellationToken.None)));

        StringAssert.Contains(exception.Message, "agent exploded");
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task ChatAsync_CancellationInterruptsActiveTurn()
    {
        var transport = StandardTurn();
        var (engine, _) = CreateEngine(transport);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await CollectAsync(engine.ChatAsync(
                [new UserMessage("wait")], null, cancellation.Token));
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
    }

    [TestMethod]
    public async Task ChatAsync_EngineTimeoutThrowsTimeoutExceptionWhileCallerRemainsActive()
    {
        var transport = StandardTurn();
        transport.AutoRespondToInterrupt = true;
        var (engine, _) = CreateEngine(transport, timeoutSeconds: 1);
        using var callerCancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() => CollectAsync(
            engine.ChatAsync([new UserMessage("wait")], null, callerCancellation.Token)));

        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
        Assert.IsFalse(callerCancellation.IsCancellationRequested);
        Assert.IsNotNull(transport.SingleSent("turn/interrupt"));
        Assert.IsNotNull(transport.SingleSent("thread/unsubscribe"));
    }

    [TestMethod]
    public async Task GetAvailableModelsAsync_EngineTimeoutThrowsTimeoutExceptionWhileCallerRemainsActive()
    {
        var transport = new ScriptedTransport(Response(1, "{}"));
        var (engine, _) = CreateEngine(transport, timeoutSeconds: 1);
        using var callerCancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            engine.GetAvailableModelsAsync(callerCancellation.Token));

        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
        Assert.IsFalse(callerCancellation.IsCancellationRequested);
    }

    [TestMethod]
    public async Task ChatAsync_ServerInterruptionWithoutCallerCancellationIsNormalFailure()
    {
        var transport = StandardTurn(
            """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"interrupted"}}}""");
        var (engine, _) = CreateEngine(transport);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            engine.ChatAsync([new UserMessage("wait")], null, CancellationToken.None)));

        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
        Assert.IsNotNull(transport.SingleSent("thread/unsubscribe"));
    }

    [TestMethod]
    public async Task ChatAsync_EarlyTurnStartedAllowsCancellationToInterruptAnnouncedTurn()
    {
        var transport = new ScriptedTransport(
            Response(1, "{}"),
            Response(2, PermissionProfiles()),
            Response(3, SafeThread()),
            """{"method":"turn/started","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"inProgress","items":[]}}}""");
        var (engine, _) = CreateEngine(transport);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await CollectAsync(engine.ChatAsync(
                [new UserMessage("wait")], null, cancellation.Token));
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        var interrupt = transport.SingleSent("turn/interrupt").GetProperty("params");
        Assert.AreEqual("thread-1", interrupt.GetProperty("threadId").GetString());
        Assert.AreEqual("turn-1", interrupt.GetProperty("turnId").GetString());
    }

    [TestMethod]
    public async Task ChatAsync_UnknownServerRequestIsRejectedFailClosed()
    {
        var transport = SuccessfulTurn(
            """{"method":"item/commandExecution/requestApproval","id":99,"params":{"threadId":"thread-1","turnId":"turn-1"}}""",
            """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","completedAtMs":1,"item":{"id":"message-1","type":"agentMessage","phase":"final_answer","text":"safe"}}}""");
        var (engine, _) = CreateEngine(transport);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("hello")], null, CancellationToken.None));

        Assert.AreEqual("safe", Text(updates));
        var error = transport.SingleResponse(99).GetProperty("error");
        Assert.AreEqual(-32601, error.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public void RouterAndSingletonStatusFollowCurrentOptionsSnapshot()
    {
        var aiOptions = new TestOptionsMonitor<AIOptions>(new()
        {
            Engine = AIEngineKind.BuiltIn,
            Provider = "OpenAI"
        });
        var builtIn = new StubBackend(AIEngineKind.BuiltIn, "BuiltIn/OpenAI", true);
        var codex = new StubBackend(AIEngineKind.CodexAppServer, "CodexAppServer", true);
        var router = new AIEngineRouter([builtIn, codex], aiOptions);

        Assert.AreEqual("BuiltIn/OpenAI", router.Name);
        aiOptions.Set(new() { Engine = AIEngineKind.CodexAppServer, Provider = "OpenAI" });
        Assert.AreEqual("CodexAppServer", router.Name);

        var openAI = new TestOptionsMonitor<OpenAIOptions>(new()
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "key",
            Model = "model"
        });
        var anthropic = new TestOptionsMonitor<AnthropicOptions>(new());
        var codexOptions = new TestOptionsMonitor<CodexAppServerOptions>(new()
        {
            Endpoint = "ws://example.com:8765",
            TimeoutSeconds = 10
        });
        var status = new AIEngineStatus(aiOptions, openAI, anthropic, codexOptions);

        Assert.AreEqual("CodexAppServer", status.Name);
        Assert.IsFalse(status.IsConfigured, "A remote WebSocket endpoint must not be accepted.");
        codexOptions.Set(new()
        {
            Endpoint = "wss://agent.example.com/app-server",
            BearerToken = "secret",
            TimeoutSeconds = 10
        });
        Assert.IsTrue(status.IsConfigured, "A remote endpoint is allowed when it uses TLS.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIEngine(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Engine"] = "CodexAppServer",
            ["AI:CodexAppServer:Endpoint"] = "ws://127.0.0.1:8765"
        }).Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        Assert.IsInstanceOfType<AIEngineStatus>(provider.GetRequiredService<IAIEngineStatus>());
    }

    [TestMethod]
    public void CodexEngineConfiguration_OnlyAllowsPlainWebSocketOnLoopback()
    {
        var transport = new ScriptedTransport();
        var monitor = new TestOptionsMonitor<CodexAppServerOptions>(new()
        {
            Endpoint = "ws://app-server.example.com:8765",
            TimeoutSeconds = 30
        });
        var engine = new CodexAppServerEngine(
            new ScriptedTransportFactory(transport),
            monitor,
            NullLogger<CodexAppServerEngine>.Instance);

        Assert.IsFalse(engine.IsConfigured);

        monitor.Set(new()
        {
            Endpoint = "ws://127.0.0.1:8765",
            TimeoutSeconds = 30
        });
        Assert.IsTrue(engine.IsConfigured);

        monitor.Set(new()
        {
            Endpoint = "wss://app-server.example.com/socket",
            BearerToken = "secret",
            TimeoutSeconds = 30
        });
        Assert.IsTrue(engine.IsConfigured);

        monitor.Set(new()
        {
            Endpoint = "https://app-server.example.com/socket",
            TimeoutSeconds = 30
        });
        Assert.IsFalse(engine.IsConfigured);
    }

    [TestMethod]
    public void Providers_RetainSnapshotOptionsConstructors()
    {
        var clientFactory = new NoOpHttpClientFactory();
        var openAI = new OpenAIProvider(
            clientFactory,
            Options.Create(new OpenAIOptions
            {
                ApiKey = "openai-key",
                Model = "openai-model"
            }),
            NullLogger<OpenAIProvider>.Instance);
        var anthropic = new AnthropicProvider(
            clientFactory,
            Options.Create(new AnthropicOptions
            {
                ApiKey = "anthropic-key",
                Model = "anthropic-model"
            }),
            NullLogger<AnthropicProvider>.Instance);

        Assert.IsTrue(openAI.IsConfigured);
        Assert.IsTrue(anthropic.IsConfigured);
    }

    private static (CodexAppServerEngine Engine, ScriptedTransportFactory Factory) CreateEngine(
        ScriptedTransport transport,
        int timeoutSeconds = 30)
    {
        var factory = new ScriptedTransportFactory(transport);
        var engine = new CodexAppServerEngine(
            factory,
            new TestOptionsMonitor<CodexAppServerOptions>(new()
            {
                Endpoint = "ws://127.0.0.1:8765",
                BearerToken = "secret",
                Model = "configured-model",
                PermissionProfile = ":read-only",
                TimeoutSeconds = timeoutSeconds
            }),
            NullLogger<CodexAppServerEngine>.Instance);
        return (engine, factory);
    }

    private static ScriptedTransport StandardTurn(params string[] turnMessages)
        => new(
            [
                Response(1, "{}"),
                Response(2, PermissionProfiles()),
                Response(3, SafeThread()),
                Response(4, """{"turn":{"id":"turn-1"}}"""),
                .. turnMessages
            ]);

    private static ScriptedTransport SuccessfulTurn(params string[] turnMessages)
        => new(
            [
                Response(1, "{}"),
                Response(2, PermissionProfiles()),
                Response(3, SafeThread()),
                Response(4, """{"turn":{"id":"turn-1"}}"""),
                .. turnMessages,
                CompletedTurn()
            ]);

    private static string Response(long id, string result)
        => $"{{\"id\":{id},\"result\":{result}}}";

    private static string PermissionProfiles()
        => """{"data":[{"id":":read-only","allowed":true,"description":null}],"nextCursor":null}""";

    private static string SafeThread()
        => """{"thread":{"id":"thread-1"},"activePermissionProfile":{"id":":read-only","extends":null},"sandbox":{"type":"readOnly","networkAccess":false}}""";

    private static string CompletedTurn()
        => """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed"}}}""";

    private static string ToolRequest(string requestId, string callId)
        => $"{{\"method\":\"item/tool/call\",\"id\":\"{requestId}\",\"params\":{{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"callId\":\"{callId}\",\"tool\":\"lookup\",\"arguments\":{{\"query\":\"anime\"}}}}}}";

    private static string Text(IEnumerable<IChatUpdate> updates)
        => string.Concat(updates.OfType<TextDelta>().Select(update => update.Text));

    private static async Task<List<IChatUpdate>> CollectAsync(IAsyncEnumerable<IChatUpdate> stream)
    {
        var updates = new List<IChatUpdate>();
        await foreach (var update in stream)
            updates.Add(update);
        return updates;
    }

    private static string? GetMethod(JsonDocument message)
        => message.RootElement.TryGetProperty("method", out var method)
            ? method.GetString()
            : null;

    private sealed class RecordingToolExecutor : IToolExecutor
    {
        public IReadOnlyList<ToolDefinition> ToolDefinitions { get; } =
        [
            new("lookup", "Look up a value", JsonSerializer.Deserialize<JsonElement>(
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""))
        ];

        public List<ToolCall> Calls { get; } = [];

        public Task<IToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            Calls.Add(toolCall);
            return Task.FromResult<IToolResult>(new ToolSuccessResult<string>("tool-value"));
        }
    }

    private sealed class ScriptedTransportFactory(ScriptedTransport transport)
        : ICodexAppServerTransportFactory
    {
        public Uri? Endpoint { get; private set; }
        public string? BearerToken { get; private set; }

        public Task<ICodexAppServerTransport> ConnectAsync(
            Uri endpoint,
            string? bearerToken,
            CancellationToken cancellationToken)
        {
            Endpoint = endpoint;
            BearerToken = bearerToken;
            return Task.FromResult<ICodexAppServerTransport>(transport);
        }
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class ScriptedTransport : ICodexAppServerTransport
    {
        private readonly ConcurrentQueue<string> _received;
        private readonly object _sentLock = new();

        public ScriptedTransport(params string[] received)
        {
            _received = new(received);
        }

        public List<JsonDocument> Sent { get; } = [];

        public string? FailSendingResponseId { get; set; }

        public CancellationTokenSource? CancelWhenFailingResponse { get; set; }

        public bool AutoRespondToInterrupt { get; set; }

        public ValueTask SendAsync(string message, CancellationToken cancellationToken)
        {
            JsonDocument parsed;
            lock (_sentLock)
            {
                parsed = JsonDocument.Parse(message);
                Sent.Add(parsed);
            }

            if (FailSendingResponseId is not null &&
                !parsed.RootElement.TryGetProperty("method", out _) &&
                parsed.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                string.Equals(id.GetString(), FailSendingResponseId, StringComparison.Ordinal))
            {
                if (CancelWhenFailingResponse is { } cancellation)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }

                throw new IOException("simulated transport failure");
            }

            if (AutoRespondToInterrupt
                && parsed.RootElement.TryGetProperty("method", out var method)
                && string.Equals(method.GetString(), "turn/interrupt", StringComparison.Ordinal)
                && parsed.RootElement.TryGetProperty("id", out var requestId)
                && requestId.TryGetInt64(out var numericRequestId))
                _received.Enqueue(Response(numericRequestId, "{}"));

            return ValueTask.CompletedTask;
        }

        public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_received.TryDequeue(out var message)) return message;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public JsonElement SingleSent(string method)
        {
            lock (_sentLock)
                return Sent.Single(message => GetMethod(message) == method).RootElement.Clone();
        }

        public IReadOnlyList<JsonElement> SentFor(string method)
        {
            lock (_sentLock)
                return Sent.Where(message => GetMethod(message) == method)
                    .Select(message => message.RootElement.Clone())
                    .ToArray();
        }

        public JsonElement SingleResponse(object id)
        {
            lock (_sentLock)
                return Sent.Single(message =>
                {
                    var root = message.RootElement;
                    if (root.TryGetProperty("method", out _) ||
                        !root.TryGetProperty("id", out var actual))
                        return false;
                    return id switch
                    {
                        string text => actual.ValueKind == JsonValueKind.String &&
                                       actual.GetString() == text,
                        int number => actual.ValueKind == JsonValueKind.Number &&
                                      actual.GetInt32() == number,
                        _ => false
                    };
                }).RootElement.Clone();
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class StubBackend(
        AIEngineKind kind,
        string name,
        bool isConfigured) : IAIEngineBackend
    {
        public AIEngineKind Kind => kind;
        public string Name => name;
        public bool IsConfigured => isConfigured;

        public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AIModel>>([]);

        public async IAsyncEnumerable<IChatUpdate> ChatAsync(
            IReadOnlyList<IMessage> messages,
            ChatOptions? options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new Finished("stop");
        }
    }
}
