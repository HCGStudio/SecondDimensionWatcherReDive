using System.ComponentModel.DataAnnotations;
using SecondDimensionWatcherReDive.Framework.DataRepository;
namespace SecondDimensionWatcherReDive.Controllers.External;
internal sealed record MediaTimelineRequest(Guid AnimationInfoId,
    [Required, StringLength(2048)] string Path,
    [Required, StringLength(64)] string MediaVersion,
    [Range(0.001, 2678400)] double DurationSeconds,
    bool SeasonDefault,
    [Required, MaxLength(200)] MediaTimelinePoint[] Points);
internal sealed record MediaTimelineAcceptRequest(Guid AnimationInfoId,
    [Required, StringLength(2048)] string Path,
    [Required, StringLength(64)] string MediaVersion,
    [Range(0.001, 2678400)] double DurationSeconds);
