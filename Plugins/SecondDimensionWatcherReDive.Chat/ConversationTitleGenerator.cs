using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat;

internal interface IConversationTitleGenerator
{
    Task<string?> GenerateAsync(
        string userMessage,
        string assistantMessage,
        string? model,
        CancellationToken cancellationToken);

    Task TryAutoTitleAsync(
        Guid conversationId,
        Guid profileId,
        string userMessage,
        string assistantMessage,
        string? model,
        CancellationToken cancellationToken);
}

internal sealed partial class ConversationTitleGenerator(
    IServiceProvider serviceProvider,
    IChatRepository chatRepository,
    ILogger<ConversationTitleGenerator> logger) : IConversationTitleGenerator
{
    private const int MaxTitleLength = 60;

    private const string SystemPrompt =
        "You generate a short conversation title from the first user/assistant exchange.\n" +
        "Rules:\n" +
        "- Output ONLY the title text. No explanation, no prefix.\n" +
        "- Single line. No quotes, no markdown, no numbering, no surrounding punctuation.\n" +
        "- Summarize the user's intent or topic, do not paraphrase a full sentence.\n" +
        "- Use the same language the user wrote in.\n" +
        "- Keep it concise (about 3-10 words; for CJK, 4-15 characters).";

    public async Task<string?> GenerateAsync(
        string userMessage,
        string assistantMessage,
        string? model,
        CancellationToken cancellationToken)
    {
        var aiEngine = serviceProvider.GetService<IAIEngine>();
        if (aiEngine is null)
            return null;

        var userExcerpt = Truncate(userMessage, 800);
        var assistantExcerpt = Truncate(assistantMessage, 800);

        var messages = new List<IMessage>
        {
            new SystemMessage(SystemPrompt),
            new UserMessage(
                $"User message:\n{userExcerpt}\n\nAssistant reply:\n{assistantExcerpt}\n\nTitle:")
        };

        var options = new ChatOptions
        {
            Model = model,
            ToolExecutor = null,
            // Single round only. With no ToolExecutor the engine breaks out after the
            // first response anyway; setting this to 0 would skip the provider call entirely.
            MaxToolRounds = 1,
            MaxTokens = 64
        };

        var sb = new StringBuilder();
        try
        {
            await foreach (var update in aiEngine.ChatAsync(messages, options, cancellationToken))
            {
                if (update is TextDelta td)
                    sb.Append(td.Text);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTitleGenerationFailed(ex);
            return null;
        }

        return Sanitize(sb.ToString());
    }

    public async Task TryAutoTitleAsync(
        Guid conversationId,
        Guid profileId,
        string userMessage,
        string assistantMessage,
        string? model,
        CancellationToken cancellationToken)
    {
        try
        {
            var title = await GenerateAsync(userMessage, assistantMessage, model, cancellationToken);
            if (string.IsNullOrWhiteSpace(title))
            {
                LogTitleEmpty(conversationId);
                return;
            }

            // Race-safety: only persist if title is still unset on the latest snapshot.
            var current = await chatRepository.GetConversationWithMessagesAsync(
                conversationId, profileId, cancellationToken);
            if (current is null)
                return;
            if (!IsAutoTitleEligible(current.Title))
            {
                LogTitleAlreadySet(conversationId);
                return;
            }

            await chatRepository.UpdateConversationTitleAsync(
                conversationId, profileId, title, cancellationToken);
            LogTitleSaved(conversationId, title);
        }
        catch (OperationCanceledException)
        {
            // Don't impact the chat main flow on cancel.
        }
        catch (Exception ex)
        {
            LogAutoTitleFailed(ex, conversationId);
        }
    }

    /// <summary>
    /// A conversation is eligible for auto-title only when its title has never been set
    /// (i.e. is null or whitespace). Any user-provided or previously-generated title is preserved.
    /// </summary>
    public static bool IsAutoTitleEligible(string? currentTitle)
        => string.IsNullOrWhiteSpace(currentTitle);

    public static string? Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var line = raw.Replace("\r", "\n");
        var firstLine = line
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var trimmed = firstLine.Trim();
        // Strip wrapping markdown emphasis / code fences.
        trimmed = trimmed.Trim('*', '_', '`').Trim();
        // Strip wrapping quotes (ascii + common CJK pairs).
        trimmed = StripWrappingQuotes(trimmed);
        // Strip leading list/numbering markers like "1. ", "- ", "• ".
        trimmed = StripLeadingMarker(trimmed).Trim();
        // Strip trailing terminal punctuation.
        trimmed = trimmed.TrimEnd('.', '。', '!', '！', '?', '？', ',', '，', ';', '；', ':', '：').Trim();

        if (trimmed.Length == 0)
            return null;

        if (trimmed.Length > MaxTitleLength)
            trimmed = trimmed[..MaxTitleLength].TrimEnd();

        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string StripWrappingQuotes(string s)
    {
        if (s.Length < 2) return s;
        (char open, char close)[] pairs =
        [
            ('"', '"'), ('\'', '\''),
            ('“', '”'), ('‘', '’'),
            ('「', '」'), ('『', '』'),
            ('《', '》'), ('（', '）'), ('(', ')'), ('[', ']'), ('【', '】')
        ];
        foreach (var (open, close) in pairs)
        {
            if (s[0] == open && s[^1] == close)
                return s.Substring(1, s.Length - 2).Trim();
        }
        return s;
    }

    private static string StripLeadingMarker(string s)
    {
        var i = 0;
        while (i < s.Length && (s[i] == '-' || s[i] == '*' || s[i] == '•' || s[i] == '·'))
            i++;
        // numbered: digits followed by . or )
        var j = i;
        while (j < s.Length && char.IsDigit(s[j])) j++;
        if (j > i && j < s.Length && (s[j] == '.' || s[j] == ')' || s[j] == '、'))
            i = j + 1;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return s[i..];
    }

    private static string Truncate(string s, int maxChars)
        => s.Length <= maxChars ? s : s[..maxChars];

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Chat] Title generation API call failed")]
    private partial void LogTitleGenerationFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: title model returned empty/invalid output")]
    private partial void LogTitleEmpty(Guid conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Chat] Conversation {ConversationId}: title already set, skipping auto-generation")]
    private partial void LogTitleAlreadySet(Guid conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Chat] Conversation {ConversationId}: auto title saved: {Title}")]
    private partial void LogTitleSaved(Guid conversationId, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Chat] Conversation {ConversationId}: auto-title flow failed (non-fatal)")]
    private partial void LogAutoTitleFailed(Exception ex, Guid conversationId);
}
