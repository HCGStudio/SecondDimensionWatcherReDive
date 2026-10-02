using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using CredentialEntity = SecondDimensionWatcherReDive.Models.PasskeyCredential;
using CeremonyEntity = SecondDimensionWatcherReDive.Models.PasskeyCeremony;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class PasskeyRepository(Models.ApplicationContext context) : IPasskeyRepository
{
    public async Task<IReadOnlyList<PasskeyCredential>> GetCredentialsAsync(
        Guid userId, string? relyingPartyId, CancellationToken cancellationToken) =>
        (await context.Set<CredentialEntity>().AsNoTracking()
            .Where(value => value.UserId == userId
                            && (relyingPartyId == null || value.RelyingPartyId == relyingPartyId))
            .OrderBy(value => value.CreatedAt).ToListAsync(cancellationToken)).Select(ToRecord).ToList();

    public async Task<PasskeyCredential?> FindCredentialAsync(
        byte[] credentialId, CancellationToken cancellationToken)
    {
        var entity = await context.Set<CredentialEntity>().AsNoTracking()
            .FirstOrDefaultAsync(value => value.CredentialId == credentialId, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<bool> AddCredentialAsync(PasskeyCredential credential, Guid sessionId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Serialize account credential changes, including password removal, across replicas.
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {credential.UserId} FOR UPDATE", cancellationToken);
            if (!await context.LoginSessions.AnyAsync(session => session.Id == sessionId
                    && session.UserId == credential.UserId && session.RevokedAt == null
                    && session.ExpiresAt > now && !session.User.IsDisabled
                    && session.AuthenticatedAt >= now.AddMinutes(-5), cancellationToken)
                || await context.Set<CredentialEntity>().CountAsync(
                    value => value.UserId == credential.UserId, cancellationToken) >= 20)
                return false;
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PasskeyCredentials"
                    ("Id", "UserId", "CredentialId", "PublicKey", "SignCount", "RelyingPartyId", "Name",
                     "IsBackupEligible", "IsBackedUp", "CreatedAt", "LastUsedAt")
                VALUES ({credential.Id}, {credential.UserId}, {credential.CredentialId}, {credential.PublicKey},
                    {credential.SignCount}, {credential.RelyingPartyId}, {credential.Name},
                    {credential.IsBackupEligible}, {credential.IsBackedUp}, {credential.CreatedAt}, {credential.LastUsedAt})
                ON CONFLICT DO NOTHING
                """, cancellationToken);
            var succeeded = inserted == 1 || await context.Set<CredentialEntity>().AnyAsync(
                value => value.Id == credential.Id && value.UserId == credential.UserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return succeeded;
        });
    }

    public async Task AddCeremonyAsync(PasskeyCeremony ceremony, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await context.Set<CeremonyEntity>().Where(value => value.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
        context.Set<CeremonyEntity>().Add(new CeremonyEntity
        {
            Id = ceremony.Id, UserId = ceremony.UserId, SessionId = ceremony.SessionId,
            Purpose = ceremony.Purpose, Origin = ceremony.Origin, RelyingPartyId = ceremony.RelyingPartyId,
            BrowserBindingHash = ceremony.BrowserBindingHash, OptionsJson = ceremony.OptionsJson,
            ExpiresAt = ceremony.ExpiresAt
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PasskeyCeremony?> ConsumeCeremonyAsync(Guid id, string purpose, Guid? sessionId,
        string origin, string browserBindingHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var query = context.Set<CeremonyEntity>().Where(value => value.Id == id
            && value.Purpose == purpose && value.SessionId == sessionId && value.Origin == origin
            && value.BrowserBindingHash == browserBindingHash && value.ExpiresAt > now);
        var entity = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        // Only one contender can delete the challenge. Even invalid responses consume it.
        if (entity is null || await query.ExecuteDeleteAsync(cancellationToken) != 1) return null;
        return new PasskeyCeremony(entity.Id, entity.UserId, entity.SessionId, entity.Purpose,
            entity.Origin, entity.RelyingPartyId, entity.BrowserBindingHash, entity.OptionsJson, entity.ExpiresAt);
    }

    public async Task<bool> RecordAssertionAsync(PasskeyCredential credential, long signCount,
        bool isBackedUp, DateTimeOffset now, CancellationToken cancellationToken) =>
        await context.Set<CredentialEntity>()
            .Where(value => value.Id == credential.Id && value.SignCount == credential.SignCount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.SignCount, signCount)
                .SetProperty(value => value.IsBackedUp, isBackedUp)
                .SetProperty(value => value.LastUsedAt, now), cancellationToken) == 1;

    public async Task<bool> RemovePasswordAsync(Guid userId, Guid sessionId, Guid credentialId,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);
            if (!await context.Set<CredentialEntity>().AnyAsync(value => value.Id == credentialId
                    && value.UserId == userId, cancellationToken)
                || !await context.LoginSessions.AnyAsync(session => session.Id == sessionId
                    && session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now
                    && !session.User.IsDisabled, cancellationToken)) return false;
            await context.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.PasswordHash, (string?)null)
                .SetProperty(user => user.PasswordRemoved, true)
                .SetProperty(user => user.UpdatedAt, now), cancellationToken);
            await context.LoginSessions.Where(session => session.UserId == userId
                    && session.Id != sessionId && session.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

    private static PasskeyCredential ToRecord(CredentialEntity value) => new(
        value.Id, value.UserId, value.CredentialId, value.PublicKey, value.SignCount,
        value.RelyingPartyId, value.Name, value.IsBackupEligible, value.IsBackedUp,
        value.CreatedAt, value.LastUsedAt);
}
