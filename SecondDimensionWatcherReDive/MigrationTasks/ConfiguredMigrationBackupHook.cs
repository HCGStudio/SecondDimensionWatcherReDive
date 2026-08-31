using System.Diagnostics;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.MigrationTasks;

public sealed partial class ConfiguredMigrationBackupHook(
    IOptions<MigrationOptions> options,
    ILogger<ConfiguredMigrationBackupHook> logger) : IMigrationBackupHook
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.BackupExecutable))
        {
            if (configured.RequireBackup)
                throw new InvalidOperationException(
                    "Migration:RequireBackup is enabled but Migration:BackupExecutable is not configured.");
            return;
        }

        if (configured.BackupTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Migration:BackupTimeout must be positive.");

        var startInfo = new ProcessStartInfo
        {
            FileName = configured.BackupExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in configured.BackupArguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        LogStarting(logger, configured.BackupExecutable);
        if (!process.Start())
            throw new InvalidOperationException(
                $"Could not start migration backup hook '{configured.BackupExecutable}'.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(configured.BackupTimeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Migration backup hook exited with code {process.ExitCode}: {Summarize(error)}");

            LogCompleted(logger);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Migration backup hook exceeded its timeout of {configured.BackupTimeout}.",
                exception);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited between HasExited and Kill.
        }
    }

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 1024 ? normalized : normalized[..1024];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Running migration backup hook {Executable}")]
    private static partial void LogStarting(ILogger logger, string executable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration backup hook completed")]
    private static partial void LogCompleted(ILogger logger);
}
