using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public class OpenAiCompatibleEngine(
    IHttpClientFactory httpClientFactory,
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options,
    ILogger<OpenAiCompatibleEngine> logger)
    : InferenceEngineBase(httpClientFactory, tmdbTool, options)
{
    protected override ILogger Logger => logger;

    protected override string ProviderName => "OpenAI";

    private static JsonObject BuildToolDefinition()
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "search_tmdb",
                ["description"] = "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The anime name to search for"
                        }
                    },
                    ["required"] = new JsonArray("query")
                }
            }
        };
    }

    protected override async Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken)
    {
        var opts = Options;

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = $"Title: {title}\nDescription: {description}" }
        };

        var tools = new JsonArray { BuildToolDefinition() };

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = opts.Model,
                ["max_tokens"] = opts.MaxTokens,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone()
            };

            var endpoint = opts.BaseUrl.TrimEnd('/') + "/chat/completions";
            using var response = await SendRequestAsync(endpoint,
                req => req.Headers.Add("Authorization", $"Bearer {opts.ApiKey}"),
                requestBody, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return null;
            }

            var json = JsonNode.Parse(responseBody);
            var choice = json?["choices"]?[0];
            var message = choice?["message"];
            var finishReason = choice?["finish_reason"]?.GetValue<string>();

            if (message == null)
            {
                logger.LogWarning("OpenAI API returned no message in response");
                return null;
            }

            messages.Add(message.DeepClone());

            if (finishReason == "tool_calls")
            {
                var toolCalls = message["tool_calls"]?.AsArray();
                if (toolCalls == null) continue;

                foreach (var toolCall in toolCalls)
                {
                    var functionName = toolCall?["function"]?["name"]?.GetValue<string>();
                    var arguments = toolCall?["function"]?["arguments"]?.GetValue<string>();
                    var toolCallId = toolCall?["id"]?.GetValue<string>();

                    if (functionName == "search_tmdb" && arguments != null)
                    {
                        var args = JsonNode.Parse(arguments);
                        var query = args?["query"]?.GetValue<string>();
                        var result = await ExecuteToolCallAsync(query, title, cancellationToken);

                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = toolCallId,
                            ["content"] = result
                        });
                    }
                }

                continue;
            }

            var content = message["content"]?.GetValue<string>();
            return ParseInferenceResult(content);
        }

        logger.LogWarning("OpenAI inference exceeded max tool rounds for title: {Title}", title);
        return null;
    }
}
