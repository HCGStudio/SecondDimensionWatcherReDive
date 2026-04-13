using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace SecondDimensionWatcherReDive.Utils.Scraper;

public static partial class MikananiScraper
{
    public const string BaseUrl = "https://mikanani.me";

    /// <summary>Season name to Chinese character mapping for mikanani API.</summary>
    public static readonly Dictionary<string, string> SeasonMap = new()
    {
        ["春"] = "春",
        ["夏"] = "夏",
        ["秋"] = "秋",
        ["冬"] = "冬"
    };

    public record ScrapedBangumi(int MikanId, string Title, int DayOfWeek, string? ImageUrl);

    public record ScrapedSubgroup(int SubgroupId, string Name);

    /// <summary>
    ///     Scrapes mikanani.me for a specific season's anime list.
    ///     Uses the homepage for current season (year/season null),
    ///     or /Home/BangumiCoverFlowByDayOfWeek for other seasons.
    /// </summary>
    public static async Task<List<ScrapedBangumi>> ScrapeSeasonAsync(
        HttpClient httpClient, ILogger logger,
        int? year = null, string? season = null,
        CancellationToken cancellationToken = default)
    {
        string html;
        if (year != null && !string.IsNullOrEmpty(season))
        {
            var url = $"{BaseUrl}/Home/BangumiCoverFlowByDayOfWeek?year={year}&seasonStr={Uri.EscapeDataString(season)}";
            html = await httpClient.GetStringAsync(url, cancellationToken);
        }
        else
        {
            html = await httpClient.GetStringAsync(BaseUrl, cancellationToken);
        }

        return ParseBangumiHtml(html, logger);
    }

    private static List<ScrapedBangumi> ParseBangumiHtml(string html, ILogger logger)
    {
        var result = new List<ScrapedBangumi>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var dayGroups = doc.DocumentNode.SelectNodes("//div[contains(@class,'sk-bangumi')]");
        if (dayGroups == null)
        {
            LogNoDayGroupsFound(logger);
            return result;
        }

        foreach (var group in dayGroups)
        {
            var dayAttr = group.GetAttributeValue("data-dayofweek", -1);
            if (dayAttr < 0) continue;

            var entries = group.SelectNodes(".//ul[contains(@class,'an-ul')]/li");
            if (entries == null) continue;

            foreach (var li in entries)
            {
                try
                {
                    var bangumiSpan = li.SelectSingleNode(
                        ".//span[contains(@class,'js-expand_bangumi')]");
                    var idStr = bangumiSpan?.GetAttributeValue("data-bangumiid", "");
                    if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out var mikanId))
                        continue;

                    var titleNode = li.SelectSingleNode(".//a[contains(@class,'an-text')]");
                    var title = titleNode?.GetAttributeValue("title", "")?.Trim();
                    if (string.IsNullOrEmpty(title))
                        title = titleNode?.InnerText?.Trim() ?? "";
                    if (string.IsNullOrEmpty(title)) continue;
                    title = HtmlEntity.DeEntitize(title);

                    var imageNode = li.SelectSingleNode(
                        ".//span[contains(@class,'b-lazy')]");
                    var imageUrl = imageNode?.GetAttributeValue("data-src", null!);

                    result.Add(new ScrapedBangumi(mikanId, title, dayAttr, imageUrl));
                }
                catch (Exception ex)
                {
                    LogParseBangumiEntryFailed(logger, ex);
                }
            }
        }

        LogScrapedBangumiEntries(logger, result.Count);
        return result;
    }

    /// <summary>
    ///     Scrapes a bangumi detail page for subgroup information.
    /// </summary>
    public static async Task<List<ScrapedSubgroup>> ScrapeSubgroupsAsync(
        HttpClient httpClient, int mikanId, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ScrapedSubgroup>();
        var url = $"{BaseUrl}/Home/Bangumi/{mikanId}";
        var html = await httpClient.GetStringAsync(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var subgroupNodes = doc.DocumentNode.SelectNodes(
            "//li[contains(@class,'leftbar-item')]//a[contains(@class,'subgroup-name')]");
        if (subgroupNodes == null)
        {
            LogNoSubgroupsFound(logger, mikanId);
            return result;
        }

        foreach (var node in subgroupNodes)
        {
            try
            {
                var classAttr = node.GetAttributeValue("class", "");
                var match = SubgroupIdRegex().Match(classAttr);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var subgroupId))
                    continue;

                var name = HtmlEntity.DeEntitize(node.InnerText?.Trim() ?? "");
                if (string.IsNullOrEmpty(name)) continue;

                result.Add(new ScrapedSubgroup(subgroupId, name));
            }
            catch (Exception ex)
            {
                LogParseSubgroupEntryFailed(logger, ex, mikanId);
            }
        }

        LogScrapedSubgroups(logger, result.Count, mikanId);
        return result;
    }

    public static string BuildRssUrl(int mikanId, int? subgroupId = null)
    {
        var url = $"{BaseUrl}/RSS/Bangumi?bangumiId={mikanId}";
        if (subgroupId != null)
            url += $"&subgroupid={subgroupId}";
        return url;
    }

    [GeneratedRegex(@"subgroup-(\d+)")]
    private static partial Regex SubgroupIdRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "No day groups found in mikanani HTML")]
    private static partial void LogNoDayGroupsFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse a bangumi entry")]
    private static partial void LogParseBangumiEntryFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scraped {Count} bangumi entries from mikanani")]
    private static partial void LogScrapedBangumiEntries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No subgroups found for bangumi {MikanId}")]
    private static partial void LogNoSubgroupsFound(ILogger logger, int mikanId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse a subgroup entry for bangumi {MikanId}")]
    private static partial void LogParseSubgroupEntryFailed(ILogger logger, Exception ex, int mikanId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scraped {Count} subgroups for bangumi {MikanId}")]
    private static partial void LogScrapedSubgroups(ILogger logger, int count, int mikanId);
}
