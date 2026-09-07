namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed class DuplicateReleaseException(string releaseIdentity, Exception innerException)
    : Exception("The release already exists.", innerException)
{
    public string ReleaseIdentity { get; } = releaseIdentity;
}
