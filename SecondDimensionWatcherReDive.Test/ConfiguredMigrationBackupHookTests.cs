using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.MigrationTasks;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ConfiguredMigrationBackupHookTests
{
    [TestMethod]
    public async Task ExecuteAsync_NoCommandAndBackupOptional_IsNoOp()
    {
        var hook = CreateHook(new MigrationOptions());

        await hook.ExecuteAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task ExecuteAsync_NoCommandAndBackupRequired_BlocksMigration()
    {
        var hook = CreateHook(new MigrationOptions { RequireBackup = true });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => hook.ExecuteAsync(CancellationToken.None));
    }

    private static ConfiguredMigrationBackupHook CreateHook(MigrationOptions options) => new(
        Options.Create(options),
        NullLogger<ConfiguredMigrationBackupHook>.Instance);
}
