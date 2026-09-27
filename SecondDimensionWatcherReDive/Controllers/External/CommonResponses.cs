namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record MessageResponse(string Message);
internal sealed record ErrorResponse(string? Error);
internal sealed record CodedMessageResponse(string Code, string Message);
internal sealed record RegistrationAvailabilityResponse(bool Allow);
