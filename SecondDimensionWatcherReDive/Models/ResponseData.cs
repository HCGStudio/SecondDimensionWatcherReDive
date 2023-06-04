namespace SecondDimensionWatcherReDive.Models;

public record ResponseData<T>(T Data, int TotalItems);