using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Engines;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.Providers;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class OpenAIProviderTests
{
    private static readonly Uri BaseAddress = new("https://api.example.com/v1/");

    [TestMethod]
    public async Task DefaultMode_UsesChatCompletionsRequestAndStreamsText()
    {
        var handler = new RecordingHandler(Sse(
            """{"choices":[{"index":0,"delta":{"content":"Hel"}}]}""",
            """{"choices":[{"index":0,"delta":{"content":"lo"}}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]"));
        var provider = CreateProvider(handler);

        var updates = await CollectAsync(provider.StreamChatCompletionAsync(
            [new SystemMessage("be concise"), new UserMessage("hello")],
            null,
            null,
            null,
            null,
            CancellationToken.None));

        Assert.AreEqual("Hello", Text(updates));
        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);

        var captured = handler.Requests.Single();
        Assert.AreEqual("https://api.example.com/v1/chat/completions", captured.Uri);
        Assert.AreEqual("Bearer sk-test", captured.Authorization);

        using var document = JsonDocument.Parse(captured.Body);
        var root = document.RootElement;
        Assert.AreEqual("configured-model", root.GetProperty("model").GetString());
        Assert.IsTrue(root.GetProperty("stream").GetBoolean());
        Assert.AreEqual(321, root.GetProperty("max_tokens").GetInt32());
        Assert.AreEqual(2, root.GetProperty("messages").GetArrayLength());
        Assert.IsFalse(root.TryGetProperty("input", out _));
        Assert.IsFalse(root.TryGetProperty("max_output_tokens", out _));
    }

    [TestMethod]
    public async Task ResponsesMode_UsesResponsesRequestShapeAndStreamsText()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"Res"}""",
            """{"type":"response.output_text.delta","output_index":0,"delta":"ponse"}""",
            """{"type":"response.completed","response":{"id":"resp_1","status":"completed","output":[{"id":"msg_1","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Response"}],"phase":"final_answer"}]}}""",
            "[DONE]"));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);
        var tools = new[]
        {
            new ToolDefinition("lookup", "Look something up", Schema(
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""),
                ToolRiskLevel.ReadOnly)
        };

        var updates = await CollectAsync(provider.StreamChatCompletionAsync(
            [new SystemMessage("be concise"), new UserMessage("hello")],
            tools,
            null,
            null,
            null,
            CancellationToken.None));

        Assert.AreEqual("Response", Text(updates));
        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);

        var captured = handler.Requests.Single();
        Assert.AreEqual("https://api.example.com/v1/responses", captured.Uri);
        Assert.AreEqual("Bearer sk-test", captured.Authorization);

        using var document = JsonDocument.Parse(captured.Body);
        var root = document.RootElement;
        Assert.AreEqual("configured-model", root.GetProperty("model").GetString());
        Assert.IsTrue(root.GetProperty("stream").GetBoolean());
        Assert.IsFalse(root.GetProperty("store").GetBoolean());
        Assert.IsFalse(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.AreEqual("disabled", root.GetProperty("truncation").GetString());
        Assert.AreEqual(321, root.GetProperty("max_output_tokens").GetInt32());
        CollectionAssert.AreEqual(
            new[] { "reasoning.encrypted_content" },
            root.GetProperty("include").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.IsFalse(root.TryGetProperty("messages", out _));

        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.AreEqual(2, input.Length);
        Assert.AreEqual("message", input[0].GetProperty("type").GetString());
        Assert.AreEqual("system", input[0].GetProperty("role").GetString());
        Assert.AreEqual("input_text",
            input[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.AreEqual("user", input[1].GetProperty("role").GetString());

        var tool = root.GetProperty("tools")[0];
        Assert.AreEqual("function", tool.GetProperty("type").GetString());
        Assert.AreEqual("lookup", tool.GetProperty("name").GetString());
        Assert.AreEqual("Look something up", tool.GetProperty("description").GetString());
        Assert.AreEqual("object", tool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.IsFalse(tool.GetProperty("strict").GetBoolean());
        Assert.IsFalse(tool.TryGetProperty("function", out _),
            "Responses tools must be flat rather than nested under a function property.");
    }

    [TestMethod]
    public async Task ResponsesMode_CompressesPersistedToolHistoryToVisibleMessages()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"Follow-up answer"}""",
            """{"type":"response.completed","response":{"id":"resp_followup","status":"completed","output":[{"id":"msg_followup","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Follow-up answer"}]}]}}""",
            "[DONE]"));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);

        await CollectAsync(provider.StreamChatCompletionAsync(
            [
                new UserMessage("original question"),
                new AssistantMessage("I'll check.",
                    [new ToolCall("old_call", "lookup", "{\"query\":\"old\"}")]),
                new ToolResultMessage("old_call", "{\"value\":\"old result\"}"),
                new AssistantMessage("The previous answer."),
                new UserMessage("follow up")
            ],
            null,
            null,
            null,
            null,
            CancellationToken.None));

        using var document = JsonDocument.Parse(handler.Requests.Single().Body);
        var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();

        Assert.AreEqual(6, input.Length);
        CollectionAssert.AreEqual(
            new[] { "user", "assistant", "assistant", "assistant", "assistant", "user" },
            input.Select(item => item.GetProperty("role").GetString()).ToArray());
        Assert.IsFalse(input.Any(item =>
            item.GetProperty("type").GetString() is "function_call" or "function_call_output"),
            "Historical call/result pairs must not be replayed without their original reasoning items.");
        Assert.AreEqual("I'll check.",
            input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.AreEqual("input_text",
            input[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.AreEqual("commentary", input[1].GetProperty("phase").GetString());
        StringAssert.Contains(
            input[2].GetProperty("content")[0].GetProperty("text").GetString(),
            "lookup");
        StringAssert.Contains(
            input[3].GetProperty("content")[0].GetProperty("text").GetString(),
            "old result");
        Assert.AreEqual("The previous answer.",
            input[4].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.AreEqual("final_answer", input[4].GetProperty("phase").GetString());
    }

    [TestMethod]
    public async Task AIEngine_TwoResponseToolRounds_ReplaysRawOutputAndOnlyAppendsToolResult()
    {
        const string reasoningOutput =
            """{"id":"rs_1","type":"reasoning","encrypted_content":"encrypted-reasoning","summary":[],"phase":"analysis"}""";
        const string functionCallOutput =
            """{"id":"fc_1","type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"query\":\"one\"}","status":"completed","phase":"commentary"}""";

        var firstRound = Sse(
            """{"type":"response.output_item.added","output_index":1,"item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"query\":\"","status":"in_progress"}}""",
            """{"type":"response.function_call_arguments.delta","output_index":1,"item_id":"fc_1","delta":"one\"}"}""",
            """{"type":"response.function_call_arguments.done","output_index":1,"item_id":"fc_1","arguments":"{\"query\":\"one\"}"}""",
            "{\"type\":\"response.completed\",\"response\":{\"id\":\"resp_1\",\"status\":\"completed\",\"output\":[" +
            reasoningOutput + "," + functionCallOutput + "]}}",
            "[DONE]");
        var secondRound = Sse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"All done"}""",
            """{"type":"response.completed","response":{"id":"resp_2","status":"completed","output":[{"id":"msg_2","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"All done"}],"phase":"final_answer"}]}}""",
            "[DONE]");
        var handler = new RecordingHandler(firstRound, secondRound);
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);
        var toolExecutor = new RecordingToolExecutor();
        var engine = new AIEngine(provider, NullLogger<AIEngine>.Instance);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("use the lookup tool")],
            new ChatOptions
            {
                ToolExecutor = toolExecutor,
                MaxToolRounds = 1
            },
            CancellationToken.None));

        Assert.AreEqual("All done", Text(updates));
        Assert.AreEqual("stop", updates.OfType<Finished>().Single().StopReason);
        Assert.AreEqual(1, updates.OfType<ToolResultUpdate>().Count());
        Assert.AreEqual(1, toolExecutor.Calls.Count);
        Assert.AreEqual("call_1", toolExecutor.Calls[0].Id);
        Assert.AreEqual("lookup", toolExecutor.Calls[0].Name);
        Assert.AreEqual("{\"query\":\"one\"}", toolExecutor.Calls[0].Arguments);
        Assert.AreEqual(2, handler.Requests.Count);

        using var secondRequestDocument = JsonDocument.Parse(handler.Requests[1].Body);
        var secondRequestRoot = secondRequestDocument.RootElement;
        var secondInput = secondRequestRoot
            .GetProperty("input")
            .EnumerateArray()
            .ToArray();

        Assert.IsFalse(secondRequestRoot.TryGetProperty("tools", out _),
            "The final-answer round must not advertise tools after the tool-round budget is exhausted.");
        Assert.AreEqual(4, secondInput.Length);
        Assert.AreEqual("message", secondInput[0].GetProperty("type").GetString());
        Assert.AreEqual("reasoning", secondInput[1].GetProperty("type").GetString());
        Assert.AreEqual("encrypted-reasoning",
            secondInput[1].GetProperty("encrypted_content").GetString());
        Assert.AreEqual("analysis", secondInput[1].GetProperty("phase").GetString());
        Assert.AreEqual("function_call", secondInput[2].GetProperty("type").GetString());
        Assert.AreEqual("commentary", secondInput[2].GetProperty("phase").GetString());
        Assert.AreEqual("call_1", secondInput[2].GetProperty("call_id").GetString());
        Assert.AreEqual("function_call_output", secondInput[3].GetProperty("type").GetString());
        Assert.AreEqual("call_1", secondInput[3].GetProperty("call_id").GetString());
        StringAssert.Contains(secondInput[3].GetProperty("output").GetString(), "tool-value");

        Assert.IsTrue(JsonElement.DeepEquals(Schema(reasoningOutput), secondInput[1]),
            "The encrypted reasoning output item must be replayed without reshaping it.");
        Assert.IsTrue(JsonElement.DeepEquals(Schema(functionCallOutput), secondInput[2]),
            "The function-call output item, including phase, must be replayed without reshaping it.");
        Assert.AreEqual(1, secondInput.Count(item =>
            item.GetProperty("type").GetString() == "function_call"));
        Assert.AreEqual(1, secondInput.Count(item =>
            item.GetProperty("type").GetString() == "function_call_output"));
        Assert.IsFalse(secondInput.Any(item =>
            item.GetProperty("type").GetString() == "message" &&
            item.TryGetProperty("role", out var role) && role.GetString() == "assistant"),
            "The engine's synthesized assistant tool-call message must not be appended to Responses continuation input.");
    }

    [TestMethod]
    public async Task ResponsesContinuation_PinsEndpointAndCredentialAcrossToolRounds()
    {
        var firstRound = Sse(
            """{"type":"response.output_item.added","output_index":0,"item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"query\":\"one\"}","status":"in_progress"}}""",
            """{"type":"response.completed","response":{"id":"resp_1","status":"completed","output":[{"id":"fc_1","type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"query\":\"one\"}","status":"completed"}]}}""",
            "[DONE]");
        var secondRound = Sse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"done"}""",
            """{"type":"response.completed","response":{"id":"resp_2","status":"completed","output":[{"id":"msg_2","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"done"}]}]}}""",
            "[DONE]");
        var handler = new RecordingHandler(firstRound, secondRound);
        var monitor = new TestOptionsMonitor<OpenAIOptions>(new()
        {
            BaseUrl = "https://origin-a.example/v1",
            ApiKey = "key-a",
            Model = "model-a",
            ApiMode = OpenAIApiMode.Responses,
            MaxTokens = 100
        });
        var provider = new OpenAIProvider(
            new StubHttpClientFactory(handler),
            monitor,
            NullLogger<OpenAIProvider>.Instance);

        var firstUpdates = await CollectAsync(provider.StreamChatCompletionAsync(
            [new UserMessage("use a tool")],
            null,
            null,
            null,
            null,
            CancellationToken.None));
        var continuation = firstUpdates.OfType<Finished>().Single().Continuation;
        Assert.IsNotNull(continuation);

        monitor.Set(new OpenAIOptions
        {
            BaseUrl = "https://origin-b.example/v1",
            ApiKey = "key-b",
            Model = "model-b",
            ApiMode = OpenAIApiMode.Responses,
            MaxTokens = 200
        });

        await CollectAsync(provider.StreamChatCompletionAsync(
            [new ToolResultMessage("call_1", "tool result")],
            null,
            null,
            null,
            continuation,
            CancellationToken.None));

        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(request =>
            request.Uri == "https://origin-a.example/v1/responses"));
        Assert.IsTrue(handler.Requests.All(request => request.Authorization == "Bearer key-a"));
        using var secondRequest = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.AreEqual("model-a", secondRequest.RootElement.GetProperty("model").GetString());
        Assert.AreEqual(100, secondRequest.RootElement.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    public async Task AIEngine_ZeroToolRounds_DoesNotAdvertiseOrExecuteTools()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.output_text.delta","output_index":0,"delta":"No tools needed"}""",
            """{"type":"response.completed","response":{"id":"resp_final","status":"completed","output":[{"id":"msg_final","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"No tools needed"}]}]}}""",
            "[DONE]"));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);
        var toolExecutor = new RecordingToolExecutor();
        var engine = new AIEngine(provider, NullLogger<AIEngine>.Instance);

        var updates = await CollectAsync(engine.ChatAsync(
            [new UserMessage("answer without tools")],
            new ChatOptions
            {
                ToolExecutor = toolExecutor,
                MaxToolRounds = 0
            },
            CancellationToken.None));

        Assert.AreEqual("No tools needed", Text(updates));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(0, toolExecutor.Calls.Count);
        using var document = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.IsFalse(document.RootElement.TryGetProperty("tools", out _));
    }

    [TestMethod]
    public async Task AIEngine_UnexpectedToolCallInFinalRound_IsNotExposedOrExecuted()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.output_item.added","output_index":0,"item":{"id":"fc_unexpected","type":"function_call","call_id":"call_unexpected","name":"lookup","arguments":"","status":"in_progress"}}""",
            """{"type":"response.function_call_arguments.delta","output_index":0,"item_id":"fc_unexpected","delta":"{\"query\":\"unexpected\"}"}""",
            """{"type":"response.function_call_arguments.done","output_index":0,"item_id":"fc_unexpected","arguments":"{\"query\":\"unexpected\"}"}""",
            """{"type":"response.completed","response":{"id":"resp_unexpected","status":"completed","output":[{"id":"fc_unexpected","type":"function_call","call_id":"call_unexpected","name":"lookup","arguments":"{\"query\":\"unexpected\"}","status":"completed"}]}}""",
            "[DONE]"));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);
        var toolExecutor = new RecordingToolExecutor();
        var engine = new AIEngine(provider, NullLogger<AIEngine>.Instance);
        var exposedUpdates = new List<IChatUpdate>();
        InvalidOperationException? exception = null;

        try
        {
            await foreach (var update in engine.ChatAsync(
                               [new UserMessage("do not use tools")],
                               new ChatOptions
                               {
                                   ToolExecutor = toolExecutor,
                                   MaxToolRounds = 0
                               },
                               CancellationToken.None))
                exposedUpdates.Add(update);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        Assert.IsNotNull(exception);
        StringAssert.Contains(exception.Message, "tool-call limit");
        Assert.AreEqual(0, toolExecutor.Calls.Count);
        Assert.IsFalse(exposedUpdates.Any(update =>
                update is ToolCallBegin or ToolCallDelta or ToolResultUpdate),
            "An unexecutable final-round call must not leak into persisted chat history.");
    }

    [TestMethod]
    public async Task AIEngine_MismatchedCompletedToolCall_DoesNotExecuteStreamedCall()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.output_item.added","output_index":0,"item":{"id":"fc_streamed","type":"function_call","call_id":"call_streamed","name":"lookup","arguments":"","status":"in_progress"}}""",
            """{"type":"response.function_call_arguments.delta","output_index":0,"item_id":"fc_streamed","delta":"{\"query\":\"streamed\"}"}""",
            """{"type":"response.function_call_arguments.done","output_index":0,"item_id":"fc_streamed","arguments":"{\"query\":\"streamed\"}"}""",
            """{"type":"response.completed","response":{"id":"resp_mismatch","status":"completed","output":[{"id":"fc_completed","type":"function_call","call_id":"call_completed","name":"lookup","arguments":"{\"query\":\"different\"}","status":"completed"}]}}""",
            "[DONE]"));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);
        var toolExecutor = new RecordingToolExecutor();
        var engine = new AIEngine(provider, NullLogger<AIEngine>.Instance);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => CollectAsync(
            engine.ChatAsync(
                [new UserMessage("look something up")],
                new ChatOptions
                {
                    ToolExecutor = toolExecutor,
                    MaxToolRounds = 1
                },
                CancellationToken.None)));

        StringAssert.Contains(exception.Message, "did not match its streamed call");
        Assert.AreEqual(0, toolExecutor.Calls.Count,
            "No tool may execute when the authoritative completed output disagrees with the SSE deltas.");
    }

    [TestMethod]
    public async Task ResponsesMode_IncompleteResponseThrowsWithReason()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.incomplete","response":{"id":"resp_incomplete","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}}"""));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CollectAsync(
            provider.StreamChatCompletionAsync(
                [new UserMessage("hello")],
                null,
                null,
                null,
                null,
                CancellationToken.None)));

        StringAssert.Contains(exception.Message, "max_output_tokens");
    }

    [TestMethod]
    public async Task ResponsesMode_FailedResponseThrowsHttpRequestException()
    {
        var handler = new RecordingHandler(Sse(
            """{"type":"response.failed","response":{"id":"resp_failed","status":"failed","error":{"code":"server_error","message":"provider exploded"},"output":[]}}"""));
        var provider = CreateProvider(handler, OpenAIApiMode.Responses);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => CollectAsync(
            provider.StreamChatCompletionAsync(
                [new UserMessage("hello")],
                null,
                null,
                null,
                null,
                CancellationToken.None)));

        StringAssert.Contains(exception.Message, "provider exploded");
    }

    private static OpenAIProvider CreateProvider(
        RecordingHandler handler,
        OpenAIApiMode? apiMode = null)
    {
        var openAIOptions = new OpenAIOptions
        {
            BaseUrl = BaseAddress.ToString().TrimEnd('/'),
            ApiKey = "sk-test",
            Model = "configured-model",
            MaxTokens = 321
        };
        if (apiMode is not null)
            openAIOptions.ApiMode = apiMode.Value;

        return new OpenAIProvider(
            new StubHttpClientFactory(handler),
            Options.Create(openAIOptions),
            NullLogger<OpenAIProvider>.Instance);
    }

    private static JsonElement Schema(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);

    private static string Sse(params string[] dataBlocks)
        => string.Concat(dataBlocks.Select(data => $"data: {data}\n\n"));

    private static async Task<List<IChatUpdate>> CollectAsync(IAsyncEnumerable<IChatUpdate> stream)
    {
        var updates = new List<IChatUpdate>();
        await foreach (var update in stream)
            updates.Add(update);
        return updates;
    }

    private static string Text(IEnumerable<IChatUpdate> updates)
        => string.Concat(updates.OfType<TextDelta>().Select(update => update.Text));

    private sealed class RecordingToolExecutor : IToolExecutor
    {
        public IReadOnlyList<ToolDefinition> ToolDefinitions { get; } =
        [
            new ToolDefinition("lookup", "Look something up", Schema(
                """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""),
                ToolRiskLevel.ReadOnly)
        ];

        public List<ToolCall> Calls { get; } = [];

        public Task<IToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            Calls.Add(toolCall);
            return Task.FromResult<IToolResult>(new ToolSuccessResult<string>("tool-value"));
        }
    }

    private sealed class StubHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual("OpenAI", name);
            var client = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = BaseAddress
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "sk-test");
            return client;
        }
    }

    private sealed record CapturedRequest(string Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> _responseBodies = new(responseBodies);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responseBodies.Count == 0)
                throw new InvalidOperationException("No stub response was queued for this request.");

            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseBodies.Dequeue(), Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
