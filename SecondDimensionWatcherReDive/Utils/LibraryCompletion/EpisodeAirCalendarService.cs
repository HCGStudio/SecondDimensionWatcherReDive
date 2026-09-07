using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

public sealed class EpisodeAirCalendarService(TmdbTool tmdbTool, IMemoryCache cache)
{
    public async Task<EpisodeAirCalendar> GetAsync(string tmdbId, int season, CancellationToken cancellationToken)
    {
        var key = $"episode-air-dates:{tmdbId}:{season}";
        if (cache.TryGetValue<EpisodeAirCalendar>(key, out var cached)) return cached!;
        if (!int.TryParse(tmdbId, out var id) || !tmdbTool.IsConfigured)
            return new([], DateTimeOffset.UtcNow, "unknown");
        var json = await tmdbTool.GetSeasonEpisodesAsync(id, season, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(json);
        var episodes = new List<EpisodeAirDate>();
        if (document.RootElement.TryGetProperty("episodes", out var rows))
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("episode_number", out var number) || !number.TryGetInt32(out var episode) || episode <= 0) continue;
                var raw = row.TryGetProperty("air_date", out var date) ? date.GetString() : null;
                var valid = DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
                episodes.Add(new(episode, valid ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null));
            }
        var result = new EpisodeAirCalendar(episodes, DateTimeOffset.UtcNow, episodes.Count > 0 ? "TMDB" : "unknown");
        cache.Set(key, result, episodes.Count > 0 ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(10));
        return result;
    }
}
