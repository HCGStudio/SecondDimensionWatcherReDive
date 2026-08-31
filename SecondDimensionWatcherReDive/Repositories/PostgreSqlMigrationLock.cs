using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
///     Holds a session-level PostgreSQL advisory lock on a dedicated physical
///     connection. The connection itself is the crash-safe lease.
/// </summary>
public sealed partial class PostgreSqlMigrationLock(
    Models.ApplicationContext context,
    ILogger<PostgreSqlMigrationLock> logger) : IMigrationLock
{
    // "SDWMIGR1" encoded as a signed 64-bit advisory-lock namespace.
    private const long LockKey = 0x5344574D49475231;

    public async Task<IMigrationLockLease> AcquireAsync(
        CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetConnectionString()
                               ?? throw new InvalidOperationException(
                                   "A database connection string is required for migrations.");
        var leaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Multiplexing = false
        }.ConnectionString;
        var connection = new NpgsqlConnection(leaseConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            LogWaiting(logger);
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(@key)",
                connection)
            {
                // The overall Migration:Timeout owns this wait; do not let Npgsql's
                // shorter command timeout make replicas fail while another migrates.
                CommandTimeout = 0
            };
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
            LogAcquired(logger);
            return new Lease(connection, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(
        NpgsqlConnection connection,
        ILogger logger) : IMigrationLockLease
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@key)",
                    connection)
                {
                    CommandTimeout = 5
                };
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
                LogReleased(logger);
            }
            catch (Exception exception)
            {
                // Closing the physical connection below still releases the lock.
                LogReleaseFailed(logger, exception);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for the PostgreSQL migration lease")]
    private static partial void LogWaiting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Acquired the PostgreSQL migration lease")]
    private static partial void LogAcquired(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Released the PostgreSQL migration lease")]
    private static partial void LogReleased(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not explicitly release the migration lease; closing its connection")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);
}
