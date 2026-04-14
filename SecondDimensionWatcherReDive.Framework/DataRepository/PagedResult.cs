namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public readonly record struct PagedResult<T>(IReadOnlyList<T> Data, int TotalCount);
