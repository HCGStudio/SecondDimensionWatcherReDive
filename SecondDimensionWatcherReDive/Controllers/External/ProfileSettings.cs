using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record RenameProfileRequest([Required] string Name);
internal sealed record SetProfilePinRequest(string? Pin, string? CurrentPin);
