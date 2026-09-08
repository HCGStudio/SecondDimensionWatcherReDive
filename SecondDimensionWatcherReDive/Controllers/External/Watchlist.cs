using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed class WatchlistRequest
{
    private string? _tmdbId;
    private int? _mikanId;

    public Guid? Id { get; set; }

    [StringLength(64)]
    public string? TmdbId
    {
        get => _tmdbId;
        set { _tmdbId = value; TmdbIdSpecified = true; }
    }

    public int? MikanId
    {
        get => _mikanId;
        set { _mikanId = value; MikanIdSpecified = true; }
    }

    [JsonIgnore] public bool TmdbIdSpecified { get; private set; }
    [JsonIgnore] public bool MikanIdSpecified { get; private set; }

    [Required, StringLength(512, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [Required, StringLength(16)]
    public string Status { get; set; } = "";
}
