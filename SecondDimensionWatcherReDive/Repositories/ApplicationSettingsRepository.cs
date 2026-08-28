using Microsoft.EntityFrameworkCore;
using Npgsql;
using DataApplicationSettings = SecondDimensionWatcherReDive.Framework.DataRepository.ApplicationSettings;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class ApplicationSettingsRepository(Models.ApplicationContext context)
    : Framework.DataRepository.IApplicationSettingsRepository
{
    public async Task<DataApplicationSettings?> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await context.ApplicationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                settings => settings.Id == Models.ApplicationSettings.SingletonId,
                cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<DataApplicationSettings?> TrySaveAsync(
        string valuesJson,
        string? protectedSecrets,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var entity = await context.ApplicationSettings
            .SingleOrDefaultAsync(
                settings => settings.Id == Models.ApplicationSettings.SingletonId,
                cancellationToken);

        if (entity is null)
        {
            if (expectedRevision != 0)
                return null;

            entity = new Models.ApplicationSettings
            {
                ValuesJson = valuesJson,
                ProtectedSecrets = protectedSecrets,
                Revision = 1,
                UpdatedAt = updatedAt
            };
            context.ApplicationSettings.Add(entity);
        }
        else
        {
            if (entity.Revision != expectedRevision)
                return null;

            context.Entry(entity).Property(settings => settings.Revision).OriginalValue = expectedRevision;
            entity.ValuesJson = valuesJson;
            entity.ProtectedSecrets = protectedSecrets;
            entity.Revision = checked(expectedRevision + 1);
            entity.UpdatedAt = updatedAt;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return entity.ToRecord();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return null;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            context.ChangeTracker.Clear();
            return null;
        }
    }
}
