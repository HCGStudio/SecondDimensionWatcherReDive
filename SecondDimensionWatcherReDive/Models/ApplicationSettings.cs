namespace SecondDimensionWatcherReDive.Models;

public sealed class ApplicationSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string ValuesJson { get; set; } = "{}";

    public string? ProtectedSecrets { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
