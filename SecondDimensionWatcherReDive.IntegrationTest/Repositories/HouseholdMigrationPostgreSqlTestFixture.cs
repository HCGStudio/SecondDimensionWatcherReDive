using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

internal sealed record HouseholdMigrationSnapshot(
    int UserCount,
    Guid? UserId,
    string? Username,
    UserRole? Role,
    Guid? ProfileId,
    string? ProfileName,
    Guid? ProgressProfileId,
    double? PositionSeconds,
    Guid? PreferenceProfileId,
    string? SubtitleLanguage,
    Guid? ConversationProfileId,
    string? ConversationTitle,
    Guid? DeviceUserId,
    string? DeviceScope,
    string? DeviceRoot,
    DateTimeOffset? DeviceExpiresAt,
    DateTimeOffset? DeviceRevokedAt);

internal sealed record HouseholdMigrationDownSnapshot(
    int PlaybackCount,
    int PreferenceCount,
    int ConversationCount,
    int DeviceTokenCount,
    bool UsersTableExists,
    bool ProfilesTableExists);

internal sealed record CleanRegistrationSnapshot(
    Guid PersistedProfileId,
    Guid IssuedProfileId,
    bool SessionIsActive,
    int UserCount);

internal sealed record ConcurrentAdminUpdateSnapshot(
    IReadOnlyList<UpdateUserAccessResult> Results,
    int EnabledAdministratorCount);

internal sealed record ProfileIsolationSnapshot(
    double? FirstPosition,
    double? SecondPosition,
    string? FirstSubtitleLanguage,
    string? SecondSubtitleLanguage,
    int FirstConversationCount,
    int SecondConversationCount,
    bool CrossProfileConversationHidden,
    double? FirstContinuePosition,
    double? SecondContinuePosition);

internal sealed record SessionLifecycleSnapshot(
    bool WrongPinRejected,
    bool CorrectPinRotated,
    bool OldAccessRejected,
    bool NewProfileClaimIsActive,
    bool OldRefreshReplayRejected,
    bool LogoutRejectedAccess,
    bool LogoutRejectedRefresh,
    bool AdministratorRevokeRejectedAccess,
    bool AdministratorRevokeRejectedRefresh,
    int ConcurrentRefreshSuccessCount);

internal sealed record ConcurrentRegistrationSnapshot(
    int SuccessCount,
    int ConflictCount,
    int UserCount,
    int SessionCount);

internal sealed record DowngradeSafetySnapshot(
    bool Rejected,
    bool CurrentMigrationStillApplied,
    int UserCount,
    int ProfileCount,
    int ProgressCount,
    int PreferenceCount,
    int DeviceTokenCount);

/// <summary>
/// PostgreSQL-only migration fixture. It lives in the integration-test repository boundary so EF entities and
/// ApplicationContext never escape the permitted data-access boundary.
/// </summary>
internal sealed class HouseholdMigrationPostgreSqlTestFixture(string connectionString)
{
    internal const string PreviousMigration = "20260828164158_AddApplicationSettings";

    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString, options => options.EnableRetryOnFailure())
            .Options;

    public async Task RecreateAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.EnsureDeletedAsync(cancellationToken);
    }

    public async Task SeedLegacyAndUpgradeAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(PreviousMigration, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var animationInfoId = Guid.Parse("41000000-0000-0000-0000-000000000001");
        context.AnimationInfo.Add(new Models.AnimationInfo
        {
            Id = animationInfoId,
            Title = "legacy episode",
            Description = string.Empty,
            PublishTime = now,
            DownloadUrl = string.Empty,
            DownloadType = string.Empty,
            CachedDownloadData = [],
            AdditionalDownloadInfo = string.Empty,
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/legacy"
        });
        await context.SaveChangesAsync(cancellationToken);

        var progressId = Guid.Parse("42000000-0000-0000-0000-000000000001");
        var conversationId = Guid.Parse("43000000-0000-0000-0000-000000000001");
        var tokenId = Guid.Parse("44000000-0000-0000-0000-000000000001");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "PlaybackProgresses"
                 ("Id", "UserId", "AnimationInfoId", "VirtualPath", "PositionSeconds",
                  "DurationSeconds", "IsWatched", "UpdatedAt", "WatchedAt")
             VALUES
                 ({progressId}, {Guid.Empty}, {animationInfoId}, {'/' + "legacy/episode.mkv"},
                  {123d}, {1500d}, {false}, {now}, {null});
             """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "PlaybackPreferences"
                 ("UserId", "SubtitleLanguage", "SubtitleTrackLabel", "AudioLanguage",
                  "AudioTrackLabel", "AutoPlayNext", "UpdatedAt")
             VALUES ({Guid.Empty}, {"zh-Hans"}, {null}, {"ja"}, {null}, {true}, {now});
             """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "ChatConversations" ("Id", "Title", "CreatedAt", "UpdatedAt")
             VALUES ({conversationId}, {"legacy chat"}, {now}, {now});
             """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "WebDavTokens"
                 ("Id", "Username", "TokenHash", "Description", "CreatedAt")
             VALUES ({tokenId}, {"legacy-device"}, {"legacy-hash"}, {"old client"}, {now});
             """,
            cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task<HouseholdMigrationSnapshot> InspectAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var profile = await context.Profiles.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var progress = await context.PlaybackProgresses.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var preference = await context.PlaybackPreferences.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var conversation = await context.ChatConversations.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var token = await context.WebDavTokens.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return new HouseholdMigrationSnapshot(
            await context.Users.CountAsync(cancellationToken),
            user?.Id,
            user?.Username,
            user?.Role,
            profile?.Id,
            profile?.Name,
            progress?.UserId,
            progress?.PositionSeconds,
            preference?.UserId,
            preference?.SubtitleLanguage,
            conversation?.ProfileId,
            conversation?.Title,
            token?.UserId,
            token?.Scope,
            token?.VirtualRoot,
            token?.ExpiresAt,
            token?.RevokedAt);
    }

    public async Task MigrateCleanDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task UpgradeAsync(CancellationToken cancellationToken) =>
        MigrateCleanDatabaseAsync(cancellationToken);

    public async Task<int> GetUserCountAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.Users.CountAsync(cancellationToken);
    }

    public async Task<CleanRegistrationSnapshot> RegisterAndCreateSessionAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new IdentityRepository(context);
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            IdentityDefaults.UserId,
            IdentityDefaults.Username,
            BCrypt.Net.BCrypt.HashPassword("integration-password"),
            UserRole.Admin,
            false,
            now,
            now);
        var profile = new UserProfile(
            IdentityDefaults.ProfileId,
            user.Id,
            IdentityDefaults.ProfileName,
            null,
            null,
            true,
            now,
            now);
        await repository.CreateUserWithProfileAsync(user, profile, cancellationToken);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSecret"] = "postgres-integration-secret-long-enough-123456"
            })
            .Build();
        var issuer = new SessionTokenIssuer(configuration, repository);
        var issued = await issuer.CreateSessionAsync(
            user, profile, "integration", cancellationToken);
        var persistedProfileId = await context.Profiles
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken);
        var active = await repository.GetAuthenticatedSessionAsync(
            issued.SessionId, DateTimeOffset.UtcNow, cancellationToken);
        var secondUser = new UserAccount(
            Guid.NewGuid(),
            "family-member",
            BCrypt.Net.BCrypt.HashPassword("member-password"),
            UserRole.Member,
            false,
            now,
            now);
        var secondProfile = new UserProfile(
            Guid.NewGuid(),
            secondUser.Id,
            "Member Home",
            null,
            null,
            true,
            now,
            now);
        await repository.CreateUserWithProfileAsync(
            secondUser, secondProfile, cancellationToken);
        return new CleanRegistrationSnapshot(
            persistedProfileId,
            issued.ProfileId,
            active is not null,
            await context.Users.CountAsync(cancellationToken));
    }

    public async Task<ConcurrentAdminUpdateSnapshot> DemoteTwoAdminsConcurrentlyAsync(
        CancellationToken cancellationToken)
    {
        var firstUserId = Guid.Parse("51000000-0000-0000-0000-000000000001");
        var secondUserId = Guid.Parse("51000000-0000-0000-0000-000000000002");
        await using (var seedContext = new Models.ApplicationContext(_contextOptions))
        {
            var now = DateTimeOffset.UtcNow;
            seedContext.Users.AddRange(
                UserEntity(firstUserId, "first-admin", now),
                UserEntity(secondUserId, "second-admin", now));
            seedContext.Profiles.AddRange(
                ProfileEntity(Guid.Parse("52000000-0000-0000-0000-000000000001"), firstUserId, now),
                ProfileEntity(Guid.Parse("52000000-0000-0000-0000-000000000002"), secondUserId, now));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        async Task<UpdateUserAccessResult> DemoteAsync(Guid id)
        {
            await using var updateContext = new Models.ApplicationContext(_contextOptions);
            var repository = new IdentityRepository(updateContext);
            return await repository.UpdateUserAccessAsync(
                id,
                UserRole.Member,
                false,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        var results = await Task.WhenAll(
            DemoteAsync(firstUserId),
            DemoteAsync(secondUserId));
        await using var inspectContext = new Models.ApplicationContext(_contextOptions);
        var enabledAdmins = await inspectContext.Users.CountAsync(
            user => user.Role == UserRole.Admin && !user.IsDisabled,
            cancellationToken);
        return new ConcurrentAdminUpdateSnapshot(results, enabledAdmins);
    }

    public async Task<SessionLifecycleSnapshot> ExerciseSessionLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var configuration = CreateJwtConfiguration();
        var validationParameters = CreateTokenValidationParameters(configuration);
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            Guid.Parse("61000000-0000-0000-0000-000000000001"),
            "session-user",
            BCrypt.Net.BCrypt.HashPassword("session-password"),
            UserRole.Admin,
            false,
            now,
            now);
        var firstProfile = new UserProfile(
            Guid.Parse("62000000-0000-0000-0000-000000000001"),
            user.Id,
            "First",
            null,
            null,
            true,
            now,
            now);
        var secondProfile = new UserProfile(
            Guid.Parse("62000000-0000-0000-0000-000000000002"),
            user.Id,
            "Second",
            null,
            BCrypt.Net.BCrypt.HashPassword("2468"),
            false,
            now,
            now);

        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new IdentityRepository(context);
        await repository.CreateUserWithProfileAsync(user, firstProfile, cancellationToken);
        await repository.AddProfileAsync(secondProfile, cancellationToken);
        var issuer = new SessionTokenIssuer(configuration, repository);
        var initial = await issuer.CreateSessionAsync(
            user, firstProfile, "profile-switch-test", cancellationToken);
        var oldPrincipal = ValidateToken(initial.AccessToken, validationParameters);
        var authorization = new Mock<IAuthorizationService>();
        var accounts = CreateAccountsController(
            repository, issuer, authorization.Object, oldPrincipal);

        var wrongPin = await accounts.SwitchProfile(
            new Controllers.External.SwitchProfileRequest(
                secondProfile.Id, "0000", initial.RefreshToken),
            cancellationToken);
        var afterWrongPin = await repository.GetAuthenticatedSessionAsync(
            initial.SessionId, DateTimeOffset.UtcNow, cancellationToken);
        var wrongPinRejected = wrongPin is UnauthorizedResult
                               && afterWrongPin?.Profile.Id == firstProfile.Id;

        var correctPin = await accounts.SwitchProfile(
            new Controllers.External.SwitchProfileRequest(
                secondProfile.Id, "2468", initial.RefreshToken),
            cancellationToken);
        var rotated = (correctPin as OkObjectResult)?.Value
                      as Controllers.External.LoginResult;
        if (rotated?.Token is null || rotated.RefreshToken is null)
            throw new InvalidOperationException("Profile switch did not issue tokens.");
        var newPrincipal = ValidateToken(rotated.Token, validationParameters);
        var correctPinRotated = rotated.ProfileId == secondProfile.Id
                                && rotated.RefreshToken != initial.RefreshToken;
        var oldAccessRejected = !await IsPrincipalCurrentAsync(
            oldPrincipal, repository, cancellationToken);
        var newProfileClaimIsActive = newPrincipal.TryGetProfileId(out var newProfileId)
                                      && newProfileId == secondProfile.Id
                                      && await IsPrincipalCurrentAsync(
                                          newPrincipal, repository, cancellationToken);

        var auth = CreateAuthController(
            configuration, validationParameters, repository, issuer);
        var oldReplay = await auth.Refresh(
            new Controllers.External.AuthRequest(
                initial.AccessToken, initial.RefreshToken),
            cancellationToken);
        var oldRefreshReplayRejected = IsUnauthorized(oldReplay);

        auth.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = newPrincipal }
        };
        await auth.Logout(cancellationToken);
        var logoutRejectedAccess = !await IsPrincipalCurrentAsync(
            newPrincipal, repository, cancellationToken);
        var logoutRefresh = await auth.Refresh(
            new Controllers.External.AuthRequest(
                rotated.Token, rotated.RefreshToken),
            cancellationToken);
        var logoutRejectedRefresh = IsUnauthorized(logoutRefresh);

        var adminRevoked = await issuer.CreateSessionAsync(
            user, secondProfile, "administrator-revoke-test", cancellationToken);
        var adminRevokedPrincipal = ValidateToken(
            adminRevoked.AccessToken, validationParameters);
        var adminAccounts = CreateAccountsController(
            repository, issuer, authorization.Object, newPrincipal);
        await adminAccounts.RevokeAnySession(
            adminRevoked.SessionId, cancellationToken);
        var administratorRevokeRejectedAccess = !await IsPrincipalCurrentAsync(
            adminRevokedPrincipal, repository, cancellationToken);
        var administratorRevokeRefresh = await auth.Refresh(
            new Controllers.External.AuthRequest(
                adminRevoked.AccessToken, adminRevoked.RefreshToken),
            cancellationToken);
        var administratorRevokeRejectedRefresh = IsUnauthorized(
            administratorRevokeRefresh);

        var concurrent = await issuer.CreateSessionAsync(
            user, secondProfile, "concurrent-refresh-test", cancellationToken);
        async Task<bool> RefreshConcurrentlyAsync()
        {
            await using var refreshContext = new Models.ApplicationContext(_contextOptions);
            var refreshRepository = new IdentityRepository(refreshContext);
            var refreshIssuer = new SessionTokenIssuer(configuration, refreshRepository);
            var refreshController = CreateAuthController(
                configuration,
                validationParameters,
                refreshRepository,
                refreshIssuer);
            var result = await refreshController.Refresh(
                new Controllers.External.AuthRequest(
                    concurrent.AccessToken, concurrent.RefreshToken),
                cancellationToken);
            return result is OkObjectResult;
        }

        var concurrentResults = await Task.WhenAll(
            RefreshConcurrentlyAsync(),
            RefreshConcurrentlyAsync());
        return new SessionLifecycleSnapshot(
            wrongPinRejected,
            correctPinRotated,
            oldAccessRejected,
            newProfileClaimIsActive,
            oldRefreshReplayRejected,
            logoutRejectedAccess,
            logoutRejectedRefresh,
            administratorRevokeRejectedAccess,
            administratorRevokeRejectedRefresh,
            concurrentResults.Count(result => result));
    }

    public async Task<ConcurrentRegistrationSnapshot> RegisterConcurrentlyAsync(
        CancellationToken cancellationToken)
    {
        var configuration = CreateJwtConfiguration();
        var validationParameters = CreateTokenValidationParameters(configuration);
        async Task<IActionResult> RegisterAsync()
        {
            await using var registerContext = new Models.ApplicationContext(_contextOptions);
            var repository = new IdentityRepository(registerContext);
            var issuer = new SessionTokenIssuer(configuration, repository);
            var controller = CreateAuthController(
                configuration, validationParameters, repository, issuer);
            return await controller.Register(
                new Controllers.External.LoginData(
                    "concurrent-password",
                    IdentityDefaults.Username,
                    "concurrent-registration",
                    IdentityDefaults.ProfileName),
                cancellationToken);
        }

        var results = await Task.WhenAll(RegisterAsync(), RegisterAsync());
        await using var inspect = new Models.ApplicationContext(_contextOptions);
        return new ConcurrentRegistrationSnapshot(
            results.Count(result => result is OkObjectResult),
            results.Count(result => result is ConflictResult),
            await inspect.Users.CountAsync(cancellationToken),
            await inspect.LoginSessions.CountAsync(cancellationToken));
    }

    public async Task SeedUnsafeScopedDeviceTokenAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = new Models.ApplicationContext(_contextOptions);
        context.Users.Add(UserEntity(
            IdentityDefaults.UserId, IdentityDefaults.Username, now));
        context.Users.Local.Single().PasswordHash = null;
        context.Profiles.Add(ProfileEntity(
            IdentityDefaults.ProfileId, IdentityDefaults.UserId, now));
        context.WebDavTokens.Add(new Models.WebDavToken
        {
            Id = Guid.NewGuid(),
            UserId = IdentityDefaults.UserId,
            Username = "scoped-device",
            TokenHash = "hash",
            CreatedAt = now,
            Scope = "read",
            VirtualRoot = "/Anime",
            ExpiresAt = now.AddDays(30)
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DowngradeSafetySnapshot> AttemptUnsafeDowngradeAsync(
        CancellationToken cancellationToken)
    {
        var rejected = false;
        try
        {
            await using var downContext = new Models.ApplicationContext(_contextOptions);
            await downContext.Database.MigrateAsync(
                PreviousMigration, cancellationToken);
        }
        catch (Exception exception) when (IsSafetyRejection(exception))
        {
            rejected = true;
        }

        await using var inspect = new Models.ApplicationContext(_contextOptions);
        var applied = await inspect.Database.GetAppliedMigrationsAsync(cancellationToken);
        return new DowngradeSafetySnapshot(
            rejected,
            applied.Contains("20260829155550_AddHouseholdIdentityAndAccessScopes"),
            await inspect.Users.CountAsync(cancellationToken),
            await inspect.Profiles.CountAsync(cancellationToken),
            await inspect.PlaybackProgresses.CountAsync(cancellationToken),
            await inspect.PlaybackPreferences.CountAsync(cancellationToken),
            await inspect.WebDavTokens.CountAsync(cancellationToken));
    }

    public async Task<ProfileIsolationSnapshot> ExerciseProfileIsolationAsync(
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse("71000000-0000-0000-0000-000000000001");
        var firstProfileId = Guid.Parse("72000000-0000-0000-0000-000000000001");
        var secondProfileId = Guid.Parse("72000000-0000-0000-0000-000000000002");
        var animationInfoId = Guid.Parse("73000000-0000-0000-0000-000000000001");
        const string VirtualPath = "/unknown/episode.mkv";
        var now = DateTimeOffset.UtcNow;
        await using var context = new Models.ApplicationContext(_contextOptions);
        context.Users.Add(new Models.UserAccount
        {
            Id = userId,
            Username = "family",
            PasswordHash = "hash",
            Role = UserRole.Member,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Profiles.AddRange(
            ProfileEntity(firstProfileId, userId, now, "First"),
            ProfileEntity(secondProfileId, userId, now, "Second"));
        context.AnimationInfo.Add(new Models.AnimationInfo
        {
            Id = animationInfoId,
            Title = "profile isolation",
            Description = string.Empty,
            PublishTime = now,
            DownloadUrl = string.Empty,
            DownloadType = string.Empty,
            CachedDownloadData = [],
            AdditionalDownloadInfo = string.Empty,
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/profile-isolation"
        });
        context.FileMappings.Add(new Models.FileMapping
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = animationInfoId,
            VirtualPath = VirtualPath,
            PhysicalPath = "/disk/episode.mkv",
            FileStore = "local"
        });
        await context.SaveChangesAsync(cancellationToken);

        var playback = new PlaybackRepository(context, _contextOptions);
        await playback.UpsertProgressAsync(
            firstProfileId, animationInfoId, VirtualPath,
            10, 100, false, now, cancellationToken);
        await playback.UpsertProgressAsync(
            secondProfileId, animationInfoId, VirtualPath,
            70, 100, false, now.AddSeconds(1), cancellationToken);
        await playback.UpsertPreferencesAsync(new PlaybackPreferences(
            firstProfileId, "zh-Hans", null, "ja", null, true, now), cancellationToken);
        await playback.UpsertPreferencesAsync(new PlaybackPreferences(
            secondProfileId, "en", null, "en", null, false, now), cancellationToken);

        var chat = new ChatRepository(context);
        var firstConversation = await chat.CreateConversationAsync(
            firstProfileId, "first", cancellationToken);
        await chat.CreateConversationAsync(secondProfileId, "second", cancellationToken);
        var firstProgress = await playback.FindProgressAsync(
            firstProfileId, animationInfoId, VirtualPath, cancellationToken);
        var secondProgress = await playback.FindProgressAsync(
            secondProfileId, animationInfoId, VirtualPath, cancellationToken);
        var firstPreferences = await playback.GetPreferencesAsync(
            firstProfileId, cancellationToken);
        var secondPreferences = await playback.GetPreferencesAsync(
            secondProfileId, cancellationToken);
        var firstConversations = await chat.GetConversationsAsync(
            firstProfileId, cancellationToken);
        var secondConversations = await chat.GetConversationsAsync(
            secondProfileId, cancellationToken);
        var crossProfile = await chat.GetConversationWithMessagesAsync(
            firstConversation.Id, secondProfileId, cancellationToken);
        var firstContinue = await playback.GetContinueWatchingAsync(
            firstProfileId, 10, cancellationToken);
        var secondContinue = await playback.GetContinueWatchingAsync(
            secondProfileId, 10, cancellationToken);
        return new ProfileIsolationSnapshot(
            firstProgress?.PositionSeconds,
            secondProgress?.PositionSeconds,
            firstPreferences.SubtitleLanguage,
            secondPreferences.SubtitleLanguage,
            firstConversations.Count,
            secondConversations.Count,
            crossProfile is null,
            firstContinue.SingleOrDefault()?.Progress.PositionSeconds,
            secondContinue.SingleOrDefault()?.Progress.PositionSeconds);
    }

    private static Models.UserAccount UserEntity(
        Guid id,
        string username,
        DateTimeOffset now) => new()
    {
        Id = id,
        Username = username,
        PasswordHash = "hash",
        Role = UserRole.Admin,
        IsDisabled = false,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static IConfiguration CreateJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSecret"] = "postgres-integration-secret-long-enough-123456"
            })
            .Build();

    private static TokenValidationParameters CreateTokenValidationParameters(
        IConfiguration configuration) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.ASCII.GetBytes(configuration["JwtSecret"]!)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        RequireExpirationTime = true
    };

    private static ClaimsPrincipal ValidateToken(
        string token,
        TokenValidationParameters validationParameters) =>
        new JwtSecurityTokenHandler().ValidateToken(
            token, validationParameters, out _);

    private static AccountsController CreateAccountsController(
        IIdentityRepository repository,
        SessionTokenIssuer issuer,
        IAuthorizationService authorizationService,
        ClaimsPrincipal principal) => new(repository, issuer, authorizationService)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        }
    };

    private static AuthController CreateAuthController(
        IConfiguration configuration,
        TokenValidationParameters validationParameters,
        IIdentityRepository repository,
        SessionTokenIssuer issuer) => new(
        configuration,
        validationParameters,
        repository,
        issuer,
        NullLogger<AuthController>.Instance);

    private static async Task<bool> IsPrincipalCurrentAsync(
        ClaimsPrincipal principal,
        IIdentityRepository repository,
        CancellationToken cancellationToken)
    {
        if (!principal.TryGetUserId(out var userId)
            || !principal.TryGetProfileId(out var profileId)
            || !principal.TryGetSessionId(out var sessionId))
            return false;
        var authenticated = await repository.GetAuthenticatedSessionAsync(
            sessionId, DateTimeOffset.UtcNow, cancellationToken);
        return authenticated is not null
               && authenticated.User.Id == userId
               && authenticated.Profile.Id == profileId
               && principal.IsInRole(authenticated.User.Role.ToString());
    }

    private static bool IsUnauthorized(IActionResult result) =>
        result is UnauthorizedResult or UnauthorizedObjectResult;

    private static bool IsSafetyRejection(Exception exception) =>
        exception is PostgresException { SqlState: "P0001" }
        || exception.InnerException is not null && IsSafetyRejection(exception.InnerException);

    private static Models.UserProfile ProfileEntity(
        Guid id,
        Guid userId,
        DateTimeOffset now,
        string name = "Home") => new()
    {
        Id = id,
        UserId = userId,
        Name = name,
        IsDefault = true,
        CreatedAt = now,
        UpdatedAt = now
    };

    public async Task<HouseholdMigrationDownSnapshot> MigrateDownAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(PreviousMigration, cancellationToken);
        var usersExists = await TableExistsAsync(context, "Users", cancellationToken);
        var profilesExists = await TableExistsAsync(context, "Profiles", cancellationToken);
        return new HouseholdMigrationDownSnapshot(
            await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM \"PlaybackProgresses\"")
                .SingleAsync(cancellationToken),
            await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM \"PlaybackPreferences\"")
                .SingleAsync(cancellationToken),
            await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM \"ChatConversations\"")
                .SingleAsync(cancellationToken),
            await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM \"WebDavTokens\"")
                .SingleAsync(cancellationToken),
            usersExists,
            profilesExists);
    }

    private static async Task<bool> TableExistsAsync(
        Models.ApplicationContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = @name)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
