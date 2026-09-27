using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.MigrationTasks;

[JsonSerializable(typeof(MigrateFileMappings.FileMappingCheckpoint))]
internal partial class MigrationJsonSerializerContext : JsonSerializerContext;
