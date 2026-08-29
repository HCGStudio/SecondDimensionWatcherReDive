using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
using Testcontainers.PostgreSql;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

[TestClass]
public sealed class HouseholdMigrationPostgreSqlTests
{
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_identity_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static HouseholdMigrationPostgreSqlTestFixture Fixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new HouseholdMigrationPostgreSqlTestFixture(Database.GetConnectionString());
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetAsync() => await Fixture.RecreateAsync(CancellationToken.None);

    [TestMethod]
    public async Task LegacyHistory_IsAssignedToDefaultProfile_BeforeForeignKeysAreAdded()
    {
        await Fixture.SeedLegacyAndUpgradeAsync(CancellationToken.None);

        var snapshot = await Fixture.InspectAsync(CancellationToken.None);
        Assert.AreEqual(1, snapshot.UserCount);
        Assert.AreEqual(IdentityDefaults.UserId, snapshot.UserId);
        Assert.AreEqual("admin", snapshot.Username);
        Assert.AreEqual(UserRole.Admin, snapshot.Role);
        Assert.AreEqual(IdentityDefaults.ProfileId, snapshot.ProfileId);
        Assert.AreEqual("Home", snapshot.ProfileName);
        Assert.AreEqual(IdentityDefaults.ProfileId, snapshot.ProgressProfileId);
        Assert.AreEqual(123d, snapshot.PositionSeconds);
        Assert.AreEqual(IdentityDefaults.ProfileId, snapshot.PreferenceProfileId);
        Assert.AreEqual("zh-Hans", snapshot.SubtitleLanguage);
        Assert.AreEqual(IdentityDefaults.ProfileId, snapshot.ConversationProfileId);
        Assert.AreEqual("legacy chat", snapshot.ConversationTitle);
        Assert.AreEqual(IdentityDefaults.UserId, snapshot.DeviceUserId);
        Assert.AreEqual("read", snapshot.DeviceScope);
        Assert.AreEqual("/", snapshot.DeviceRoot);
        Assert.IsNull(snapshot.DeviceExpiresAt);
        Assert.IsNull(snapshot.DeviceRevokedAt);

        var down = await Fixture.MigrateDownAsync(CancellationToken.None);
        Assert.AreEqual(1, down.PlaybackCount);
        Assert.AreEqual(1, down.PreferenceCount);
        Assert.AreEqual(1, down.ConversationCount);
        Assert.AreEqual(1, down.DeviceTokenCount);
        Assert.IsFalse(down.UsersTableExists);
        Assert.IsFalse(down.ProfilesTableExists);

        // Re-upgrade proves Down left the legacy rows in a valid, recoverable state.
        await Fixture.UpgradeAsync(CancellationToken.None);
        var reupgraded = await Fixture.InspectAsync(CancellationToken.None);
        Assert.AreEqual(123d, reupgraded.PositionSeconds);
    }

    [TestMethod]
    public async Task CleanMigration_LeavesRegistrationOpen_AndCanDowngrade()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);
        Assert.AreEqual(0, await Fixture.GetUserCountAsync(CancellationToken.None));

        var registration = await Fixture.RegisterAndCreateSessionAsync(CancellationToken.None);
        Assert.AreEqual(IdentityDefaults.ProfileId, registration.PersistedProfileId);
        Assert.AreEqual(IdentityDefaults.ProfileId, registration.IssuedProfileId);
        Assert.IsTrue(registration.SessionIsActive);
        Assert.AreEqual(2, registration.UserCount);

        var registeredDown = await Fixture.AttemptUnsafeDowngradeAsync(
            CancellationToken.None);
        Assert.IsTrue(registeredDown.Rejected);
        Assert.IsTrue(registeredDown.CurrentMigrationStillApplied);
        Assert.AreEqual(2, registeredDown.UserCount);

        await Fixture.RecreateAsync(CancellationToken.None);
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);
        var down = await Fixture.MigrateDownAsync(CancellationToken.None);
        Assert.AreEqual(0, down.PlaybackCount);
        Assert.AreEqual(0, down.PreferenceCount);
        Assert.AreEqual(0, down.ConversationCount);
        Assert.AreEqual(0, down.DeviceTokenCount);
        Assert.IsFalse(down.UsersTableExists);
        Assert.IsFalse(down.ProfilesTableExists);
    }

    [TestMethod]
    public async Task ConcurrentAdminDemotions_CannotRemoveLastEnabledAdministrator()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);

        var result = await Fixture.DemoteTwoAdminsConcurrentlyAsync(CancellationToken.None);

        Assert.AreEqual(1, result.Results.Count(item => item == UpdateUserAccessResult.Updated));
        Assert.AreEqual(1, result.Results.Count(item => item == UpdateUserAccessResult.LastAdministrator));
        Assert.AreEqual(1, result.EnabledAdministratorCount);
    }

    [TestMethod]
    public async Task Profiles_HaveIndependentPlaybackPreferencesAndConversations()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);

        var result = await Fixture.ExerciseProfileIsolationAsync(CancellationToken.None);

        Assert.AreEqual(10d, result.FirstPosition);
        Assert.AreEqual(70d, result.SecondPosition);
        Assert.AreEqual("zh-Hans", result.FirstSubtitleLanguage);
        Assert.AreEqual("en", result.SecondSubtitleLanguage);
        Assert.AreEqual(1, result.FirstConversationCount);
        Assert.AreEqual(1, result.SecondConversationCount);
        Assert.IsTrue(result.CrossProfileConversationHidden);
        Assert.AreEqual(10d, result.FirstContinuePosition);
        Assert.AreEqual(70d, result.SecondContinuePosition);
    }

    [TestMethod]
    public async Task ProfileSwitchLogoutRevokeAndRefreshRotation_InvalidateOldCredentials()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);

        var result = await Fixture.ExerciseSessionLifecycleAsync(CancellationToken.None);

        Assert.IsTrue(result.WrongPinRejected);
        Assert.IsTrue(result.CorrectPinRotated);
        Assert.IsTrue(result.OldAccessRejected);
        Assert.IsTrue(result.NewProfileClaimIsActive);
        Assert.IsTrue(result.OldRefreshReplayRejected);
        Assert.IsTrue(result.LogoutRejectedAccess);
        Assert.IsTrue(result.LogoutRejectedRefresh);
        Assert.IsTrue(result.AdministratorRevokeRejectedAccess);
        Assert.IsTrue(result.AdministratorRevokeRejectedRefresh);
        Assert.AreEqual(1, result.ConcurrentRefreshSuccessCount);
    }

    [TestMethod]
    public async Task Downgrade_WithMultipleProfilesAndHistory_IsRejectedWithoutMutation()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);
        await Fixture.ExerciseProfileIsolationAsync(CancellationToken.None);

        var result = await Fixture.AttemptUnsafeDowngradeAsync(CancellationToken.None);

        Assert.IsTrue(result.Rejected);
        Assert.IsTrue(result.CurrentMigrationStillApplied);
        Assert.AreEqual(1, result.UserCount);
        Assert.AreEqual(2, result.ProfileCount);
        Assert.AreEqual(2, result.ProgressCount);
        Assert.AreEqual(2, result.PreferenceCount);
    }

    [TestMethod]
    public async Task Downgrade_WithScopedExpiringDeviceToken_IsRejectedWithoutWideningIt()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);
        await Fixture.SeedUnsafeScopedDeviceTokenAsync(CancellationToken.None);

        var result = await Fixture.AttemptUnsafeDowngradeAsync(CancellationToken.None);

        Assert.IsTrue(result.Rejected);
        Assert.IsTrue(result.CurrentMigrationStillApplied);
        Assert.AreEqual(1, result.DeviceTokenCount);
    }

    [TestMethod]
    public async Task ConcurrentFirstRegistration_ReturnsConflictInsteadOfServerError()
    {
        await Fixture.MigrateCleanDatabaseAsync(CancellationToken.None);

        var result = await Fixture.RegisterConcurrentlyAsync(CancellationToken.None);

        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(1, result.ConflictCount);
        Assert.AreEqual(1, result.UserCount);
        Assert.AreEqual(1, result.SessionCount);
    }
}
