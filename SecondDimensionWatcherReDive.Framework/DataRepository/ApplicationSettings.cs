namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record ApplicationSettings(
    int Id,
    string ValuesJson,
    string? ProtectedSecrets,
    long Revision,
    DateTimeOffset UpdatedAt);
