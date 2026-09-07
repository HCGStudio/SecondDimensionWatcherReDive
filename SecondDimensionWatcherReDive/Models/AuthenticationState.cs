namespace SecondDimensionWatcherReDive.Models;

public sealed class AuthenticationState
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string PasswordHash { get; set; } = string.Empty;

    public Guid ClaimId { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
}
