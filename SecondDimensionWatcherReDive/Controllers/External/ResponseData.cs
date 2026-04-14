namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record ResponseData<T>(T Data, int TotalItems);
