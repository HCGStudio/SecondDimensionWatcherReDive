using System.ComponentModel.DataAnnotations;
namespace SecondDimensionWatcherReDive.Controllers.External;
internal sealed record WatchlistRequest(Guid? Id,
    [StringLength(64)] string? TmdbId,
    int? MikanId,
    [Required, StringLength(512, MinimumLength = 1)] string Title,
    [Required, StringLength(16)] string Status);
