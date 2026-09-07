namespace SecondDimensionWatcherReDive.Configuration;

public sealed class MigrationOptions
{
    public const string SectionName = "Migration";

    /// <summary>Maximum time spent waiting for the lease and running all migrations.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Executable invoked before schema and data migration work.</summary>
    public string? BackupExecutable { get; set; }

    /// <summary>Arguments passed directly to <see cref="BackupExecutable" /> without a shell.</summary>
    public List<string> BackupArguments { get; set; } = [];

    public TimeSpan BackupTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Refuse to migrate when no successful backup hook is configured.</summary>
    public bool RequireBackup { get; set; }
}
