namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed record NetworkCapabilityRequest(
    string Method,
    string Url,
    string? Body,
    string? ContentType);

internal sealed record PluginNetworkResponse(int Status, string? ContentType, string Body);

internal sealed record PluginBinaryResponse(string Base64);

internal sealed record PluginFileListEntry(string Name, bool IsDirectory);

internal sealed record PluginWriteResponse(int Written);

internal sealed record PluginExistsResponse(bool Exists, bool IsDirectory);

internal sealed record PluginPathRequest(string Path);
