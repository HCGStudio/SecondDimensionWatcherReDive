namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IFileNameRegexRuleRepository
{
    /// <summary>
    ///     Returns rules newest-first. Filename matching uses the first rule that matches a file.
    /// </summary>
    Task<IReadOnlyList<FileNameRegexRule>> GetForAnimationAsync(
        Guid animationId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Atomically adds a rule or returns the existing rule with the same animation and pattern.
    /// </summary>
    Task<FileNameRegexRule> GetOrAddAsync(
        FileNameRegexRule rule,
        CancellationToken cancellationToken);
}
