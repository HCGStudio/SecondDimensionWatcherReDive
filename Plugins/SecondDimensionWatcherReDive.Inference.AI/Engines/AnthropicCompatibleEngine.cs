using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public class AnthropicCompatibleEngine(
    IHttpClientFactory httpClientFactory,
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options,
    ILogger<AnthropicCompatibleEngine> logger)
    : InferenceEngineBase(httpClientFactory, tmdbTool, options)
{
    protected override ILogger Logger => logger;

    protected override string ProviderName => "Anthropic";

    private static JsonObject BuildToolDefinition()
    {
        return new JsonObject
        {
            ["name"] = "search_tmdb",
            ["description"] = "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata.",
            ["input_schema"] = new JsonObject
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
        };
    }

    protected override async Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken)
    {
        var opts = Options;

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = $"Title: {title}\nDescription: {description}" }
        };

        var tools = new JsonArray { BuildToolDefinition() };

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = opts.Model,
                ["max_tokens"] = opts.MaxTokens,
                ["system"] = SystemPrompt,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone()
            };

            var endpoint = opts.BaseUrl.TrimEnd('/') + "/v1/messages";
            using var response = await SendRequestAsync(endpoint, req =>
            {
                req.Headers.Add("x-api-key", opts.ApiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
            }, requestBody, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Anthropic API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return null;
            }

            var json = JsonNode.Parse(responseBody);
            var contentBlocks = json?["content"]?.AsArray();
            var stopReason = json?["stop_reason"]?.GetValue<string>();

            if (contentBlocks == null)
            {
                logger.LogWarning("Anthropic API returned no content in response");
                return null;
            }

            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = contentBlocks.DeepClone()
            });

            if (stopReason == "tool_use")
            {
                var toolResultBlocks = new JsonArray();

                foreach (var block in contentBlocks)
                {
                    if (block?["type"]?.GetValue<string>() != "tool_use") continue;

                    var toolName = block["name"]?.GetValue<string>();
                    var toolUseId = block["id"]?.GetValue<string>();
                    var input = block["input"];

                    if (toolName == "search_tmdb" && input != null)
                    {
                        var query = input["query"]?.GetValue<string>();
                        var result = await ExecuteToolCallAsync(query, title, cancellationToken);

                        toolResultBlocks.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = toolUseId,
                            ["content"] = result
                        });
                    }
                }

                if (toolResultBlocks.Count > 0)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = toolResultBlocks.DeepClone()
                    });
                }

                continue;
            }

            var textContent = string.Join("", contentBlocks
                .Where(b => b?["type"]?.GetValue<string>() == "text")
                .Select(b => b?["text"]?.GetValue<string>() ?? ""));

            return ParseInferenceResult(textContent);
        }

        logger.LogWarning("Anthropic inference exceeded max tool rounds for title: {Title}", title);
        return null;
    }
}
