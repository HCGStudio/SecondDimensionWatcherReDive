using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.MigrationTasks;

public sealed partial class MigrateFileMappings(
    IServiceScopeFactory scopeFactory,
    ILogger<MigrateFileMappings> logger) : IMigrationTask
{
    private const int PageSize = 50;
    private const int InferenceMaxRetryCount = 3;

    public string Key => "MigrateFileMappings";

    // Version 2 reruns the old marker-based migration because v1 could mark a
    // partially failed pass as complete.
    public int Version => 2;

    public MigrationFailurePolicy FailurePolicy => MigrationFailurePolicy.BlockStartup;

    public async Task ExecuteAsync(
        MigrationExecutionContext context,
        CancellationToken cancellationToken)
    {
        var checkpoint = ParseCheckpoint(context.Checkpoint);
        var migrated = checkpoint?.Migrated ?? 0;
        var skipped = checkpoint?.Skipped ?? 0;
        var processed = checkpoint?.Processed ?? 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var animationInfoRepository = scope.ServiceProvider
                .GetRequiredService<IAnimationInfoRepository>();
            var fileMappingRepository = scope.ServiceProvider
                .GetRequiredService<IFileMappingRepository>();
            var fileMapper = scope.ServiceProvider.GetRequiredService<IFileMapper>();

            var batch = await animationInfoRepository.GetDownloadedMigrationBatchAsync(
                checkpoint?.PublishTime,
                checkpoint?.Id,
                PageSize,
                cancellationToken);
            if (batch.Count == 0) break;

            foreach (var info in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (info.FileStore is null || info.StorePath is null)
                {
                    skipped++;
                }
                // Inference calls MapDownloadAsync after filling in metadata. Mapping
                // here would race it and could replace the canonical mapping with /unknown.
                else if (!info.IsAiProcessed && info.AiRetryCount < InferenceMaxRetryCount)
                {
                    skipped++;
                }
                else if (await fileMappingRepository.ExistsForAnimationInfoAsync(
                             info.Id,
                             cancellationToken))
                {
                    skipped++;
                }
                else
                {
                    var mapped = await fileMapper.MapDownloadAsync(info.Id, cancellationToken);
                    if (!mapped)
                        throw new InvalidOperationException(
                            $"File mapping migration did not produce mappings for AnimationInfo {info.Id}.");
                    migrated++;
                }

                processed++;
                checkpoint = new FileMappingCheckpoint(
                    info.PublishTime,
                    info.Id,
                    processed,
                    migrated,
                    skipped);
            }

            // Mapping writes are idempotent and committed before this checkpoint.
            // A crash between them safely replays at most one batch.
            await context.SaveCheckpointAsync(
                JsonSerializer.Serialize(checkpoint),
                cancellationToken);
            LogCheckpoint(logger, processed, migrated, skipped);
        }

        LogSummary(logger, migrated, skipped);
    }

    private static FileMappingCheckpoint? ParseCheckpoint(string? value)
    {
        if (value is null) return null;
        try
        {
            return JsonSerializer.Deserialize<FileMappingCheckpoint>(value)
                   ?? throw new JsonException("Checkpoint was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The MigrateFileMappings checkpoint is invalid; inspect the migration state before retrying.",
                exception);
        }
    }

    private sealed record FileMappingCheckpoint(
        DateTimeOffset PublishTime,
        Guid Id,
        int Processed,
        int Migrated,
        int Skipped);

    [LoggerMessage(Level = LogLevel.Information, Message = "File mapping migration checkpoint: {Processed} processed, {Migrated} migrated, {Skipped} skipped")]
    private static partial void LogCheckpoint(
        ILogger logger,
        int processed,
        int migrated,
        int skipped);

    [LoggerMessage(Level = LogLevel.Information, Message = "File mapping migration complete: {Migrated} migrated, {Skipped} skipped")]
    private static partial void LogSummary(ILogger logger, int migrated, int skipped);
}
