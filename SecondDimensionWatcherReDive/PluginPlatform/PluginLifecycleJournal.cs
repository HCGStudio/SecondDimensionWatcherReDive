using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal static class PluginLifecycleJournalValues
{
    public const string Upgrade = "upgrade";
    public const string Uninstall = "uninstall";
    public const string Prepared = "prepared";
    public const string Committed = "committed";
}

internal sealed record PluginLifecycleJournal(
    string Operation,
    string PluginId,
    string Phase,
    PluginCatalogEntry[] OriginalEntries,
    RetainedPluginData? OriginalRetained,
    RetainedPluginData? IntendedRetained,
    bool DeleteData);

internal enum PluginLifecycleCheckpoint
{
    AfterMove,
    AfterCommit
}

/// <summary>
/// Test-only abrupt-termination signal. The manager deliberately bypasses its in-process
/// rollback for this exception so a new manager can exercise durable startup recovery.
/// </summary>
internal sealed class PluginProcessCrashSimulationException() : Exception("Simulated plugin host termination.");
