namespace SecondDimensionWatcherReDive.ConfigMigration;

public sealed class ConfigMigrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
