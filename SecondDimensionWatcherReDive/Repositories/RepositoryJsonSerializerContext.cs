using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

[JsonSerializable(typeof(LibrarySearchRepository.SearchCursor))]
[JsonSerializable(typeof(DownloadCompletionJobPayload))]
[JsonSerializable(typeof(string[]))]
internal partial class RepositoryJsonSerializerContext : JsonSerializerContext;
